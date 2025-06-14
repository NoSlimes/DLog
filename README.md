# DLog — Advanced Logging Utility for Unity

DLog is a simple, color-coded logging utility for Unity that enhances your debugging experience by providing categorized logs directly in the Unity Editor. It also includes a dedicated editor window acting as a full console to view and filter logs easily.

## Features

- Categorized logs with color coding (Log, Warning, Error, and custom categories)
- Developer-only logs that are excluded from builds
- Dedicated Editor Window for viewing logs in real-time with filtering options

## How to Use

### Logging

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

You can optionally pass a Unity `Object` as context and specify a custom `DLogCategory` for custom color-coded categories.

### Opening the Log Console Window

* In the Unity Editor, open the console window by navigating to:

  **Window > DLog Console**

* This window displays all logs generated via `DLog` in real-time.

* It can also optionally fetch logs made with Debug.Log

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

Of course, you can define categories anywhere you want — even inside a MonoBehaviour:

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
