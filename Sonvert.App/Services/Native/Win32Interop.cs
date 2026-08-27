using System;
using System.Runtime.InteropServices;

namespace Sonvert.App.Services.Native;

/// <summary>
/// 直接调用 Windows 原生 API 的两块能力：
///   1）SetClickThrough——让窗口的鼠标点击穿透到它后面的内容，
///      对应字幕窗口的"锁定"功能。Avalonia 本身没有这个能力，
///      必须靠系统的扩展窗口样式（WS_EX_TRANSPARENT）实现，
///      这不是我们能绕开的限制，是 Windows 本身的机制。
///   2）全局热键（RegisterHotKey）——即使程序不在前台（比如全屏
///      玩游戏时），按下指定快捷键也能收到通知，用于"字幕锁定后，
///      不用切出游戏也能解锁"这个场景。
///
/// 这两块都涉及跟 Windows 消息循环打交道，是这个应用里少数几处
/// 真正意义上的"底层系统编程"，代码本身不长，但错一处容易导致
/// 崩溃或者内存泄漏（尤其是委托生命周期这块，见下面 SetWndProc 的注释）。
/// </summary>
public static class Win32Interop
{
    private const int GWL_EXSTYLE = -20;
    private const int GWLP_WNDPROC = -4;
    private const long WS_EX_LAYERED = 0x80000;
    private const long WS_EX_TRANSPARENT = 0x20;

    public const uint WM_HOTKEY = 0x0312;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint VK_L = 0x4C;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>自定义窗口消息处理函数的委托签名——WM_HOTKEY 这类系统消息
    /// 就是通过这个回调传给我们的代码的。</summary>
    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>开启/关闭鼠标点击穿透。开启后，这个窗口对鼠标而言"不存在"，
    /// 点击会直接作用到它后面的内容（比如后面的游戏画面）。</summary>
    public static void SetClickThrough(IntPtr hWnd, bool clickThrough)
    {
        var exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
        exStyle = clickThrough
            ? exStyle | WS_EX_LAYERED | WS_EX_TRANSPARENT
            : exStyle & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT);
        SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    /// <summary>
    /// "窗口子类化"——把窗口原本的消息处理函数换成我们自己的，
    /// 让我们能拦截到 WM_HOTKEY 这类消息。返回值是原来的处理函数指针，
    /// 调用方必须在自己的新处理函数里，对不关心的消息调用
    /// CallOriginalWndProc 转发给它，否则窗口会失去所有原生行为
    /// （拖拽、绘制、关闭这些都会失效）。
    ///
    /// 关键坑：newProc 这个委托对象，调用方必须自己用一个字段长期持有它
    /// （不能只是传进来就不管了）——因为这里只是把委托对应的原生函数指针
    /// 交给了 Windows，.NET 的垃圾回收器不知道 Windows 那边还在用它，
    /// 如果委托对象本身被回收，Windows 之后调用这个已经失效的函数指针
    /// 会直接导致程序崩溃。这是 P/Invoke 里一个经典的、容易被忽略的坑。
    /// </summary>
    public static IntPtr SetWndProc(IntPtr hWnd, WndProcDelegate newProc)
    {
        var newProcPtr = Marshal.GetFunctionPointerForDelegate(newProc);
        return SetWindowLongPtr(hWnd, GWLP_WNDPROC, newProcPtr);
    }

    public static IntPtr CallOriginalWndProc(IntPtr originalWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        => CallWindowProc(originalWndProc, hWnd, msg, wParam, lParam);
}