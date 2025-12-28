using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TgdSoundboard.Services;

public class GlobalHotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<int, Action> _hotkeyActions = new();
    private readonly Dictionary<string, int> _hotkeyIds = new();
    private int _nextId = 1;
    private IntPtr _windowHandle;
    private HwndSource? _source;

    public void Initialize(Window window)
    {
        _windowHandle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_windowHandle);
        _source?.AddHook(WndProc);
    }

    public bool RegisterHotkey(string hotkeyString, Action callback)
    {
        if (string.IsNullOrWhiteSpace(hotkeyString)) return false;
        if (_windowHandle == IntPtr.Zero) return false;

        // Parse hotkey string (e.g., "Ctrl+Shift+F1")
        if (!TryParseHotkey(hotkeyString, out var modifiers, out var key))
            return false;

        var id = _nextId++;
        var vk = KeyInterop.VirtualKeyFromKey(key);

        if (RegisterHotKey(_windowHandle, id, modifiers, (uint)vk))
        {
            _hotkeyActions[id] = callback;
            _hotkeyIds[hotkeyString] = id;
            return true;
        }

        return false;
    }

    public void UnregisterHotkey(string hotkeyString)
    {
        if (_hotkeyIds.TryGetValue(hotkeyString, out var id))
        {
            UnregisterHotKey(_windowHandle, id);
            _hotkeyActions.Remove(id);
            _hotkeyIds.Remove(hotkeyString);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _hotkeyIds.Values)
        {
            UnregisterHotKey(_windowHandle, id);
        }
        _hotkeyActions.Clear();
        _hotkeyIds.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_hotkeyActions.TryGetValue(id, out var action))
            {
                action.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private static bool TryParseHotkey(string hotkeyString, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;

        var parts = hotkeyString.Split('+');
        if (parts.Length == 0) return false;

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            switch (trimmed.ToLower())
            {
                case "ctrl":
                case "control":
                    modifiers |= 0x0002; // MOD_CONTROL
                    break;
                case "alt":
                    modifiers |= 0x0001; // MOD_ALT
                    break;
                case "shift":
                    modifiers |= 0x0004; // MOD_SHIFT
                    break;
                case "win":
                case "windows":
                    modifiers |= 0x0008; // MOD_WIN
                    break;
                default:
                    // Try to parse as a key
                    if (Enum.TryParse<Key>(trimmed, true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                    else if (trimmed.Length == 1)
                    {
                        // Single character (A-Z, 0-9)
                        key = KeyInterop.KeyFromVirtualKey((int)char.ToUpper(trimmed[0]));
                    }
                    break;
            }
        }

        return key != Key.None;
    }

    public static string FormatHotkey(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
            parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt))
            parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift))
            parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows))
            parts.Add("Win");

        parts.Add(key.ToString());

        return string.Join("+", parts);
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }
}
