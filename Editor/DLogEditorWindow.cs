using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;

namespace NoSlimes.Logging
{
    class ClearLogsOnBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) { DLogEditorWindow.ClearLogsIfEnabled(DLogEditorWindow.ClearOnBuildPrefKey); }
    }

    [InitializeOnLoad]
    public class DLogEditorWindow : EditorWindow
    {
        private const int MAX_LOG_ENTRIES = 5000;
        private static DLogEditorWindow _instance;

        private Vector2 _logScrollPos, _stackTraceScrollPos;
        // MODIFIED: Changed _logEntries and _selectedLogEntry from static to instance fields.
        // This allows Unity's EditorWindow serialization to automatically save and restore them during a domain reload.
        [SerializeField] private List<LogEntry> _logEntries = new();
        [SerializeField] private LogEntry _selectedLogEntry;

        private static readonly Dictionary<string, bool> _categoryToggles = new();
        private string _searchText = "";
        private const string SearchControlName = "DLogSearchField";

        public const string ClearOnPlayPrefKey = "DLog.ClearOnPlay";
        public const string ClearOnBuildPrefKey = "DLog.ClearOnBuild";
        public const string ClearOnRecompilePrefKey = "DLog.ClearOnRecompile";
        private const string ShowSourcePrefKey = "DLog.ShowSource";
        private const string CaptureUnityPrefKey = "DLog.CaptureUnity";
        private const string FocusWindowPrefKey = "DLog.FocusWindow";
        private const string CollapsePrefKey = "DLog.Collapse";
        private const string EnableDevLogsPrefKey = "DLog.EnableDevLogs";
        private const string CategoryTogglePrefKeyPrefix = "DLog.Category.";
        private const string ErrorPausePrefKey = "DLog.ErrorPause";

        private static bool _showSourceInLog, _captureUnityLogs, _focusWindowOnLog, _collapse, _errorPause;
        private static bool _clearOnPlay = true, _clearOnBuild = true, _clearOnRecompile;

        private static bool _isDLogMessage, _scrollToBottom, _prefsLoaded = false;
        private static readonly DLogCategory UnityLogCategory = new DLogCategory("Unity Log", new Color(0.7f, 0.7f, 0.7f));
        private static readonly DLogCategory UnityWarningCategory = new DLogCategory("Unity Warning", new Color(0.9f, 0.8f, 0.4f));
        private static readonly DLogCategory UnityErrorCategory = new DLogCategory("Unity Error", new Color(0.9f, 0.5f, 0.5f));
        private static List<DLogCategory> _allCategories;
        private GUIStyle _logBoxStyle, _logButtonStyle, _selectedLogButtonStyle, _stackTraceBoxStyle;

        static DLogEditorWindow()
        {
            DLog.AddToEditorWindowHook = AddDLog;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChange;
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded() { ClearLogsIfEnabled(ClearOnRecompilePrefKey); }
        [MenuItem("Window/DLog Console")] public static void ShowWindow() { GetWindow<DLogEditorWindow>("DLog Console"); }

        private void OnEnable()
        {
            _instance = this;
            titleContent = new GUIContent("DLog Console", EditorGUIUtility.IconContent("console.infoicon").image);
            if (!_prefsLoaded) { FindAllLogCategoriesInProject(); LoadPrefs(); _prefsLoaded = true; }
            if (_captureUnityLogs) { Application.logMessageReceivedThreaded -= HandleUnityLog; Application.logMessageReceivedThreaded += HandleUnityLog; }
        }

        private void OnDisable() { Application.logMessageReceivedThreaded -= HandleUnityLog; }

        private void OnGUI() { InitStylesIfNeeded(); HandleHotkeys(); DrawToolbar(); DrawLogAndDetailPanel(); }
        private void HandleHotkeys() { if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F && Event.current.control) { EditorGUI.FocusTextInControl(SearchControlName); Event.current.Use(); } }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var clearButtonContent = new GUIContent("Clear", "Clear all logs");
            float clearButtonWidth = EditorStyles.toolbarButton.CalcSize(clearButtonContent).x + 10f;
            if (GUILayout.Button(clearButtonContent, EditorStyles.toolbarButton, GUILayout.Width(clearButtonWidth)))
            {
                // This now correctly clears the instance fields because OnGUI is an instance method.
                _logEntries.Clear();
                _selectedLogEntry = null;
                Repaint();
            }

            if (EditorGUILayout.DropdownButton(GUIContent.none, FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(16f)))
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Clear on Play"), _clearOnPlay, () =>
                {
                    _clearOnPlay = !_clearOnPlay;
                    EditorPrefs.SetBool(ClearOnPlayPrefKey, _clearOnPlay);
                });
                menu.AddItem(new GUIContent("Clear on Build"), _clearOnBuild, () =>
                {
                    _clearOnBuild = !_clearOnBuild;
                    EditorPrefs.SetBool(ClearOnBuildPrefKey, _clearOnBuild);
                });
                menu.AddItem(new GUIContent("Clear on Recompile"), _clearOnRecompile, () =>
                {
                    _clearOnRecompile = !_clearOnRecompile;
                    EditorPrefs.SetBool(ClearOnRecompilePrefKey, _clearOnRecompile);
                });

                menu.ShowAsContext();
            }

            GUILayout.Space(5f);

            var collapseContent = new GUIContent("Collapse", "Group identical log messages together");
            float collapseWidth = EditorStyles.toolbarButton.CalcSize(collapseContent).x + 10f;
            bool newCollapseState = GUILayout.Toggle(_collapse, collapseContent, EditorStyles.toolbarButton, GUILayout.Width(collapseWidth));
            if (newCollapseState != _collapse)
            {
                _collapse = newCollapseState;
                EditorPrefs.SetBool(CollapsePrefKey, _collapse);
            }

            var categoryFiltersContent = new GUIContent("Category Filters", "Filter logs by category");
            if (EditorGUILayout.DropdownButton(categoryFiltersContent, FocusType.Passive, EditorStyles.toolbarDropDown))
            {
                GenericMenu menu = new GenericMenu();
                DrawCategorySubMenu(menu, "Relevant", GetRelevantCategories());
                DrawCategorySubMenu(menu, "All", _allCategories.Select(c => c.Name).ToList());
                menu.ShowAsContext();
            }

            var errorPauseContent = new GUIContent("Error Pause", "Pause the editor when an error log is received");
            float errorPauseWidth = EditorStyles.toolbarButton.CalcSize(errorPauseContent).x + 10f;
            bool newErrorPauseValue = GUILayout.Toggle(_errorPause, errorPauseContent, EditorStyles.toolbarButton, GUILayout.Width(errorPauseWidth));
            if (newErrorPauseValue != _errorPause)
            {
                _errorPause = newErrorPauseValue;
                EditorPrefs.SetBool(ErrorPausePrefKey, _errorPause);
            }

            GUILayout.FlexibleSpace();
            GUI.SetNextControlName(SearchControlName);
            _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));

            var srcToggleContent = new GUIContent("Src", "Show source file info in log entries");
            float srcToggleWidth = EditorStyles.toolbarButton.CalcSize(srcToggleContent).x + 10f;
            bool newSrcValue = GUILayout.Toggle(_showSourceInLog, srcToggleContent, EditorStyles.toolbarButton, GUILayout.Width(srcToggleWidth));
            if (newSrcValue != _showSourceInLog)
            {
                _showSourceInLog = newSrcValue;
                EditorPrefs.SetBool(ShowSourcePrefKey, _showSourceInLog);
            }

            var focusToggleContent = new GUIContent("Focus", "Focus this window when a new log is added");
            float focusToggleWidth = EditorStyles.toolbarButton.CalcSize(focusToggleContent).x + 10f;
            bool newFocusValue = GUILayout.Toggle(_focusWindowOnLog, focusToggleContent, EditorStyles.toolbarButton, GUILayout.Width(focusToggleWidth));
            if (newFocusValue != _focusWindowOnLog)
            {
                _focusWindowOnLog = newFocusValue;
                EditorPrefs.SetBool(FocusWindowPrefKey, _focusWindowOnLog);
            }

            var unityToggleContent = new GUIContent("Unity Logs", "Capture native Unity Debug.Log messages");
            float unityToggleWidth = EditorStyles.toolbarButton.CalcSize(unityToggleContent).x + 10f;
            bool newCaptureState = GUILayout.Toggle(_captureUnityLogs, unityToggleContent, EditorStyles.toolbarButton, GUILayout.Width(unityToggleWidth));
            if (newCaptureState != _captureUnityLogs)
            {
                _captureUnityLogs = newCaptureState;
                EditorPrefs.SetBool(CaptureUnityPrefKey, _captureUnityLogs);
                if (_captureUnityLogs) { Application.logMessageReceivedThreaded += HandleUnityLog; }
                else { Application.logMessageReceivedThreaded -= HandleUnityLog; }
            }

            var devLogsContent = new GUIContent("Dev Logs", "Enable or disable developer-only logs");
            float devLogsToggleWidth = EditorStyles.toolbarButton.CalcSize(devLogsContent).x + 10f;
            bool newDevLogsValue = GUILayout.Toggle(DLog.EnableDevLogs, devLogsContent, EditorStyles.toolbarButton, GUILayout.Width(devLogsToggleWidth));
            if (newDevLogsValue != DLog.EnableDevLogs)
            {
                DLog.EnableDevLogs = newDevLogsValue;
                EditorPrefs.SetBool(EnableDevLogsPrefKey, DLog.EnableDevLogs);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategorySubMenu(GenericMenu parentMenu, string subMenuTitle, List<string> categoryNames)
        {
            System.Action<string, string> addItemToMenu = (categoryName, menuPath) =>
            {
                if (!_categoryToggles.ContainsKey(categoryName)) { _categoryToggles[categoryName] = true; }
                parentMenu.AddItem(new GUIContent(menuPath), _categoryToggles[categoryName], OnCategoryToggled, categoryName);
            };

            var standardDLogCategories = new[] { "Log", "Warning", "Error" };
            var unityCategories = new[] { UnityLogCategory.Name, UnityWarningCategory.Name, UnityErrorCategory.Name };
            bool hasAddedStandard = false;
            foreach (var stdName in standardDLogCategories)
            {
                if (categoryNames.Contains(stdName))
                {
                    addItemToMenu(stdName, $"{subMenuTitle}/{stdName}");
                    hasAddedStandard = true;
                }
            }

            bool hasAddedUnity = false;
            foreach (var unityName in unityCategories)
            {
                if (categoryNames.Contains(unityName))
                {
                    addItemToMenu(unityName, $"{subMenuTitle}/Unity/{unityName}");
                    hasAddedUnity = true;
                }
            }

            var allSpecialCategories = standardDLogCategories.Concat(unityCategories);
            var customCategories = categoryNames.Except(allSpecialCategories).OrderBy(name => name).ToList();
            if ((hasAddedStandard || hasAddedUnity) && customCategories.Any())
            {
                parentMenu.AddSeparator($"{subMenuTitle}/");
            }

            foreach (var customName in customCategories)
            {
                addItemToMenu(customName, $"{subMenuTitle}/{customName}");
            }
        }

        private void OnCategoryToggled(object userData) { string categoryName = (string)userData; if (_categoryToggles.ContainsKey(categoryName)) { _categoryToggles[categoryName] = !_categoryToggles[categoryName]; EditorPrefs.SetBool(CategoryTogglePrefKeyPrefix + categoryName, _categoryToggles[categoryName]); } }

        private void DrawLogAndDetailPanel()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            _logScrollPos = EditorGUILayout.BeginScrollView(_logScrollPos, _logBoxStyle, GUILayout.ExpandHeight(true));
            var filteredLogs = _logEntries.Where(entry => (_categoryToggles.TryGetValue(entry.Category, out bool e) && e) && (string.IsNullOrEmpty(_searchText) || entry.Message.ToLowerInvariant().Contains(_searchText.ToLowerInvariant())));
            if (_collapse)
            {
                var groupedLogs = filteredLogs.GroupBy(log => log.Message);
                foreach (var group in groupedLogs)
                {
                    LogEntry representativeEntry = group.First(); int count = group.Count();
                    string displayMessage = representativeEntry.GetFormattedMessage(_showSourceInLog); if (count > 1) { displayMessage += $" <color=grey>({count})</color>"; }
                    bool isSelected = _selectedLogEntry != null && group.Any(log => log == _selectedLogEntry);
                    GUIStyle style = isSelected ? _selectedLogButtonStyle : _logButtonStyle;
                    GUILayout.Box(displayMessage, style);
                    Rect entryRect = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.MouseDown && entryRect.Contains(Event.current.mousePosition))
                    {
                        GUI.FocusControl(null);
                        if (Event.current.button == 0)
                        {
                            var lastEntryInGroup = group.Last(); _selectedLogEntry = isSelected ? null : lastEntryInGroup;
                            if (!isSelected && _selectedLogEntry != null && _selectedLogEntry.Context != null) { EditorGUIUtility.PingObject(_selectedLogEntry.Context); }
                            if (Event.current.clickCount == 2 && _selectedLogEntry != null) { AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(_selectedLogEntry.SourceFilePath), _selectedLogEntry.SourceLineNumber); }
                            Event.current.Use(); Repaint();
                        }
                    }
                }
            }
            else
            {
                foreach (var entry in filteredLogs)
                {
                    bool isSelected = entry == _selectedLogEntry; GUIStyle style = isSelected ? _selectedLogButtonStyle : _logButtonStyle;
                    GUILayout.Box(entry.GetFormattedMessage(_showSourceInLog), style);
                    Rect entryRect = GUILayoutUtility.GetLastRect();
                    if (Event.current.type == EventType.MouseDown && entryRect.Contains(Event.current.mousePosition))
                    {
                        GUI.FocusControl(null);
                        if (Event.current.button == 0)
                        {
                            if (Event.current.clickCount == 1) { _selectedLogEntry = isSelected ? null : entry; if (!isSelected && _selectedLogEntry != null && _selectedLogEntry.Context != null) { EditorGUIUtility.PingObject(_selectedLogEntry.Context); } }
                            else if (Event.current.clickCount == 2) { AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(entry.SourceFilePath), entry.SourceLineNumber); }
                            Event.current.Use(); Repaint();
                        }
                    }
                }
            }
            if (_scrollToBottom && Event.current.type == EventType.Repaint) { _logScrollPos.y = float.MaxValue; _scrollToBottom = false; }
            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
            if (_selectedLogEntry != null)
            {
                GUILayout.BeginVertical(GUILayout.Width(position.width * 0.5f));
                _stackTraceScrollPos = EditorGUILayout.BeginScrollView(_stackTraceScrollPos, _stackTraceBoxStyle, GUILayout.ExpandHeight(true));

                GUIStyle richLabelStyle = new GUIStyle(EditorStyles.textArea)
                {
                    richText = true,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                };

                EditorGUILayout.SelectableLabel(_selectedLogEntry.StackTrace, richLabelStyle, GUILayout.ExpandHeight(true));

                EditorGUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void InitStylesIfNeeded()
        {
            if (_logBoxStyle == null) { _logBoxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(5, 5, 5, 5), normal = { background = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.1f, 1f)) } }; }
            if (_logButtonStyle == null) { _logButtonStyle = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true, fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(5, 5, 2, 2) }; }
            if (_selectedLogButtonStyle == null) { _selectedLogButtonStyle = new GUIStyle(_logButtonStyle); var selectedBg = MakeTex(1, 1, new Color(0.25f, 0.4f, 0.65f, 1f)); _selectedLogButtonStyle.normal.background = selectedBg; _selectedLogButtonStyle.hover.background = selectedBg; _selectedLogButtonStyle.active.background = selectedBg; }
            if (_stackTraceBoxStyle == null) { _stackTraceBoxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(5, 5, 5, 5), margin = new RectOffset(0, 0, 5, 0), normal = { background = MakeTex(1, 1, new Color(0.12f, 0.12f, 0.12f, 1f)) } }; }
        }

        private static Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height]; for (int i = 0; i < pix.Length; ++i) { pix[i] = col; }
            Texture2D result = new(width, height) { hideFlags = HideFlags.HideAndDontSave }; result.SetPixels(pix); result.Apply(); return result;
        }

        private static void AddDLog(object message, DLogCategory category, Object context, string filePath, int lineNumber)
        {
            _isDLogMessage = true;
            AddLogInternal(message, category, context, filePath, lineNumber, System.Environment.StackTrace);
        }

        private static void HandleUnityLog(string logString, string stackTrace, LogType type)
        {
            if (_isDLogMessage) { _isDLogMessage = false; return; }
            var category = GetCategoryFromLogType(type);
            ParseStackTrace(stackTrace, out string filePath, out int lineNumber, out Object context);
            AddLogInternal(logString, category, context, filePath, lineNumber, stackTrace);
        }

        private static void AddLogInternal(object message, DLogCategory category, Object context, string filePath, int lineNumber, string rawStackTrace)
        {
            EditorApplication.delayCall += () =>
            {
                // MODIFIED: Check for _instance and use its fields.
                if (_instance == null) return;

                if (_instance._logEntries.Count >= MAX_LOG_ENTRIES) { _instance._logEntries.RemoveAt(0); }
                string normalizedPath = filePath.Replace('\\', '/');
                string projectRelativePath = normalizedPath.StartsWith(Application.dataPath) ? "Assets" + normalizedPath.Substring(Application.dataPath.Length) : normalizedPath;
                string formattedStackTrace = FormatStackTrace(rawStackTrace);

                _instance._logEntries.Add(new LogEntry(message.ToString(), category.Name, category.ColorHex, context, projectRelativePath, lineNumber, formattedStackTrace));

                if (!_categoryToggles.ContainsKey(category.Name)) { _categoryToggles[category.Name] = true; }
                _scrollToBottom = true;
                if (_instance != null) { _instance.Repaint(); }
                if (_focusWindowOnLog && _instance != null && !_instance.hasFocus) { _instance.Focus(); }

                bool isCategoryError = category == UnityErrorCategory || category == DLogCategory.Error;
                if (_errorPause && isCategoryError && EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.isPaused = true;
                }
            };
        }

        private static string FormatStackTrace(string rawStackTrace)
        {
            if (string.IsNullOrEmpty(rawStackTrace)) return "";
            var sb = new StringBuilder();
            var lines = rawStackTrace.Split('\n');
            string[] noisePatterns = { "NoSlimes.Logging", "System.Environment", "System.Runtime.CompilerServices", "System.Threading", "UnityEngine.UnitySynchronizationContext", "UnityEngine.Debug", "UnityEngine.GUI" };
            var dlogRegex = new Regex(@"at (.*) \(\) \[.*\] in (.+):(\d+)");
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmedLine) || noisePatterns.Any(p => trimmedLine.Contains(p))) continue;
                var dlogMatch = dlogRegex.Match(trimmedLine);
                if (dlogMatch.Success)
                {
                    string methodPath = dlogMatch.Groups[1].Value;
                    string filePath = dlogMatch.Groups[2].Value.Replace('\\', '/');
                    string lineNumber = dlogMatch.Groups[3].Value;
                    if (filePath.StartsWith(Application.dataPath)) { filePath = "Assets" + filePath.Substring(Application.dataPath.Length); }
                    sb.AppendLine($"{methodPath} () (at <a href=\"{filePath}\" line=\"{lineNumber}\">{filePath}:{lineNumber}</a>)");

                }
                else { sb.AppendLine(trimmedLine); }
            }
            return sb.ToString();
        }

        // MODIFIED: This static method now acts on the window instance.
        private static void ClearLogs()
        {
            if (_instance == null) return;
            _instance._logEntries.Clear();
            _instance._selectedLogEntry = null;
            _instance.Repaint();
        }

        public static void ClearLogsIfEnabled(string prefKey)
        {
            if (EditorPrefs.GetBool(prefKey, false))
            {
                EditorApplication.delayCall += ClearLogs;
            }
        }

        private static void HandlePlayModeStateChange(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                ClearLogsIfEnabled(ClearOnPlayPrefKey);
            }
        }

        private static void LoadPrefs()
        {
            _collapse = EditorPrefs.GetBool(CollapsePrefKey, false);
            _clearOnPlay = EditorPrefs.GetBool(ClearOnPlayPrefKey, true);
            _clearOnBuild = EditorPrefs.GetBool(ClearOnBuildPrefKey, true);
            _clearOnRecompile = EditorPrefs.GetBool(ClearOnRecompilePrefKey, true);
            _showSourceInLog = EditorPrefs.GetBool(ShowSourcePrefKey, true);
            _captureUnityLogs = EditorPrefs.GetBool(CaptureUnityPrefKey, true);
            _focusWindowOnLog = EditorPrefs.GetBool(FocusWindowPrefKey, false);
            DLog.EnableDevLogs = EditorPrefs.GetBool(EnableDevLogsPrefKey, true);
            _errorPause = EditorPrefs.GetBool(ErrorPausePrefKey, false);

            if (_allCategories != null) { foreach (var category in _allCategories) { _categoryToggles[category.Name] = EditorPrefs.GetBool(CategoryTogglePrefKeyPrefix + category.Name, true); } }
        }

        private static DLogCategory GetCategoryFromLogType(LogType type)
        {
            switch (type) { case LogType.Error: case LogType.Exception: case LogType.Assert: return UnityErrorCategory; case LogType.Warning: return UnityWarningCategory; default: return UnityLogCategory; }
        }

        private static void ParseStackTrace(string stackTrace, out string filePath, out int lineNumber, out Object context)
        {
            filePath = ""; lineNumber = 0; context = null; if (string.IsNullOrEmpty(stackTrace)) return;
            var match = Regex.Match(stackTrace, @"\(at (.+):(\d+)\)");
            if (match.Success) { filePath = match.Groups[1].Value; lineNumber = int.Parse(match.Groups[2].Value); }
        }

        private static void FindAllLogCategoriesInProject()
        {
            _allCategories = new List<DLogCategory>();
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            var projectAssemblies = assemblies.Where(a => !a.FullName.StartsWith("Unity") && !a.FullName.StartsWith("System") && !a.FullName.StartsWith("Microsoft"));
            foreach (var assembly in projectAssemblies)
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(DLogCategory));
                        foreach (var field in fields)
                        {
                            var category = field.GetValue(null) as DLogCategory;
                            if (category != null && !_allCategories.Any(c => c.Name == category.Name)) { _allCategories.Add(category); }
                        }
                    }
                }
                catch (System.Exception) { /* Ignore */ }
            }
            _allCategories.AddRange(new[] { UnityLogCategory, UnityWarningCategory, UnityErrorCategory });
            foreach (var cat in _allCategories) { if (!_categoryToggles.ContainsKey(cat.Name)) { _categoryToggles[cat.Name] = true; } }
        }

        private List<string> GetRelevantCategories() => _logEntries.Select(log => log.Category).Distinct().ToList();

        // MODIFIED: Made the LogEntry class serializable so Unity can save/load it.
        [System.Serializable]
        private class LogEntry
        {
            public string Message;
            public string Category;
            public string ColorHex;
            public Object Context;
            public string SourceFilePath;
            public int SourceLineNumber;
            public string StackTrace;

            public LogEntry() { }

            public LogEntry(string message, string category, string colorHex, Object context, string sourceFilePath, int sourceLineNumber, string stackTrace)
            {
                Message = message; Category = category; ColorHex = colorHex; Context = context; SourceFilePath = sourceFilePath; SourceLineNumber = sourceLineNumber; StackTrace = stackTrace;
            }
            public string GetFormattedMessage(bool showSource) { string baseMessage = $"<color={ColorHex}><b>[{Category}]</b></color> {Message}"; if (showSource) { string fileName = Path.GetFileName(SourceFilePath); return $"{baseMessage}  <color={ColorHex}><i>({fileName}:{SourceLineNumber})</i></color>"; } return baseMessage; }
        }
    }
}