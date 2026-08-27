using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Sonvert.App.Services.Native;
using Sonvert.App.ViewModels;
using System;
using System.Runtime.InteropServices;

namespace Sonvert.App.Views;

public partial class SubtitleWindow : Window
{
    private const int HotkeyId = 0x4A50; // 只要是个没被系统占用的整数就行，值本身没有特殊含义

    // 必须用字段长期持有这个委托，原因见 Win32Interop.SetWndProc 的注释——
    // 如果只是局部变量，GC 可能会在 Windows 还在用这个函数指针的时候
    // 把它回收掉，导致程序崩溃。
    private Win32Interop.WndProcDelegate? _wndProcDelegate;
    private IntPtr _originalWndProc;
    private IntPtr _hwnd;

    public SubtitleWindow()
    {
        InitializeComponent();

        Opened += OnOpened;
        Closing += OnClosing;

        DataContextChanged += (_, _) =>
        {
            var contentControl = this.FindControl<ItemsControl>("ContentItemsControl");
            if (contentControl is not null)
            {
                contentControl.LayoutUpdated += OnContentLayoutUpdated;
            }
        };

        var scrollViewerForDrag = this.FindControl<ScrollViewer>("ContentScrollViewer");
        if (scrollViewerForDrag is not null)
        {
            scrollViewerForDrag.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(scrollViewerForDrag).Properties.IsLeftButtonPressed)
                {
                    BeginMoveDrag(e);
                }
            };
        }

        AttachResizeHandle("ResizeLeft", WindowEdge.West);
        AttachResizeHandle("ResizeRight", WindowEdge.East);
        AttachResizeHandle("ResizeTop", WindowEdge.North);
        AttachResizeHandle("ResizeBottom", WindowEdge.South);
        AttachResizeHandle("ResizeBottomRight", WindowEdge.SouthEast);

        if (DataContext is SubtitleWindowViewModel vm)
        {
            vm.Items.CollectionChanged += OnContentLayoutUpdated;
        }

        var lockButton = this.FindControl<Button>("LockButton");
        if (lockButton is not null)
        {
            lockButton.Click += OnLockButtonClick;
        }
    }

    private void OnContentLayoutUpdated(object? sender, EventArgs e)
    {
        // 只在锁定状态下自动滚动——原因跟之前一致：没锁定时用户可能正在
        // 往上翻看历史内容，不应该被打断。LayoutUpdated 这个事件本身会
        // 频繁触发（不只是加新内容时，调整窗口大小、拖动这些也会触发），
        // 但反复调用 ScrollToEnd() 在已经滚到底部时是无副作用的，
        // 不需要额外加判断去"只在真正有新内容时才滚动"。
        if (DataContext is not SubtitleWindowViewModel { IsLocked: true }) return;

        var scrollViewer = this.FindControl<ScrollViewer>("ContentScrollViewer");
        scrollViewer?.ScrollToEnd();
    }

    private void AttachResizeHandle(string controlName, WindowEdge edge)
    {
        var handle = this.FindControl<Control>(controlName);
        if (handle is null) return;

        handle.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)
            {
                BeginResizeDrag(edge, e);
            }
        };
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_hwnd == IntPtr.Zero) return;

        Win32Interop.RegisterHotKey(_hwnd, HotkeyId, Win32Interop.MOD_CONTROL | Win32Interop.MOD_SHIFT, Win32Interop.VK_L);

        _wndProcDelegate = WndProcHook;
        _originalWndProc = Win32Interop.SetWndProc(_hwnd, _wndProcDelegate);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_hwnd != IntPtr.Zero)
    {
        // 只做真正必要的清理——注销热键。不再尝试还原 WndProc，
        // 窗口马上就要被销毁，操作系统会自己回收这些资源，
        // 手动还原反而引入了额外的、没有必要的失败风险。
        try
        {
            Win32Interop.UnregisterHotKey(_hwnd, HotkeyId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[字幕窗口] 注销热键失败（不影响关闭）: {ex.Message}");
        }
    }
    }

    /// <summary>拦截窗口消息，只关心 WM_HOTKEY（全局热键触发）——
    /// 其他所有消息原样转发给原来的处理函数，不能吞掉，否则窗口的
    /// 正常行为（比如响应关闭、绘制）会失效。</summary>
    private IntPtr WndProcHook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32Interop.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            if (DataContext is SubtitleWindowViewModel vm)
            {
                vm.Unlock();
                Win32Interop.SetClickThrough(_hwnd, false);
            }
        }

        return Win32Interop.CallOriginalWndProc(_originalWndProc, hWnd, msg, wParam, lParam);
    }

    private void OnLockButtonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SubtitleWindowViewModel vm)
        {
            vm.LockCommand.Execute(null);
        }

        // 点击穿透必须在锁定命令执行之后才启用——顺序反过来的话，
        // 这次点击事件本身可能因为还没轮到下一帧渲染就已经被"穿透"效果
        // 影响，导致锁定状态出现时序上的诡异行为。
        Win32Interop.SetClickThrough(_hwnd, true);
    }
}