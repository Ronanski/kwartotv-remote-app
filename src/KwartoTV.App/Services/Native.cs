using System.Runtime.InteropServices;
using KwartoTV.Core;

namespace KwartoTV.App.Services;

/// <summary>Win32 calls: synthetic key presses, screen and power control, window styles.</summary>
internal static class Native
{
    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint KeyUp = 0x0002, KeyExtended = 0x0001, KeyUnicode = 0x0004, MouseMove = 0x0001;
    private const ushort VkMenu = 0x12;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MouseInput mi; [FieldOffset(0)] public KeybdInput ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("powrprof.dll")] private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    public static IntPtr Foreground => GetForegroundWindow();
    public static uint ForegroundProcessId() { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid; }

    public static void Press(IEnumerable<KeyStroke> keys)
    {
        foreach (var k in keys)
        {
            var seq = new List<Input>();
            if (k.Alt) seq.Add(Key(VkMenu, 0, false));
            seq.Add(Key(k.Vk, k.Extended ? KeyExtended : 0, false));
            seq.Add(Key(k.Vk, (k.Extended ? KeyExtended : 0) | KeyUp, true));
            if (k.Alt) seq.Add(Key(VkMenu, KeyUp, true));
            Send(seq);
        }
    }

    public static void TypeText(string text)
    {
        var seq = new List<Input>();
        foreach (var ch in text)
        {
            seq.Add(Unicode(ch, 0));
            seq.Add(Unicode(ch, KeyUp));
        }
        Send(seq);
    }

    private static Input Key(ushort vk, uint flags, bool _) =>
        new() { type = InputKeyboard, u = new InputUnion { ki = new KeybdInput { wVk = vk, dwFlags = flags } } };

    private static Input Unicode(char ch, uint up) =>
        new() { type = InputKeyboard, u = new InputUnion { ki = new KeybdInput { wScan = ch, dwFlags = KeyUnicode | up } } };

    private static void Send(List<Input> inputs)
    {
        if (inputs.Count == 0) return;
        var arr = inputs.ToArray();
        SendInput((uint)arr.Length, arr, Marshal.SizeOf<Input>());
    }

    /// <summary>A 1px mouse nudge wakes a monitor that was switched off.</summary>
    public static void WakeScreen()
    {
        var move = new Input { type = InputMouse, u = new InputUnion { mi = new MouseInput { dx = 1, dwFlags = MouseMove } } };
        var back = new Input { type = InputMouse, u = new InputUnion { mi = new MouseInput { dx = -1, dwFlags = MouseMove } } };
        SendInput(2, new[] { move, back }, Marshal.SizeOf<Input>());
    }

    public static void ScreenOff(IntPtr ownWindow) => PostMessage(ownWindow, 0x0112, (IntPtr)0xF170, (IntPtr)2);

    public static void Sleep() => SetSuspendState(false, true, false);

    public static void BringToFront(IntPtr hwnd)
    {
        ShowWindow(hwnd, 9); // SW_RESTORE
        // Windows only lets the foreground app take focus; a quick Alt tap makes the request allowed.
        Press(new[] { new KeyStroke(VkMenu) });
        SetForegroundWindow(hwnd);
    }

    /// <summary>Makes a window click-through, non-activating and hidden from Alt+Tab (used for the on-screen display).</summary>
    public static void MakeOverlay(IntPtr hwnd)
    {
        const int GwlExStyle = -20;
        const long Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)(style | Transparent | ToolWindow | NoActivate));
    }
}
