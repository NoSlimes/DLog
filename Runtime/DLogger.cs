using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("NoSlimes.DLog.Editor")]

namespace NoSlimes.Logging
{
    /// <summary>
    /// A static logging utility that provides categorized, color-coded logs and
    /// editor-only dev logs
    /// </summary>
    public static class DLogger
    {
#if DEBUG
        /// <summary>
        /// Global toggle to enable or disable developer-only logs.
        /// </summary>
        internal static bool EnableDevLogs = true;
#endif

#if UNITY_EDITOR
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
#if UNITY_2021_2_OR_NEWER
        [HideInCallstack]
#endif
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
#if UNITY_2021_2_OR_NEWER
        [HideInCallstack]
#endif
        public static void LogWarning(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            category ??= DLogCategory.Warning;
#if UNITY_EDITOR
            AddToEditorWindowHook?.Invoke(message, category, context, sourceFilePath, sourceLineNumber);
#endif

            Debug.LogWarning($"<color={category.ColorHex}>[{category.Name}] {message}</color>", context);
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
#if UNITY_2021_2_OR_NEWER
        [HideInCallstack]
#endif
        public static void LogError(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
            category ??= DLogCategory.Error;

#if UNITY_EDITOR
            AddToEditorWindowHook?.Invoke(message, category, context, sourceFilePath, sourceLineNumber);
#endif

            Debug.LogError($"<color={category.ColorHex}>[{category.Name}] {message}</color>", context);
        }

        #region Developer-Only Logs
        /// <summary>
        /// Logs a developer-only message. Calls are stripped at compile time
        /// when DEBUG is not defined, including argument evaluation.
        /// </summary>
#if UNITY_2021_2_OR_NEWER
        [HideInCallstack]
#endif
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDev(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {
#if DEBUG
            if (!EnableDevLogs) return;
            Log($"[DEV] {message}", context, category, sourceFilePath, sourceLineNumber);
#endif
        }

        /// <summary>
        /// Logs a developer-only warning message. This will be compiled out of builds.
        /// </summary>
#if UNITY_2021_2_OR_NEWER
        [HideInCallstack]
#endif
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDevWarning(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {

#if DEBUG
            if (!EnableDevLogs) return;
            LogWarning($"[DEV] {message}", context, category, sourceFilePath, sourceLineNumber);
#endif
        }

        /// <summary>
        /// Logs a developer-only error message. This will be compiled out of builds.
        /// </summary>
#if UNITY_2021_2_OR_NEWER
        [HideInCallstack]
#endif
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDevError(object message, Object context = null, DLogCategory category = null,
            [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
        {

#if DEBUG
            if (!EnableDevLogs) return;
            LogError($"[DEV] {message}", context, category, sourceFilePath, sourceLineNumber);
#endif
        }
        #endregion

        #region Convenience Overloads
        // Simple overloads for just a message
        public static void Log(object message) => Log(message, null, null);
        public static void LogWarning(object message) => LogWarning(message, null, null);
        public static void LogError(object message) => LogError(message, null, null);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDev(object message) => LogDev(message, null, null);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDevWarning(object message) => LogDevWarning(message, null, null);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDevError(object message) => LogDevError(message, null, null);

        // Overloads for message + category
        public static void Log(object message, DLogCategory category) => Log(message, null, category);
        public static void LogWarning(object message, DLogCategory category) => LogWarning(message, null, category);
        public static void LogError(object message, DLogCategory category) => LogError(message, null, category);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDev(object message, DLogCategory category) => LogDev(message, null, category);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDevWarning(object message, DLogCategory category) => LogDevWarning(message, null, category);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDevError(object message, DLogCategory category) => LogDevError(message, null, category);

        // Overloads for message + context
        public static void Log(object message, Object context) => Log(message, context, null);
        public static void LogWarning(object message, Object context) => LogWarning(message, context, null);
        public static void LogError(object message, Object context) => LogError(message, context, null);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDev(object message, Object context) => LogDev(message, context, null);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDevWarning(object message, Object context) => LogDevWarning(message, context, null);
        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDevError(object message, Object context) => LogDevError(message, context, null);
        #endregion
    }

}