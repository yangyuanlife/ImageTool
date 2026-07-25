using System;
using System.Windows.Input;
using System.Windows.Interop;

namespace ImageTool.Services;

/// <summary>
/// 全局热键服务：通过 Win32 RegisterHotKey 在「消息专用窗口」上监听组合键，
/// 触发 HotkeyPressed 事件（在 WPF Dispatcher 线程上回调）。
/// </summary>
public class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int Id = 1;
    // HWND_MESSAGE：把窗口挂到消息专用链表，不显示、不进任务栏/Alt+Tab，
    // 但能稳定收到 WM_HOTKEY（这是 HwndSource 做全局热键最稳妥的挂法）。
    private static readonly IntPtr HwndMessage = (IntPtr)(-3);

    private HwndSource? _hwndSource;
    private bool _registered;

    /// <summary>热键被按下时触发（在 UI 线程）</summary>
    public event Action? HotkeyPressed;

    /// <summary>使用当前设置注册热键。注册失败（如被占用）会抛异常。</summary>
    public void Register()
    {
        var s = SettingsStore.Load();
        Register(s.HotkeyModifiers, s.HotkeyKey);
    }

    /// <summary>用指定的修饰符与主键重新注册（设置变更时调用）</summary>
    public void Register(int modifiers, int key)
    {
        Unregister();
        EnsureWindow();

        var vk = KeyInterop.VirtualKeyFromKey((Key)key);
        _registered = NativeMethods.RegisterHotKey(_hwndSource!.Handle, Id, (uint)modifiers, (uint)vk);
        if (!_registered)
        {
            // RegisterHotKey 失败时返回 false（而非抛异常），这里转成异常让上层提示，
            // 否则会静默失效、用户完全不知道热键没注册上。
            throw new InvalidOperationException(
                $"无法注册全局热键（修饰符={modifiers}，主键={key}），可能已被其它程序占用。");
        }
    }

    public void Unregister()
    {
        if (_registered && _hwndSource != null)
        {
            NativeMethods.UnregisterHotKey(_hwndSource.Handle, Id);
            _registered = false;
        }
    }

    private void EnsureWindow()
    {
        if (_hwndSource != null) return;
        var param = new HwndSourceParameters("ImageToolHotkey")
        {
            // WS_POPUP：不可见、不进任务栏/Alt+Tab，仅用于接收窗口消息
            WindowStyle = unchecked((int)0x80000000),
            HwndSourceHook = WndProc
        };
        _hwndSource = new HwndSource(param);
        // 关键：挂到消息专用窗口链表（HWND_MESSAGE），确保 WM_HOTKEY 稳定送达钩子，
        // 且窗口不会出现在 Alt+Tab。HwndSourceParameters 没有 Parent 属性，故创建后用 SetParent。
        SetParent(_hwndSource.Handle, HwndMessage);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SetParent(IntPtr hWnd, IntPtr hWndNewParent);

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && (int)wParam == Id)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _hwndSource?.Dispose();
        _hwndSource = null;
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
