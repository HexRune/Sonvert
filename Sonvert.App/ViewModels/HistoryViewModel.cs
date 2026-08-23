using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Data;
using Sonvert.App.Models;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.History;

namespace Sonvert.App.ViewModels;

/// <summary>下拉框里的一个日期筛选项。Date 为 null 代表"全部"这个特殊选项，
/// 不对应任何具体日期，选中它时不按日期过滤，也不允许"删除这一天"这个操作
/// （删除整天的功能只在选中具体某一天时才有意义）。</summary>
public class HistoryDateFilterOption
{
    public DateOnly? Date { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// "历史记录"页面。核心交互：选一个日期筛选项（或"全部"）-> 列表刷新 ->
/// 每条记录可以单独播放原文/译文音频、单独删除；选中具体某一天时还能
/// "删除这一天全部"。
///
/// 导航到这个页面时，MainViewModel 会调用 RefreshAsync()——历史记录会
/// 随时因为直播会话进行中而增长，不能只在页面第一次创建时加载一次，
/// 每次切进来都要看到最新数据。
/// </summary>
public partial class HistoryViewModel : ViewModelBase
{
    private readonly IHistoryRepository _historyRepository;
    private readonly IAudioPlaybackService _playbackService;

    public ObservableCollection<HistoryDateFilterOption> DateFilterOptions { get; } = new();

    [ObservableProperty]
    private HistoryDateFilterOption? _selectedDateFilter;

    public ObservableCollection<HistoryEntry> Entries { get; } = new();

    /// <summary>"删除这一天全部"按钮是否可用——只有选中了具体某一天
    /// （不是"全部"）时才有意义，绑定到按钮的 IsEnabled。</summary>
    public bool CanDeleteSelectedDate => SelectedDateFilter?.Date is not null;

    public HistoryViewModel(IHistoryRepository historyRepository, IAudioPlaybackService playbackService)
    {
        _historyRepository = historyRepository;
        _playbackService = playbackService;
    }

    public async Task RefreshAsync()
    {
        var previousSelection = SelectedDateFilter?.Date;

        DateFilterOptions.Clear();
        DateFilterOptions.Add(new HistoryDateFilterOption { Date = null, DisplayName = "全部" });

        foreach (var date in await _historyRepository.GetDistinctDatesAsync())
        {
            DateFilterOptions.Add(new HistoryDateFilterOption
            {
                Date = date,
                DisplayName = date.ToString("yyyy-MM-dd"),
            });
        }

        // 尽量恢复之前选中的日期（比如删除某一天之后重新加载，之前选的
        // 那天已经不在列表里了，会自动落回"全部"，这是合理的默认行为）。
        SelectedDateFilter = DateFilterOptions.FirstOrDefault(o => o.Date == previousSelection)
            ?? DateFilterOptions[0];
    }

    partial void OnSelectedDateFilterChanged(HistoryDateFilterOption? value)
    {
        OnPropertyChanged(nameof(CanDeleteSelectedDate));
        _ = ReloadEntriesAsync();
    }

    private async Task ReloadEntriesAsync()
    {
        Entries.Clear();

        var entries = SelectedDateFilter?.Date is { } date
            ? await _historyRepository.GetByDateAsync(date)
            : await _historyRepository.GetAllAsync();

        foreach (var entry in entries)
        {
            Entries.Add(entry);
        }
    }

    [RelayCommand]
    private async Task PlaySourceAsync(HistoryEntry entry)
    {
        await PlayRelativePathAsync(entry.SourceAudioRelativePath);
    }

    [RelayCommand]
    private async Task PlayTranslatedAsync(HistoryEntry entry)
    {
        if (entry.TranslatedAudioRelativePath is not null)
        {
            await PlayRelativePathAsync(entry.TranslatedAudioRelativePath);
        }
    }

    private async Task PlayRelativePathAsync(string relativePath)
    {
        var absolutePath = Path.Combine(AppDbContext.AppDataRoot, relativePath);
        if (!File.Exists(absolutePath)) return;

        var bytes = await File.ReadAllBytesAsync(absolutePath);
        await _playbackService.PlayAsync(bytes);
    }

    [RelayCommand]
    private async Task DeleteEntryAsync(HistoryEntry entry)
    {
        await _historyRepository.DeleteEntryAsync(entry.Id);
        Entries.Remove(entry);
    }

    [RelayCommand]
    private async Task DeleteSelectedDateAsync()
    {
        if (SelectedDateFilter?.Date is not { } date) return;

        await _historyRepository.DeleteDateAsync(date);
        await RefreshAsync(); // 这一天整个没了，重新加载日期列表本身也要刷新，不只是条目列表
    }
}