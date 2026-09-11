using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Sonvert.App.Services.Native;
using Sonvert.App.ViewModels;
using System;
using System.Collections.Specialized;
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

    // 判断"离底部多近算贴底"的容差——不用严格等于最大滚动值，
    // 留一点像素余量，避免因为浮点数/子像素误差导致明明用户就在
    // 底部却被判定成"没贴底"。
    private const double NearBottomThresholdPx = 60;

    // 默认 true：窗口刚打开、还没收到任何滚动事件的时候，应该表现得
    // 像"用户就在底部"，第一批内容到达时正常跟随滚动。
    private bool _isNearBottom = true;

    // 有新内容加进 Items、还没执行那次"该不该自动滚动"判断——用这个
    // 标记把"新内容到达"（CollectionChanged）和"布局真正稳定、可以
    // 滚动了"（LayoutUpdated）这两个时间点分开处理，避免下面提到的
    // 死循环。
    private bool _pendingAutoScroll;

    // 新内容到达那一刻（CollectionChanged 触发时）用户是不是贴底——
    // 必须在这一刻就存下来，不能等到 LayoutUpdated 真正执行滚动判断
    // 的时候才读 _isNearBottom：新内容加进去会让内容区域变高，
    // ScrollChanged 可能会先于我们的滚动判断触发一次，用旧的 Offset
    // 对比新的 Extent 重新算一遍"贴不贴底"，这时候算出来的结果已经不
    // 代表"用户在新内容到达前是不是贴底"了。
    private bool _shouldFollowPendingScroll;

    // LayoutUpdated 只需要挂一次，避免 DataContext 变化或窗口重复
    // Opened 时被重复挂载，导致同一个 handler 被调用多次。
    private bool _layoutUpdatedHooked;

    public SubtitleWindow()
    {
        InitializeComponent();

        Opened += OnOpened;
        Closing += OnClosing;

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

            // 不管是用户手动拖滚动条/滚轮，还是我们自己调用 ScrollToEnd()
            // 导致的滚动，都会触发这个事件——两种情况都应该更新
            // "现在是不是贴底"这个状态，不需要区分是谁导致的滚动：
            // 我们自己滚到底之后，_isNearBottom 变成 true 也是正确的。
            scrollViewerForDrag.ScrollChanged += OnScrollChanged;
        }

        AttachResizeHandle("ResizeLeft", WindowEdge.West);
        AttachResizeHandle("ResizeRight", WindowEdge.East);
        AttachResizeHandle("ResizeTop", WindowEdge.North);
        AttachResizeHandle("ResizeBottom", WindowEdge.South);
        AttachResizeHandle("ResizeBottomRight", WindowEdge.SouthEast);

        var lockButton = this.FindControl<Button>("LockButton");
        if (lockButton is not null)
        {
            lockButton.Click += OnLockButtonClick;
        }
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer) return;
        UpdateIsNearBottom(scrollViewer);
    }

    private void UpdateIsNearBottom(ScrollViewer scrollViewer)
    {
        var maxScrollableY = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        // 内容还没超出可见区域（不需要滚动）的情况下，maxScrollableY 是
        // 0，Offset.Y 也必然是 0，天然满足"贴底"——不需要单独判断这种
        // 情况。
        _isNearBottom = scrollViewer.Offset.Y >= maxScrollableY - NearBottomThresholdPx;
    }

    /// <summary>只在真的有新的识别/翻译结果加进来时触发——不在这里
    /// 立刻滚动，这时候新加的这一项还没真正渲染出来，算出来的内容
    /// 总高度还是旧的，ScrollToEnd() 滚不到真正的底部。这里只做两件
    /// 事：标记"下一次布局稳定后需要滚动一次"，以及把"这一刻用户是不是
    /// 贴底"存下来供那时候用。</summary>
    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;

        _pendingAutoScroll = true;
        _shouldFollowPendingScroll = _isNearBottom;
    }

    /// <summary>布局更新事件——触发得非常频繁，不只是内容变化会触发，
    /// 调整窗口大小、拖动、甚至我们自己调用 ScrollToEnd() 引起的重新
    /// 布局都会触发这个事件。之前的版本没有过滤这一点，导致
    /// "滚动 -> 触发布局更新 -> 又滚动 -> 又触发布局更新"停不下来，
    /// 把鼠标滚轮的操作每一帧都覆盖掉。现在只有 _pendingAutoScroll
    /// 为真（也就是真的有新内容在等着这次滚动）才会执行，执行完立刻
    /// 清掉标记，后续因为这次滚动本身引发的 LayoutUpdated 不会再重复
    /// 触发滚动，用户可以正常用鼠标滚轮翻看。
    ///
    /// 另外，LayoutUpdated 触发时 ScrollViewer 的 Extent 可能还没反映
    /// 新项的高度（新项的测量/排列还没传递上来），所以这里用
    /// Dispatcher.UIThread.Post 延迟到下一帧再滚动，确保滚到真正的底部。</summary>
    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_pendingAutoScroll) return;
        _pendingAutoScroll = false;

        if (!_shouldFollowPendingScroll) return;

        var scrollViewer = this.FindControl<ScrollViewer>("ContentScrollViewer");
        if (scrollViewer is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            scrollViewer.ScrollToEnd();
        }, DispatcherPriority.Background);
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

        // 每次显示都重置贴底状态——窗口可能是 Hide() 之后复用 Show()
        // 显示的，上一次隐藏前用户可能滚到了中间，如果不重置，
        // 新内容到达时不会自动跟随滚动。
        _isNearBottom = true;

        // LayoutUpdated 只挂一次，避免窗口重复 Opened 时重复挂载。
        if (!_layoutUpdatedHooked)
        {
            var contentControl = this.FindControl<ItemsControl>("ContentItemsControl");
            if (contentControl is not null)
            {
                contentControl.LayoutUpdated += OnLayoutUpdated;
                _layoutUpdatedHooked = true;
            }
        }

        // 订阅 CollectionChanged——先取消再订阅，避免窗口复用、
        // DataContext 变化等情况下重复订阅导致 handler 被调用多次。
        if (DataContext is SubtitleWindowViewModel vm)
        {
            vm.Items.CollectionChanged -= OnItemsCollectionChanged;
            vm.Items.CollectionChanged += OnItemsCollectionChanged;
        }

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