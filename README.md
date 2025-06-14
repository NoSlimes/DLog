# DLog — Advanced Logging Utility for Unity

DLog is a simple, color-coded logging utility for Unity that enhances your debugging experience by providing categorized logs directly in the Unity Editor. It also includes a dedicated editor window acting as a full console to view and filter logs easily.

## Features

- Categorized logs with color coding (Log, Warning, Error, and custom categories)
- Developer-only logs that are excluded from builds
- Dedicated Editor Window for viewing logs in real-time with filtering options

## Installing

You can add DLog to your project in two ways: via the Unity Package Manager (recommended) or by importing a `.unitypackage`.

### Method 1: Unity Package Manager (Recommended)

This is the cleanest method, as it keeps the package separate from your `Assets` folder and makes updates easy.

1.  In the Unity Editor, open the **Package Manager** window by navigating to **Window > Package Manager**.
2.  Click the **`+`** (plus) icon in the top-left corner of the window.
3.  Select **"Add package from git URL..."** from the dropdown menu.
4.  Enter the following URL and click **Add**:

    ```
    https://github.com/NoSlimes/DLog.git
    ```

5.  The package will be installed into your project. You can update it at any time from the Package Manager window.

### Method 2: Traditional .unitypackage

If you prefer, you can install DLog by downloading a standard `.unitypackage`.

1.  Go to the **[Releases](https://github.com/NoSlimes/DLog/releases)** page of the DLog repository.
2.  Download the latest `.unitypackage` file from the assets list.
3.  Open your Unity project and import the package by either dragging the file into your **Assets** folder or by navigating to **Assets > Import Package > Custom Package...**.

## How to Use

### Simple Logging

Use the static methods in `DLog` to log messages:

```csharp
DLog.Log("This is a standard log message");
DLog.LogWarning("This is a warning message");
DLog.LogError("This is an error message");

// Developer-only logs (only visible in the editor, not included in builds)
DLog.DevLog("Developer-only log");
DLog.DevLogWarning("Developer-only warning");
DLog.DevLogError("Developer-only error");
````

You can optionally pass a Unity `Object` as context and specify a custom `DLogCategory` for custom color-coded categories. That is the recommended approach

### Opening the Log Console Window

* In the Unity Editor, open the console window by navigating to:

  **Window > DLog Console**

* This window displays all logs generated via `DLog` in real-time.

* It can also optionally fetch logs added to Unity's built-in console

* Logs are color-coded by category for easy identification.

* Use the built-in filtering options to view logs by category or severity.

---

## Creating and Using Custom Categories

DLog’s true power lies in its flexible, color-coded categories. Here’s how to create and use your own:

### 1. Define Your Categories

It’s a good idea to centralize your categories in a static class for easy reuse and consistency:

```csharp
using UnityEngine;
using NoSlimes.Logging;

public static class LogCategories
{
    public static readonly DLogCategory AI = new("AI", Color.cyan);
    public static readonly DLogCategory Networking = new("Network", "#FFA500"); // Orange
    public static readonly DLogCategory UI = new("UI", new Color(0.5f, 0.7f, 1f)); // Light Blue
}
```

Of course, you can define categories anywhere you want:

```csharp
using UnityEngine;
using NoSlimes.Logging;

public class Player : MonoBehaviour
{
    public static readonly DLogCategory PlayerLog = new("Player", Color.cyan);
}
```

### 2. Use Categories When Logging

Pass your custom category as the third argument in any `DLog` call:

```csharp
using NoSlimes.Logging;

public class EnemyAI : MonoBehaviour
{
    void ChangeState(string newState)
    {
        DLog.Log($"State changed to '{newState}'", this, LogCategories.AI);
    }
}

public class UIManager : MonoBehaviour
{
    public void OnButtonClick()
    {
        DLog.Log("Main Menu button clicked", this, LogCategories.UI);
    }
}
```

---

## Developer-Only Logs

Use `DevLog`, `DevLogWarning`, and `DevLogError` for messages you want to see only in the editor. These calls are excluded from builds automatically, so there’s no runtime cost.

Perfect for noisy debug info like position updates or internal state dumps:

```csharp
using NoSlimes.Logging;

public class PlayerMovement : MonoBehaviour
{
    void Update()
    {
        DLog.DevLog($"Player position: {transform.position}", this, LogCategories.Physics);
    }

    void OnCollisionEnter(Collision collision)
    {
        DLog.DevLogWarning("Player collided with an untagged object.", this, LogCategories.Physics);
    }
}
```


## Upgrading Existing Projects

Manually converting all `Debug.Log` calls in a large project is tedious. To make adoption seamless, DLog includes an editor utility that can find and replace `Debug.Log`, `Debug.LogWarning`, and `Debug.LogError` calls automatically.

### DLog Upgrader Utility

The upgrader provides a safe and intuitive UI to review and apply changes, with full **Undo support**.

**How to open the tool:**

*   In the Unity Editor, navigate to **Tools > DLog Upgrader**.

**Workflow:**

1.  **Configure:** Choose whether to upgrade calls to standard `DLog.Log` methods or to the developer-only `DLog.DevLog` variants using the checkbox.
2.  **Find Occurrences:** Click the **"Find All Debug.Log Occurrences"** button. The tool will scan your project's C# scripts (excluding "Plugins", "Editor", and other common third-party folders) and list all usages.
3.  **Review Changes:** For each occurrence, the window will display:
    *   The file path and line number.
    *   The original line of code.
    *   A preview of the upgraded line of code.
4.  **Upgrade:** You have two options:
    *   Click **"Upgrade This File"** to modify a single script.
    *   Click **"Upgrade All"** to apply changes to all found files at once.

> **Important:** The upgrader is integrated with Unity's Undo system. If you make a change, you can immediately revert it by pressing `Ctrl+Z` (or `Cmd+Z` on macOS).
