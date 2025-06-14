using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NoSlimes.Logging.Editor
{
    public class DLogUpgrader : EditorWindow
    {
        private class LogOccurrence { public int LineNumber; public string OriginalLine; }
        private class FileUpgradeInfo { public string FilePath; public List<LogOccurrence> Occurrences = new List<LogOccurrence>(); public bool IsUpgraded; }

        private static readonly string[] ExcludedFolders = { "/Plugins/", "/ThirdParty/", "/External/", "/Packages/" };

        private Vector2 _scrollPosition;
        private Dictionary<string, FileUpgradeInfo> _filesWithOccurrences;
        private string _statusMessage = "Ready to search.";
        private bool _searchCompleted;

        private static readonly Regex DebugLogRegex = new Regex(@"(?<prefix>(?:UnityEngine\.)?Debug\.)(?<method>Log|LogWarning|LogError)(?<suffix>\s*\()", RegexOptions.Compiled);
        private static readonly string DLogNamespace = typeof(DLog).Namespace;

        [MenuItem("Tools/DLog Upgrader")]
        public static void ShowWindow()
        {
            GetWindow<DLogUpgrader>("DLog Upgrader");
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("DLog Upgrader", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This tool modifies C# script files directly. " +
                "These changes CANNOT be undone. " +
                "Please use version control or create backups before proceeding.",
                MessageType.Warning);

            EditorGUILayout.Space();

            if (GUILayout.Button("Find All Debug.Log Occurrences", GUILayout.Height(30)))
            {
                FindDebugLogCalls();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(_statusMessage, EditorStyles.wordWrappedLabel);
            GUILayout.FlexibleSpace();

            if (_searchCompleted && _filesWithOccurrences != null && _filesWithOccurrences.Values.Any(f => !f.IsUpgraded))
            {
                if (GUILayout.Button("Upgrade All to DLog", GUILayout.Width(150)))
                {
                    if (ConfirmUpgradeAll()) UpgradeAll(false);
                }
                if (GUILayout.Button("Upgrade All to DevLog", GUILayout.Width(150)))
                {
                    if (ConfirmUpgradeAll()) UpgradeAll(true);
                }
            }
            EditorGUILayout.EndHorizontal();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, EditorStyles.helpBox);
            if (_filesWithOccurrences != null)
            {
                foreach (var fileInfo in _filesWithOccurrences.Values)
                {
                    DrawFileGroup(fileInfo);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private bool ConfirmUpgradeAll()
        {
            return EditorUtility.DisplayDialog("Confirm Upgrade All",
                "This will modify all applicable script files.\n\n" +
                "THIS ACTION CANNOT BE UNDONE.\n\n" +
                "Are you sure you want to proceed?",
                "Yes, Upgrade All", "Cancel");
        }

        private void DrawFileGroup(FileUpgradeInfo fileInfo)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = fileInfo.IsUpgraded ? new Color(0.7f, 1f, 0.7f, 0.5f) : Color.white;
            EditorGUILayout.BeginVertical(new GUIStyle(GUI.skin.box) { padding = new RectOffset(5, 5, 5, 5) });
            GUI.backgroundColor = Color.white;

            string countLabel = $"{fileInfo.Occurrences.Count} occurrence" + (fileInfo.Occurrences.Count == 1 ? "" : "s");
            EditorGUILayout.ObjectField(countLabel, AssetDatabase.LoadAssetAtPath<MonoScript>(fileInfo.FilePath), typeof(MonoScript), false);
            EditorGUILayout.SelectableLabel(fileInfo.FilePath, EditorStyles.miniLabel, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            EditorGUILayout.EndVertical();
            EditorGUI.indentLevel++;
            foreach (var occurrence in fileInfo.Occurrences)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Line {occurrence.LineNumber}:", GUILayout.Width(70));
                EditorGUILayout.SelectableLabel(occurrence.OriginalLine.Trim(), EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
            using (new EditorGUI.DisabledScope(fileInfo.IsUpgraded))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Upgrade to DLog", GUILayout.Width(150)))
                {
                    UpgradeFiles(new[] { fileInfo.FilePath }, false);
                }
                if (GUILayout.Button("Upgrade to DevLog", GUILayout.Width(150)))
                {
                    UpgradeFiles(new[] { fileInfo.FilePath }, true);
                }
                EditorGUILayout.EndHorizontal();
            }
            if (fileInfo.IsUpgraded) EditorGUILayout.LabelField("Upgraded!", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void FindDebugLogCalls()
        {
            _searchCompleted = false; _statusMessage = "Searching..."; _filesWithOccurrences = new Dictionary<string, FileUpgradeInfo>(); Repaint();
            string[] scriptGuids = AssetDatabase.FindAssets("t:Script");
            foreach (string guid in scriptGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/") || ExcludedFolders.Any(folder => path.Contains(folder))) continue;
                var lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (DebugLogRegex.IsMatch(lines[i]))
                    {
                        if (!_filesWithOccurrences.ContainsKey(path)) _filesWithOccurrences[path] = new FileUpgradeInfo { FilePath = path };
                        _filesWithOccurrences[path].Occurrences.Add(new LogOccurrence { LineNumber = i + 1, OriginalLine = lines[i] });
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
            var pathsToUpgrade = _filesWithOccurrences.Values.Where(f => !f.IsUpgraded).Select(f => f.FilePath).ToArray();
            UpgradeFiles(pathsToUpgrade, upgradeToDev);
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
                    string updatedLine = DebugLogRegex.Replace(lines[i], match => { modified = true; string m = match.Groups["method"].Value; return $"{(upgradeToDev ? "DLog.Dev" : "DLog.")}{m}("; });
                    lines[i] = updatedLine;
                }
                if (modified)
                {
                    AddUsingDirective(lines, DLogNamespace);
                    File.WriteAllText(path, string.Join("\n", lines));
                    upgradedFileCount++;
                    if (_filesWithOccurrences.ContainsKey(path)) _filesWithOccurrences[path].IsUpgraded = true;
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
                if (trimmedLine.StartsWith("namespace") || trimmedLine.StartsWith("public") || trimmedLine.StartsWith("internal") || trimmedLine.StartsWith("[")) break;
            }
            lines.Insert(lastUsingIndex != -1 ? lastUsingIndex + 1 : 0, usingStatement);
        }
    }
}