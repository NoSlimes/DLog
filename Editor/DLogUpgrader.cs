using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.AI;

namespace NoSlimes.Logging.Editor
{
    public class DLogUpgrader : EditorWindow
    {
        private static readonly Dictionary<string, string> DevMethodMap = new()
        {
            { "Log", nameof(DLogger.LogDev) },
            { "LogWarning", nameof(DLogger.LogDevWarning) },
            { "LogError", nameof(DLogger.LogDevError) },
        };

        private static readonly Dictionary<string, string> DowngradeMap = new()
        {
            { nameof(DLogger.Log), "Debug.Log" },
            { nameof(DLogger.LogWarning), "Debug.LogWarning" },
            { nameof(DLogger.LogError), "Debug.LogError" },
            { nameof(DLogger.LogDev), "Debug.Log" },
            { nameof(DLogger.LogDevWarning), "Debug.LogWarning" },
            { nameof(DLogger.LogDevError), "Debug.LogError" },
        };

        private class LogOccurrence { public int LineNumber; public string OriginalLine; }
        private class FileUpgradeInfo
        {
            public string FilePath;
            public List<LogOccurrence> Occurrences = new List<LogOccurrence>();
            public bool IsUpgraded;
            public bool IsSelectedForUpgrade = true;
        }

        private static readonly string[] ExcludedFolders = { "/Plugins/", "/ThirdParty/", "/External/", "/Packages/" };

        private Vector2 _scrollPosition;
        private Dictionary<string, FileUpgradeInfo> _filesWithOccurrences;
        private string _statusMessage = "Ready to search.";
        private bool _searchCompleted;

        private static readonly Regex DebugLogRegex =
            new Regex(@"(?<prefix>(?:UnityEngine\.)?Debug\.)(?<method>Log|LogWarning|LogError)(?<suffix>\s*\()", RegexOptions.Compiled);

        private static readonly Regex DLoggerRegex =
            new Regex($@"{nameof(DLogger)}\.(?<method>LogDev|LogDevWarning|LogDevError|Log|LogWarning|LogError)\s*\(",
                RegexOptions.Compiled);

        private static readonly string DLogNamespace = typeof(DLogger).Namespace;

        private int _selectedTab = 0;
        private readonly string[] _tabs = { "Upgrade Debug → DLogger", "Downgrade DLogger → Debug" };

        private bool _generateCategories = false;
        private bool _removeCategoriesOnDowngrade = false;

        [MenuItem("Tools/DLog Upgrader")]
        public static void ShowWindow()
        {
            GetWindow<DLogUpgrader>("DLog Upgrader");
        }

        private void OnGUI()
        {
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs, GUILayout.Height(25));
            EditorGUILayout.Space();

            if (_selectedTab == 0)
            {
                DrawUpgradeTab();
            }
            else if (_selectedTab == 1)
            {
                DrawDowngradeTab();
            }
        }

        private void DrawUpgradeTab()
        {
            EditorGUILayout.LabelField("Upgrade Debug.Log → DLogger", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This will find Debug.Log/Warning/Error and upgrade them to DLogger.Log/Warning/Error (or Dev variants).\nThis action modifies source files directly and cannot be undone without backups or version control.",
                MessageType.Warning);

            _generateCategories = EditorGUILayout.ToggleLeft("Generate categories for each file", _generateCategories);

            if (GUILayout.Button("Find All Debug.Log Occurrences", GUILayout.Height(30)))
            {
                FindLogCalls(DebugLogRegex);
            }

            DrawResults(upgradeMode: true);
        }

        private void DrawDowngradeTab()
        {
            EditorGUILayout.LabelField("Downgrade DLogger → Debug.Log", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This will find DLogger.Log/Warning/Error (and Dev variants) and downgrade them back to Debug.Log/Warning/Error.\nThis action modifies source files directly and cannot be undone without backups or version control.",
                MessageType.Warning);

            _removeCategoriesOnDowngrade = EditorGUILayout.ToggleLeft("Remove all categories when downgrading", _removeCategoriesOnDowngrade);

            if (GUILayout.Button("Find All DLogger Occurrences", GUILayout.Height(30)))
            {
                FindLogCalls(DLoggerRegex);
            }

            DrawResults(upgradeMode: false);
        }

