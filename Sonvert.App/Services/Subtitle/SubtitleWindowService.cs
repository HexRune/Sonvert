using Sonvert.App.Services.Native;
using Sonvert.App.Settings;
using Sonvert.App.ViewModels;
using Sonvert.App.Views;

namespace Sonvert.App.Services.Subtitle;

public class SubtitleWindowService : ISubtitleWindowService
{
    private readonly ISettingsService _settingsService;
    private readonly SubtitleWindowViewModel _viewModel;
    private SubtitleWindow? _window;

    public SubtitleWindowService(ISettingsService settingsService, SubtitleWindowViewModel viewModel)
    {
        _settingsService = settingsService;
        _viewModel = viewModel;
    }

    public void Show()
    {
        if (_window is not null) return; // 已经开着，不重复创建

        var s = _settingsService.Current;

        _window = new SubtitleWindow
        {
            DataContext = _viewModel,
            Width = s.SubtitleWindowWidth,
            Height = s.SubtitleWindowHeight,
        };

        // 有记住的位置就用，没有就让 Avalonia 走默认策略——实际项目里
        // 可以在这里改成"屏幕底部居中"的计算逻辑，现在先用最简单的方式。
        if (s.SubtitleWindowX is { } x && s.SubtitleWindowY is { } y)
        {
            _window.Position = new Avalonia.PixelPoint((int)x, (int)y);
        }

        // 窗口移动/调整大小后，记住这次的位置和大小，下次打开沿用。
        _window.PositionChanged += (_, _) =>
        {
            _settingsService.Current.SubtitleWindowX = _window.Position.X;
            _settingsService.Current.SubtitleWindowY = _window.Position.Y;
            _ = _settingsService.SaveAsync();
        };
        _window.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name is nameof(SubtitleWindow.Width) or nameof(SubtitleWindow.Height))
            {
                _settingsService.Current.SubtitleWindowWidth = _window.Width;
                _settingsService.Current.SubtitleWindowHeight = _window.Height;
                _ = _settingsService.SaveAsync();
            }
        };

        _window.Show();
    }

    public void Hide()
    {
        _window?.Close();
        _window = null;
    }

    public void Unlock()
    {
        _viewModel.Unlock();

        if (_window is not null)
        {
            var hwnd = _window.TryGetPlatformHandle()?.Handle ?? System.IntPtr.Zero;
            if (hwnd != System.IntPtr.Zero)
            {
                Win32Interop.SetClickThrough(hwnd, false);
            }
        }
    }

    /// <summary>跟 Unlock() 对称——首页锁定开关打开时调用。跟悬浮字幕
    /// 窗口自己 Ctrl+Shift+L 快捷键触发的 LockCommand 是同一条路径
    /// （SubtitleWindow.axaml.cs 里那处调用），这里走 ViewModel 的
    /// LockCommand 而不是新开一个方法，避免两处各写一份"设状态+切
    /// 点透"的逻辑。</summary>
    public void Lock()
    {
        _viewModel.LockCommand.Execute(null);

        if (_window is not null)
        {
            var hwnd = _window.TryGetPlatformHandle()?.Handle ?? System.IntPtr.Zero;
            if (hwnd != System.IntPtr.Zero)
            {
                Win32Interop.SetClickThrough(hwnd, true);
            }
        }
    }

    public bool IsLocked => _viewModel.IsLocked;
}