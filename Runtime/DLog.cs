using UnityEngine;
using System.Runtime.CompilerServices;
using Mono.Cecil.Cil;

[assembly: InternalsVisibleTo("NoSlimes.DLog.Editor")]

namespace NoSlimes.Logging
{
    /// <summary>
    /// A static logging utility that provides categorized, color-coded logs and
    /// editor-only dev logs
    /// </summary>
    public static class DLog
    {
#if UNITY_EDITOR
        /// <summary>
        /// Global toggle to enable or disable developer-only logs.
        /// </summary>
        internal static bool EnableDevLogs = true;

        /// <summary>
        /// Hook for a custom editor window to subscribe to.
        /// Provides the message, category, and source file info for each log call.
        /// </summary>
        internal static System.Action<object, DLogCategory, Object, string, int> AddToEditorWindowHook;
#endif

        /// <summary>
        /// Logs a standard message.
        /// </summary>
        /// <param name="message">The message object to log.</param>
        /// <param name="context">The Unity Object to associate with the log message.</param>
        /// <param name="category">The category of the log message.</param>
        /// <param name="sourceFilePath">The full path of the source file that contains the caller. (Automatically populated)</param>
        /// <param name="sourceLineNumber">The line number in the source file at which the method is called. (Automatically populated)</param>
        public static void Log(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            category ??= DLogCategory.Log;

#if UNITY_EDITOR
            AddToEditorWindowHook?.Invoke(message, category, context, sourceFilePath, sourceLineNumber);
#endif
            Debug.Log($"<color={category.ColorHex}>[{category.Name}] {message}</color>", context);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public static void LogWarning(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            category ??= DLogCategory.Warning;
#if UNITY_EDITOR
            AddToEditorWindowHook?.Invoke(message, category, context, sourceFilePath, sourceLineNumber);
#endif

            Debug.LogWarning($"[{category.Name}] {message}", context);
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        public static void LogError(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            category ??= DLogCategory.Error;

#if UNITY_EDITOR
            AddToEditorWindowHook?.Invoke(message, category, context, sourceFilePath, sourceLineNumber);
#endif

            Debug.LogError($"[{category.Name}] {message}", context);
        }

        #region Developer-Only Logs
        /// <summary>
        /// Logs a developer-only message. This will be compiled out of builds.
        /// </summary>
        public static void DevLog(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
#if UNITY_EDITOR
            if (!EnableDevLogs) return;
            Log($"[DEV] {message}", context, category, sourceFilePath, sourceLineNumber);
#endif
        }

        /// <summary>
        /// Logs a developer-only warning message. This will be compiled out of builds.
        /// </summary>
        public static void DevLogWarning(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {

#if UNITY_EDITOR
            if (!EnableDevLogs) return;
            LogWarning($"[DEV] {message}", context, category, sourceFilePath, sourceLineNumber);
#endif
        }

        /// <summary>
        /// Logs a developer-only error message. This will be compiled out of builds.
        /// </summary>
        public static void DevLogError(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {

#if UNITY_EDITOR
            if (!EnableDevLogs) return;
            LogError($"[DEV] {message}", context, category, sourceFilePath, sourceLineNumber);
#endif
        }
        #endregion
    }
}