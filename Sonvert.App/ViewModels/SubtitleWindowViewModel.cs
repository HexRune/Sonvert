using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.Settings;

namespace Sonvert.App.ViewModels;

/// <summary>
/// 悬浮字幕窗口的 ViewModel。Items 是按时间顺序排列的完整列表（旧的在前、
/// 新的在后），跟主页"实时翻译"页面的 Results（新的在前）刚好相反——
/// 那边是给"最新的一眼就能看到"设计的，这里是给"像聊天记录一样往下滚"
/// 设计的，所以维护一份独立的、顺序相反的镜像集合，而不是直接复用
/// Results 或者在界面上简单地反转显示顺序（反转显示顺序没法支持"新内容
/// 从底部进入、旧内容自然被推上去"这种滚动观感）。
///
/// 集合里的每一项还是同一个 RecognitionResultItem 对象引用（不是拷贝），
/// 所以译文异步补上时，绑定在 ItemsControl 上的界面会自动跟着刷新，
/// 不需要像之前那样手动订阅每一项的 PropertyChanged——这是把整个集合
/// 展示出来（而不是只展示某一条）之后，白得的一个简化。
/// </summary>
public partial class SubtitleWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly LiveTranslationViewModel _liveTranslationViewModel;

    public ObservableCollection<RecognitionResultItem> Items { get; } = new();

    [ObservableProperty] private bool _showSourceText;
    [ObservableProperty] private double _fontSize;
    [ObservableProperty] private string _textColor;
    [ObservableProperty] private double _backgroundOpacity;

    [ObservableProperty] private bool _isLocked;

    public SubtitleWindowViewModel(ISettingsService settingsService, LiveTranslationViewModel liveTranslationViewModel)
    {
        _settingsService = settingsService;
        _liveTranslationViewModel = liveTranslationViewModel;

        var s = settingsService.Current;
        _showSourceText = s.SubtitleShowSourceText;
        _fontSize = s.SubtitleFontSize;
        _textColor = s.SubtitleTextColor;
        _backgroundOpacity = s.SubtitleBackgroundOpacity;

        // 把已经存在的识别结果先按时间顺序（Results 是新的在前，
        // 这里要反过来）灌一遍进来，处理"字幕窗口是中途才打开"的情况——
        // 不这么做的话，打开字幕窗口之前说过的话不会出现在列表里。
        for (var i = _liveTranslationViewModel.Results.Count - 1; i >= 0; i--)
        {
            Items.Add(_liveTranslationViewModel.Results[i]);
        }

        _liveTranslationViewModel.Results.CollectionChanged += OnResultsChanged;
    }

    private void OnResultsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Results 只会在最前面插入新项（Insert(0, item)），所以这里
        // 不需要处理"插入到中间"或者"删除"这类复杂情况，只处理新增。
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (RecognitionResultItem newItem in e.NewItems)
            {
                Items.Add(newItem); // 加到我们这份列表的末尾，保持时间顺序
            }
        }
    }

    [RelayCommand]
    private void Lock()
    {
        IsLocked = true;
    }

    public void Unlock()
    {
        IsLocked = false;
    }
}