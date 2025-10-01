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
        [SerializeField] private List<LogEntry> _logEntries = new();
        [SerializeField] private LogEntry _selectedLogEntry;

        private static readonly Dictionary<string, bool> _categoryToggles = new();
        private string _searchText = "";
        private const string SearchControlName = "DLogSearchField";

        private Rect _splitterRect;
        private bool _isResizing;
        [SerializeField] private float _splitterPosition = 200f;
        private const float SplitterHeight = 5f;

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
        private const float LogEntryHeight = 20f;

        private List<object> _cachedVisibleEntries = new();
        private bool _isCacheDirty = true;

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
            MarkCacheDirty();

            _splitterPosition = position.height / 2;
        }

        private void OnDisable() { Application.logMessageReceivedThreaded -= HandleUnityLog; }

        private void OnGUI()
        {
            InitStylesIfNeeded();
            HandleHotkeys();
            DrawToolbar();

            if (_isCacheDirty && Event.current.type == EventType.Layout)
            {
                RebuildCache();
            }

            DrawLogAndDetailPanel();
            HandleResize();
        }

        private void DrawSplitter()
        {
            _splitterRect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.horizontalSlider, GUILayout.Height(SplitterHeight));
            EditorGUIUtility.AddCursorRect(_splitterRect, MouseCursor.ResizeVertical);
        }

        private void HandleResize()
        {
            if (Event.current.type == EventType.MouseDown && _splitterRect.Contains(Event.current.mousePosition))
            {
                _isResizing = true;
                Event.current.Use();
            }

            if (_isResizing)
            {
                _splitterPosition = Event.current.mousePosition.y;
                _splitterPosition = Mathf.Clamp(_splitterPosition, 100, position.height - 100);
                Repaint();
            }

            if (Event.current.type == EventType.MouseUp)
            {
                _isResizing = false;
            }
        }

        private void MarkCacheDirty() => _isCacheDirty = true;

        private void RebuildCache()
        {
            var filteredLogs = _logEntries.Where(entry =>
                (_categoryToggles.TryGetValue(entry.Category, out bool e) && e) &&
                (string.IsNullOrEmpty(_searchText) || entry.Message.ToLowerInvariant().Contains(_searchText.ToLowerInvariant()))
            );

            if (_collapse)
            {
                _cachedVisibleEntries = filteredLogs.GroupBy(log => log.Message).Cast<object>().ToList();
            }
            else
            {
                _cachedVisibleEntries = filteredLogs.Cast<object>().ToList();
            }
            _isCacheDirty = false;
        }

        private void HandleHotkeys() { if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.F && Event.current.control) { EditorGUI.FocusTextInControl(SearchControlName); Event.current.Use(); } }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();

            if (GUILayout.Button("Clear", EditorStyles.toolbarButton))
            {
                ClearLogs();
            }

            if (EditorGUILayout.DropdownButton(GUIContent.none, FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(16f)))
            {
                GenericMenu menu = new GenericMenu();
                menu.AddItem(new GUIContent("Clear on Play"), _clearOnPlay, () => { _clearOnPlay = !_clearOnPlay; EditorPrefs.SetBool(ClearOnPlayPrefKey, _clearOnPlay); });
                menu.AddItem(new GUIContent("Clear on Build"), _clearOnBuild, () => { _clearOnBuild = !_clearOnBuild; EditorPrefs.SetBool(ClearOnBuildPrefKey, _clearOnBuild); });
                menu.AddItem(new GUIContent("Clear on Recompile"), _clearOnRecompile, () => { _clearOnRecompile = !_clearOnRecompile; EditorPrefs.SetBool(ClearOnRecompilePrefKey, _clearOnRecompile); });
                menu.ShowAsContext();
            }

            GUILayout.Space(5f);

            _collapse = GUILayout.Toggle(_collapse, "Collapse", EditorStyles.toolbarButton);

            if (EditorGUILayout.DropdownButton(new GUIContent("Category Filters"), FocusType.Passive, EditorStyles.toolbarDropDown))
            {
                GenericMenu menu = new GenericMenu();
                DrawCategorySubMenu(menu, "Relevant", GetRelevantCategories());
                DrawCategorySubMenu(menu, "All", _allCategories.Select(c => c.Name).ToList());
                menu.ShowAsContext();
            }

            _errorPause = GUILayout.Toggle(_errorPause, "Error Pause", EditorStyles.toolbarButton);

            GUILayout.FlexibleSpace();
            GUI.SetNextControlName(SearchControlName);
            _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField, GUILayout.ExpandWidth(true));

            _showSourceInLog = GUILayout.Toggle(_showSourceInLog, "Src", EditorStyles.toolbarButton);
            _focusWindowOnLog = GUILayout.Toggle(_focusWindowOnLog, "Focus", EditorStyles.toolbarButton);

            bool newCaptureState = GUILayout.Toggle(_captureUnityLogs, "Unity Logs", EditorStyles.toolbarButton);
            if (newCaptureState != _captureUnityLogs)
            {
                _captureUnityLogs = newCaptureState;
                if (_captureUnityLogs) Application.logMessageReceivedThreaded += HandleUnityLog;
                else Application.logMessageReceivedThreaded -= HandleUnityLog;
            }

            DLog.EnableDevLogs = GUILayout.Toggle(DLog.EnableDevLogs, "Dev Logs", EditorStyles.toolbarButton);

            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(CollapsePrefKey, _collapse);
                EditorPrefs.SetBool(ErrorPausePrefKey, _errorPause);
                EditorPrefs.SetBool(ShowSourcePrefKey, _showSourceInLog);
                EditorPrefs.SetBool(FocusWindowPrefKey, _focusWindowOnLog);
                EditorPrefs.SetBool(CaptureUnityPrefKey, _captureUnityLogs);
                EditorPrefs.SetBool(EnableDevLogsPrefKey, DLog.EnableDevLogs);
                MarkCacheDirty();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategorySubMenu(GenericMenu parentMenu, string subMenuTitle, List<string> categoryNames)
        {
            void AddItemToMenu(string categoryName, string menuPath)
            {
                if (!_categoryToggles.ContainsKey(categoryName)) { _categoryToggles[categoryName] = true; }
                parentMenu.AddItem(new GUIContent(menuPath), _categoryToggles[categoryName], OnCategoryToggled, categoryName);
            }

            var standardDLogCategories = new[] { "Log", "Warning", "Error" };
            var unityCategories = new[] { UnityLogCategory.Name, UnityWarningCategory.Name, UnityErrorCategory.Name };
            bool hasAddedStandard = false;
            foreach (var stdName in standardDLogCategories) { if (categoryNames.Contains(stdName)) { AddItemToMenu(stdName, $"{subMenuTitle}/{stdName}"); hasAddedStandard = true; } }
            bool hasAddedUnity = false;
            foreach (var unityName in unityCategories) { if (categoryNames.Contains(unityName)) { AddItemToMenu(unityName, $"{subMenuTitle}/Unity/{unityName}"); hasAddedUnity = true; } }
            var allSpecialCategories = standardDLogCategories.Concat(unityCategories);
            var customCategories = categoryNames.Except(allSpecialCategories).OrderBy(name => name).ToList();
            if ((hasAddedStandard || hasAddedUnity) && customCategories.Any()) { parentMenu.AddSeparator($"{subMenuTitle}/"); }
            foreach (var customName in customCategories) { AddItemToMenu(customName, $"{subMenuTitle}/{customName}"); }
        }

        private void OnCategoryToggled(object userData)
        {
            string categoryName = (string)userData;
            if (_categoryToggles.ContainsKey(categoryName))
            {
                _categoryToggles[categoryName] = !_categoryToggles[categoryName];
                EditorPrefs.SetBool(CategoryTogglePrefKeyPrefix + categoryName, _categoryToggles[categoryName]);
                MarkCacheDirty();
            }
        }

        private void DrawLogAndDetailPanel()
        {
            EditorGUILayout.BeginVertical();

            float topPanelHeight = _selectedLogEntry != null ? _splitterPosition - EditorStyles.toolbar.fixedHeight : position.height - EditorStyles.toolbar.fixedHeight - 20;
            EditorGUILayout.BeginVertical(GUILayout.Height(topPanelHeight));
            _logScrollPos = EditorGUILayout.BeginScrollView(_logScrollPos, _logBoxStyle, GUILayout.ExpandHeight(true));

            int totalCount = _cachedVisibleEntries.Count;
            float totalContentHeight = totalCount * LogEntryHeight;

            GUILayout.Box(GUIContent.none, GUIStyle.none, GUILayout.Height(totalContentHeight), GUILayout.ExpandWidth(true));

            Rect viewRect = GUILayoutUtility.GetLastRect();

            GUI.BeginGroup(viewRect);

            int firstVisibleIndex = Mathf.Max(0, (int)(_logScrollPos.y / LogEntryHeight));
            int lastVisibleIndex = Mathf.Min(totalCount - 1, firstVisibleIndex + Mathf.CeilToInt(viewRect.height / LogEntryHeight));

            for (int i = firstVisibleIndex; i <= lastVisibleIndex; i++)
            {
                object item = _cachedVisibleEntries[i];
                bool isSelected;
                string displayMessage;

                if (item is IGrouping<string, LogEntry> group)
                {
                    LogEntry representativeEntry = group.First();
                    int count = group.Count();
                    displayMessage = representativeEntry.GetFormattedMessage(_showSourceInLog);
                    if (count > 1) { displayMessage += $" <color=grey>({count})</color>"; }
                    isSelected = _selectedLogEntry != null && group.Any(log => log == _selectedLogEntry);
                }
                else
                {
                    var entry = (LogEntry)item;
                    displayMessage = entry.GetFormattedMessage(_showSourceInLog);
                    isSelected = entry == _selectedLogEntry;
                }

                Rect entryRect = new Rect(0, i * LogEntryHeight, viewRect.width, LogEntryHeight);
                GUIStyle style = isSelected ? _selectedLogButtonStyle : _logButtonStyle;

                if (Event.current.type == EventType.Repaint)
                {
                    style.Draw(entryRect, new GUIContent(displayMessage), false, false, isSelected, false);
                }
                else if (Event.current.type == EventType.MouseDown && entryRect.Contains(Event.current.mousePosition))
                {
                    HandleLogClick(item, isSelected);
                    Event.current.Use();
                }
            }

            GUI.EndGroup();

            if (_scrollToBottom && Event.current.type == EventType.Repaint)
            {
                _logScrollPos.y = totalContentHeight;
                _scrollToBottom = false;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();

            if (_selectedLogEntry != null)
            {
                DrawSplitter();
                DrawDetailPanel();
            }

            EditorGUILayout.EndVertical();
        }

        private void HandleLogClick(object item, bool isSelected)
        {
            GUI.FocusControl(null);
            LogEntry entryToSelect = item is IGrouping<string, LogEntry> group ? group.Last() : (LogEntry)item;

            if (Event.current.clickCount == 1)
            {
                var newSelection = isSelected ? null : entryToSelect;
                if (newSelection != _selectedLogEntry)
                {
                    bool wasAlreadyOpen = _selectedLogEntry != null;
                    _selectedLogEntry = newSelection;

                    if (_selectedLogEntry?.Context != null)
                    {
                        EditorGUIUtility.PingObject(_selectedLogEntry.Context);
                    }

                    if (!wasAlreadyOpen && _selectedLogEntry != null)
                    {
                        EnsureLogVisible(_selectedLogEntry);
                    }

                    Repaint();
                }
            }
            else if (Event.current.clickCount == 2)
            {
                if (_selectedLogEntry != null && !string.IsNullOrEmpty(_selectedLogEntry.SourceFilePath))
                {
                    AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(_selectedLogEntry.SourceFilePath), _selectedLogEntry.SourceLineNumber);
                }
            }
        }

        private void EnsureLogVisible(LogEntry logEntry)
        {
            int index = -1;
            for (int i = 0; i < _cachedVisibleEntries.Count; i++)
            {
                object item = _cachedVisibleEntries[i];
                if (item is IGrouping<string, LogEntry> group && group.Contains(logEntry))
                {
                    index = i;
                    break;
                }
                if (item is LogEntry entry && entry == logEntry)
                {
                    index = i;
                    break;
                }
            }

            if (index == -1) return;

            float logYPos = index * LogEntryHeight + 10;
            float topPanelHeight = _splitterPosition - EditorStyles.toolbar.fixedHeight;
            float currentScrollPos = _logScrollPos.y;

            if (logYPos < currentScrollPos)
            {
                _logScrollPos.y = logYPos;
            }

            else if (logYPos + LogEntryHeight > currentScrollPos + topPanelHeight)
            {
                _logScrollPos.y = logYPos - topPanelHeight + LogEntryHeight;
            }
        }

        private void DrawDetailPanel()
        {
            GUILayout.BeginVertical(_stackTraceBoxStyle);

            _stackTraceScrollPos = EditorGUILayout.BeginScrollView(_stackTraceScrollPos, GUILayout.ExpandHeight(true));

            GUIStyle richLabelStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };

            var content = new GUIContent(_selectedLogEntry.StackTrace);
            float requiredHeight = richLabelStyle.CalcHeight(content, position.width * 0.5f - 20f);

            EditorGUILayout.SelectableLabel(content.text, richLabelStyle, GUILayout.Height(requiredHeight));

            EditorGUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void InitStylesIfNeeded()
        {
            if (_logBoxStyle == null) { _logBoxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(5, 5, 5, 5), normal = { background = MakeTex(1, 1, new Color(0.1f, 0.1f, 0.1f, 1f)) } }; }
            if (_logButtonStyle == null) { _logButtonStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip, richText = true, fontSize = 12, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(5, 5, 2, 2) }; }
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
                if (_instance == null) return;

                if (_instance._logEntries.Count >= MAX_LOG_ENTRIES) { _instance._logEntries.RemoveAt(0); }
                string normalizedPath = filePath.Replace('\\', '/');
                string projectRelativePath = normalizedPath.StartsWith(Application.dataPath) ? "Assets" + normalizedPath.Substring(Application.dataPath.Length) : normalizedPath;
                string formattedStackTrace = FormatStackTrace(rawStackTrace);
                _instance._logEntries.Add(new LogEntry(message.ToString(), category.Name, category.ColorHex, context, projectRelativePath, lineNumber, formattedStackTrace));
                if (!_categoryToggles.ContainsKey(category.Name)) { _categoryToggles[category.Name] = true; }

                _instance.MarkCacheDirty();

                _scrollToBottom = true;

                _instance.Repaint();

                if (_focusWindowOnLog && !_instance.hasFocus) { _instance.Focus(); }
                bool isCategoryError = category == UnityErrorCategory || category == DLogCategory.Error;
                if (_errorPause && isCategoryError && EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPaused = true; }
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

        private void ClearLogs()
        {
            if (_logEntries.Count == 0 && _selectedLogEntry == null) return;
            _logEntries.Clear();
            _selectedLogEntry = null;
            MarkCacheDirty();
            Repaint();
        }

        public static void ClearLogsIfEnabled(string prefKey)
        {
            if (EditorPrefs.GetBool(prefKey, false))
            {
                _instance?.ClearLogs();
            }
        }

        private static void HandlePlayModeStateChange(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredPlayMode)
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
            if (match.Success) { filePath = match.Groups[1].Value; int.TryParse(match.Groups[2].Value, out lineNumber); }
        }

        private static void FindAllLogCategoriesInProject()
        {
            _allCategories = new List<DLogCategory>();
            var assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            var projectAssemblies = assemblies.Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location) && (a.FullName.Contains("Assembly-CSharp") || a.FullName.Contains("NoSlimes")));
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
            _allCategories = _allCategories.Distinct().ToList();
            foreach (var cat in _allCategories) { if (!_categoryToggles.ContainsKey(cat.Name)) { _categoryToggles[cat.Name] = true; } }
        }

        private List<string> GetRelevantCategories() => _logEntries.Select(log => log.Category).Distinct().ToList();

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
            { Message = message; Category = category; ColorHex = colorHex; Context = context; SourceFilePath = sourceFilePath; SourceLineNumber = sourceLineNumber; StackTrace = stackTrace; }

            public string GetFormattedMessage(bool showSource)
            {
                string baseMessage = $"<color={ColorHex}><b>[{Category}]</b></color> {Message}";
                if (showSource && !string.IsNullOrEmpty(SourceFilePath))
                {
                    try { string fileName = Path.GetFileName(SourceFilePath); return $"{baseMessage}  <color={ColorHex}><i>({fileName}:{SourceLineNumber})</i></color>"; }
                    catch (System.ArgumentException) { return baseMessage; }
                }
                return baseMessage;
            }
        }
    }
}