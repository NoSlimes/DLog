using UnityEditor;
using UnityEngine;

namespace NoSlimes.Logging.Editor
{
    [InitializeOnLoad]
    public static class DLogInstallHandler
    {
        private const string FirstTimeShownPrefKey = "DLog.Upgrader.HasShown";

        static DLogInstallHandler()
        {
            EditorApplication.delayCall += ShowWindowOnFirstInstall;
        }

        private static void ShowWindowOnFirstInstall()
        {
            EditorApplication.delayCall -= ShowWindowOnFirstInstall;

            if (!EditorPrefs.GetBool(FirstTimeShownPrefKey, false))
            {
                EditorPrefs.SetBool(FirstTimeShownPrefKey, true);

                DLogUpgrader.ShowWindow();
            }
        }

        [MenuItem("Tools/DLog/Reset First Install Pop-up")]
        private static void ResetFirstInstallFlag()
        {
            EditorPrefs.DeleteKey(FirstTimeShownPrefKey);
            DLog.Log("DLog Upgrader 'first install' flag has been reset. The window will pop up on the next recompile.");
        }
    }
}