        private void DrawResults(bool upgradeMode)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_statusMessage, EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();

            if (_searchCompleted && _filesWithOccurrences != null &&
                _filesWithOccurrences.Values.Any(f => !f.IsUpgraded && f.IsSelectedForUpgrade))
            {
                if (upgradeMode)
                {
                    if (GUILayout.Button($"Upgrade All to {nameof(DLogger)}.{nameof(DLogger.Log)}", GUILayout.Width(200)))
                        if (ConfirmAction("Upgrade All")) UpgradeAll(false);

                    if (GUILayout.Button($"Upgrade All to {nameof(DLogger)}.{nameof(DLogger.LogDev)}", GUILayout.Width(200)))
                        if (ConfirmAction("Upgrade All to Dev")) UpgradeAll(true);
                }
                else
                {
                    if (GUILayout.Button("Downgrade All to Debug.Log", GUILayout.Width(200)))
                        if (ConfirmAction("Downgrade All")) DowngradeAll();
                }
            }
            EditorGUILayout.EndHorizontal();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, EditorStyles.helpBox);
            if (_filesWithOccurrences != null)
            {
                foreach (var fileInfo in _filesWithOccurrences.Values)
                {
                    DrawFileGroup(fileInfo, upgradeMode);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool ConfirmAction(string actionName)
        {
            return EditorUtility.DisplayDialog($"{actionName}",
                $"This will modify all selected script files.\n\n" +
                "THIS ACTION CANNOT BE UNDONE.\n\n" +
                "Are you sure you want to proceed?",
                $"Yes, {actionName}", "Cancel");
        }

        private void DrawFileGroup(FileUpgradeInfo fileInfo, bool upgradeMode)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            GUI.backgroundColor = fileInfo.IsUpgraded ? new Color(0.7f, 1f, 0.7f, 0.5f) : Color.white;
            EditorGUILayout.BeginVertical(new GUIStyle(GUI.skin.box) { padding = new RectOffset(5, 5, 5, 5) });
            GUI.backgroundColor = Color.white;

            using (new EditorGUI.DisabledScope(fileInfo.IsUpgraded))
            {
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.BeginVertical();
                string countLabel = $"{fileInfo.Occurrences.Count} occurrence" +
                                    (fileInfo.Occurrences.Count == 1 ? "" : "s");
                EditorGUILayout.ObjectField(countLabel,
                    AssetDatabase.LoadAssetAtPath<MonoScript>(fileInfo.FilePath),
                    typeof(MonoScript), false);
                EditorGUILayout.SelectableLabel(fileInfo.FilePath, EditorStyles.miniLabel,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.EndVertical();

                fileInfo.IsSelectedForUpgrade =
                    EditorGUILayout.ToggleLeft("Include", fileInfo.IsSelectedForUpgrade, GUILayout.Width(70));

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();

            EditorGUI.indentLevel++;
            foreach (var occurrence in fileInfo.Occurrences)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Line {occurrence.LineNumber}:", GUILayout.Width(70));
                EditorGUILayout.SelectableLabel(occurrence.OriginalLine.Trim(), EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(2);
            using (new EditorGUI.DisabledScope(fileInfo.IsUpgraded))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (upgradeMode)
                {
                    if (GUILayout.Button($"Upgrade to {nameof(DLogger)}.{nameof(DLogger.Log)}", GUILayout.Width(200)))
                        UpgradeFiles(new[] { fileInfo.FilePath }, false);
                    if (GUILayout.Button($"Upgrade to {nameof(DLogger)}.{nameof(DLogger.LogDev)}", GUILayout.Width(200)))
                        UpgradeFiles(new[] { fileInfo.FilePath }, true);
                }
                else
                {
                    if (GUILayout.Button("Downgrade to Debug.Log", GUILayout.Width(200)))
                        DowngradeFiles(new[] { fileInfo.FilePath });
                }
                EditorGUILayout.EndHorizontal();
            }

            if (fileInfo.IsUpgraded)
                EditorGUILayout.LabelField("Processed!", EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void FindLogCalls(Regex regex)
        {
            _searchCompleted = false;
            _statusMessage = "Searching...";
            _filesWithOccurrences = new Dictionary<string, FileUpgradeInfo>();
            Repaint();

            string[] scriptGuids = AssetDatabase.FindAssets("t:Script");
            foreach (string guid in scriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/") || ExcludedFolders.Any(folder => path.Contains(folder)))
                    continue;

                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        if (!_filesWithOccurrences.ContainsKey(path))
                            _filesWithOccurrences[path] = new FileUpgradeInfo { FilePath = path };

                        _filesWithOccurrences[path].Occurrences.Add(new LogOccurrence
                        {
                            LineNumber = i + 1,
                            OriginalLine = lines[i]
                        });
                    }
                }
            }

            int totalOccurrences = _filesWithOccurrences.Values.Sum(info => info.Occurrences.Count);
            _statusMessage = $"Search complete. Found {totalOccurrences} occurrences in {_filesWithOccurrences.Count} file(s).";
            _searchCompleted = true;
            Repaint();
        }

        private void UpgradeAll(bool upgradeToDev)
        {
            var pathsToUpgrade = _filesWithOccurrences.Values
                .Where(f => !f.IsUpgraded && f.IsSelectedForUpgrade)
                .Select(f => f.FilePath).ToArray();
            UpgradeFiles(pathsToUpgrade, upgradeToDev);
        }

        private void DowngradeAll()
        {
            var pathsToDowngrade = _filesWithOccurrences.Values
                .Where(f => !f.IsUpgraded && f.IsSelectedForUpgrade)
                .Select(f => f.FilePath).ToArray();
            DowngradeFiles(pathsToDowngrade);
        }

        private void UpgradeFiles(string[] filePaths, bool upgradeToDev)
        {
            if (filePaths == null || filePaths.Length == 0) return;
            int upgradedFileCount = 0;

            foreach (string path in filePaths)
            {
                var lines = new List<string>(File.ReadAllLines(path));
                bool modified = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    string updatedLine = DebugLogRegex.Replace(lines[i], match =>
                    {
                        modified = true;
                        string method = match.Groups["method"].Value;
                        return upgradeToDev
                            ? $"{nameof(DLogger)}.{DevMethodMap[method]}("
                            : $"{nameof(DLogger)}.{method}(";
                    });

                    lines[i] = updatedLine;
                }

                if (modified)
                {
                    AddUsingDirective(lines, DLogNamespace);

                    if (_generateCategories)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(path);
                        InsertCategoryField(lines, fileName);
                        AddCategoryArguments(lines, fileName);
                    }

                    File.WriteAllText(path, string.Join("\n", lines));
                    upgradedFileCount++;
                    if (_filesWithOccurrences.ContainsKey(path))
                        _filesWithOccurrences[path].IsUpgraded = true;
                }
            }

            if (upgradedFileCount > 0)
            {
                _statusMessage = $"Successfully upgraded {upgradedFileCount} file(s). Recompiling...";
                AssetDatabase.Refresh();
            }
            else
            {
                _statusMessage = "No files were changed.";
            }

            Repaint();
        }

        private void DowngradeFiles(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0) return;
            int downgradedFileCount = 0;

            foreach (string path in filePaths)
            {
                var lines = new List<string>(File.ReadAllLines(path));
                bool modified = false;

                for (int i = 0; i < lines.Count; i++)
                {
                    string updatedLine = DLoggerRegex.Replace(lines[i], match =>
                    {
                        modified = true;
                        string method = match.Groups["method"].Value;
                        return DowngradeMap.TryGetValue(method, out var debugMethod)
                            ? $"{debugMethod}("
                            : match.Value;
                    });

                    // Optionally strip categories
                    if (_removeCategoriesOnDowngrade && updatedLine.Contains("category:"))
                    {
                        int categoryIndex = updatedLine.IndexOf(", category:");
                        if (categoryIndex >= 0)
                        {
                            int closeParen = updatedLine.LastIndexOf(")");
                            if (closeParen > categoryIndex)
                            {
                                updatedLine = updatedLine.Remove(categoryIndex, closeParen - categoryIndex);
                                modified = true;
                            }
                        }
                    }

                    lines[i] = updatedLine;
                }

                if (_removeCategoriesOnDowngrade)
                {
                    // Remove generated DLogCategory fields
                    lines = lines.Where(l => !l.Contains($"{nameof(DLogCategory)}")).ToList();

                    // If there are no more references, remove the using directive
                    bool hasReferences = lines.Any(l => l.Contains("DLogger") || l.Contains($"{nameof(DLogCategory)}"));
                    if (!hasReferences)
                    {
                        lines = lines.Where(l => !Regex.IsMatch(l.Trim(), $@"^using\s+{Regex.Escape(DLogNamespace)}\s*;")).ToList();
                    }
                }


                if (modified)
                {
                    File.WriteAllText(path, string.Join("\n", lines));
                    downgradedFileCount++;
                    if (_filesWithOccurrences.ContainsKey(path))
                        _filesWithOccurrences[path].IsUpgraded = true;
                }
            }

            if (downgradedFileCount > 0)
            {
                _statusMessage = $"Successfully downgraded {downgradedFileCount} file(s). Recompiling...";
                AssetDatabase.Refresh();
            }
            else
            {
                _statusMessage = "No files were changed.";
            }

            Repaint();
        }

        private void AddUsingDirective(List<string> lines, string namespaceToAdd)
        {
            string usingStatement = $"using {namespaceToAdd};";
            if (lines.Any(line => line.Trim() == usingStatement)) return;
            int lastUsingIndex = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmedLine = lines[i].Trim();
                if (trimmedLine.StartsWith("using ")) lastUsingIndex = i;
                if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("//")) continue;
                if (trimmedLine.StartsWith("namespace") || trimmedLine.StartsWith("public") ||
                    trimmedLine.StartsWith("internal") || trimmedLine.StartsWith("[")) break;
            }
            lines.Insert(lastUsingIndex != -1 ? lastUsingIndex + 1 : 0, usingStatement);
        }

        private void InsertCategoryField(List<string> lines, string fileName)
        {
            string categoryName = $"{fileName}Category";
            string colorHex = ColorUtility.ToHtmlStringRGB(Random.ColorHSV());

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].TrimStart().StartsWith("public class") ||
                    lines[i].TrimStart().StartsWith("internal class") ||
                    lines[i].TrimStart().StartsWith("class "))
                {
                    // Find the next line that contains "{"
                    for (int j = i; j < lines.Count; j++)
                    {
                        if (lines[j].Contains("{"))
                        {
                            int insertIndex = j + 1;
                            string indent = new string(' ', lines[j].TakeWhile(char.IsWhiteSpace).Count() + 4);
                            lines.Insert(insertIndex, $"{indent}private static readonly DLogCategory {categoryName} = new(\"{fileName}\", \"#{colorHex}\");");
                            return;
                        }
                    }
                }
            }
        }


        private void AddCategoryArguments(List<string> lines, string fileName)
        {
            string categoryName = $"{fileName}Category";

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains("DLogger.Log"))
                {
                    if (!lines[i].Contains("category:"))
                    {
                        int lastParen = lines[i].LastIndexOf(")");
                        if (lastParen != -1)
                        {
                            string before = lines[i].Substring(0, lastParen);
                            string after = lines[i].Substring(lastParen);
                            lines[i] = $"{before}, category: {categoryName}{after}";
                        }
                    }
                }
            }
        }
    }
}
