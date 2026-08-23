using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.ViewModels;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Sonvert.App.ViewModels;

/// <summary>
/// 主窗口的 ViewModel：侧边栏导航状态 + 根据选中项切换右侧内容区显示
/// 哪个页面的 ViewModel。页面切换靠 ViewLocator（Avalonia MVVM 模板自带）
/// 按 ViewModel 类型名自动找到对应 View，这里只需要把 CurrentPage 换成
/// 正确的 ViewModel 实例，View 那边不用手动管。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly LiveTranslationViewModel _liveTranslationViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly HomeViewModel _homeViewModel;
    private readonly VoiceCloningViewModel _voiceCloningViewModel;

    private readonly HistoryViewModel _historyViewModel;

    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new NavItem { Title = "主页" },
        new NavItem { Title = "实时翻译" },
        new NavItem { Title = "声音克隆" },
        new NavItem { Title = "发声角色" },
        new NavItem { Title = "历史记录" },
        new NavItem { Title = "识别对比测试" },
        new NavItem { Title = "设置" },
    };

    [ObservableProperty]
    private NavItem? _selectedNavItem;

    /// <summary>
    /// 当前应该在右侧内容区显示的页面 ViewModel。目前只有"实时翻译"
    /// 这一个页面真正搭了，其他菜单项选中时这个值是 null——
    /// MainWindow.axaml 里对 null 有专门处理（显示"页面待搭建"占位文字），
    /// 不会因为 ViewLocator 找不到对应 View 而报错。
    /// </summary>
    [ObservableProperty]
    private object? _currentPage;

    /// <summary>
    /// 翻译会话是否正在运行——顶部"正在监听麦克风..."状态栏和"停止"按钮
    /// 是否显示，看的是这个，不是"当前在哪个页面"，因为切页面不会停止
    /// 后台运行的会话。这里直接把 LiveTranslationViewModel.IsRunning
    /// 透传出来，而不是自己维护一份独立状态，避免两处状态不同步。
    /// </summary>
    public bool IsSessionActive => _liveTranslationViewModel.IsRunning;

    public MainViewModel(LiveTranslationViewModel liveTranslationViewModel,
        SettingsViewModel settingsViewModel,
        HomeViewModel homeViewModel,
        VoiceCloningViewModel voiceCloningViewModel,
        HistoryViewModel historyViewModel)
    {
        _liveTranslationViewModel = liveTranslationViewModel;
        _settingsViewModel = settingsViewModel;
        _homeViewModel = homeViewModel;
        _voiceCloningViewModel = voiceCloningViewModel;

        _homeViewModel.StartTranslationRequested += OnStartTranslationRequested;

        // LiveTranslationViewModel.IsRunning 变化时，通知 IsSessionActive
        // 也跟着刷新（IsSessionActive 是计算属性，没有自己的 [ObservableProperty]
        // 字段，所以要手动触发一次通知，让绑定到 IsSessionActive 的
        // 界面元素知道要重新读取这个值）。
        _liveTranslationViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LiveTranslationViewModel.IsRunning))
            {
                OnPropertyChanged(nameof(IsSessionActive));
            }
        };

        SelectedNavItem = NavItems[0];
        _historyViewModel = historyViewModel;
    }

    private async void OnStartTranslationRequested(object? sender, EventArgs e)
    {
        // 切换到"实时翻译"这个导航项——这一行会触发 OnSelectedNavItemChanged，
        // 自动把 CurrentPage 切成 _liveTranslationViewModel。
        SelectedNavItem = NavItems.First(item => item.Title == "实时翻译");

        await _liveTranslationViewModel.StartCommand.ExecuteAsync(null);
    }

    // [ObservableProperty] 生成的 partial 方法钩子：SelectedNavItem 变化时
    // 自动调用这个方法（命名约定固定是 On + 属性名 + Changed），根据选中的
    // 菜单项标题决定 CurrentPage 显示哪个页面 ViewModel。
    partial void OnSelectedNavItemChanged(NavItem? value)
    {
        CurrentPage = value?.Title switch
        {
            "主页" => RefreshAndGetHomeViewModel(),
            "设置" => _settingsViewModel,
            "实时翻译" => _liveTranslationViewModel,
            "声音克隆" => _voiceCloningViewModel,
            "历史记录" => RefreshAndGetHistoryViewModel(),
            _ => null, // 其他页面还没搭，先显示占位
        };
    }
    private HistoryViewModel RefreshAndGetHistoryViewModel()
    {
        _ = _historyViewModel.RefreshAsync();
        return _historyViewModel;
    }
    private HomeViewModel RefreshAndGetHomeViewModel()
    {
        _ = _homeViewModel.RefreshCharactersAsync();
        return _homeViewModel;
    }

    /// <summary>
    /// 顶部状态栏那个"停止"按钮绑的命令——之所以要在 MainViewModel 这里
    /// 单独转发一个命令，而不是直接在 XAML 里绑 LiveTranslationViewModel
    /// 的 StopCommand，是因为顶部状态栏在 MainWindow 里，跟当前选中哪个
    /// 页面无关（哪怕你切到"历史记录"页面，状态栏还在，这个按钮也该能用），
    /// 而 MainWindow.axaml 的 x:DataType 是 MainViewModel，不是
    /// LiveTranslationViewModel，没法直接绑到后者的命令上。
    /// </summary>
    [RelayCommand]
    private async Task StopSessionAsync()
    {
        await _liveTranslationViewModel.StopCommand.ExecuteAsync(null);
    }
}