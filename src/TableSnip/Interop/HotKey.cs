using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TableSnip.Interop;

/// <summary>A system-wide hotkey bound to a WPF window.</summary>
public sealed class HotKey : IDisposable
{
    private static int _nextId = 0xA100;
    private readonly HwndSource _source;
    private readonly int _id;
    private readonly Action _callback;
    private bool _disposed;

    public ModifierKeys Modifiers { get; }
    public Key Key { get; }

    private HotKey(HwndSource source, int id, ModifierKeys modifiers, Key key, Action callback)
    {
        _source = source;
        _id = id;
        _callback = callback;
        Modifiers = modifiers;
        Key = key;
        _source.AddHook(WndProc);
    }

    /// <summary>Returns null if the combination is already taken by another app.</summary>
    public static HotKey? TryRegister(Window window, ModifierKeys modifiers, Key key, Action callback)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return null;
        var source = HwndSource.FromHwnd(handle);
        if (source == null) return null;

        uint mods = NativeMethods.MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= NativeMethods.MOD_WIN;

        int id = _nextId++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!NativeMethods.RegisterHotKey(handle, id, mods, vk)) return null;
        return new HotKey(source, id, modifiers, key, callback);
    }

    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            parts.Add(Key.ToString());
            return string.Join("+", parts);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            _callback();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _source.RemoveHook(WndProc);
        NativeMethods.UnregisterHotKey(_source.Handle, _id);
    }
}
