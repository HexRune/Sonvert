using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.Services.Dialogs;
using Sonvert.App.Services.Translation;
using Sonvert.App.Settings;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sonvert.App.ViewModels;

/// <summary>
/// "词典管理"页面的 ViewModel——独立于"设置"页面之外单开的一个页面，
/// 专门管词典的增删、词条的增删、以及从 JSON 文件导入。首页翻译卡那边
/// 只保留"这次翻译用哪一个词典"这个单选下拉框，不做编辑，编辑都在这个
/// 页面完成——两边各司其职：这里管"词典库里有什么"，首页管"这次用
/// 哪一个"。
///
/// 所有增删操作都是立即生效（点了就调仓储层落库），不是"设置"页面那种
/// "改了要点保存才生效"的缓冲模式——词典管理本质上是一系列离散的操作
/// （新建一个、删一个、加一条词条），不是"编辑一批字段、攒起来一次性
/// 提交"，立即生效更符合这类操作的直觉，也是原来术语表增删词条就已经
/// 在用的模式，这里延续下来。
/// </summary>
public partial class GlossaryViewModel : ViewModelBase
{
    private readonly IGlossaryRepository _glossaryRepository;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;

    public ObservableCollection<GlossaryDictionary> Dictionaries { get; } = new();

    /// <summary>当前选中、正在查看/编辑词条的词典。选中后下面
    /// SelectedDictionaryEntries 会跟着刷新——GlossaryDictionary.Entries
    /// 本身是普通 List，不是 ObservableCollection，直接绑定它没法在
    /// 增删词条后自动刷新界面，所以单独维护一份 ObservableCollection
    /// 作为"当前选中词典的词条"这个视图专用的镜像。</summary>
    [ObservableProperty]
    private GlossaryDictionary? _selectedDictionary;

    public ObservableCollection<GlossaryEntry> SelectedDictionaryEntries { get; } = new();

    [ObservableProperty]
    private string _newDictionaryName = string.Empty;

    [ObservableProperty]
    private string _newGlossarySourceTerm = string.Empty;

    [ObservableProperty]
    private string _newGlossaryTargetTerm = string.Empty;

    /// <summary>导入成功/失败的提示文字，展示在"导入词典"按钮附近。
    /// 失败时把异常信息（JSON 格式错、缺字段这些）原样带出来，让用户
    /// 知道具体哪里不对，而不是只说一句"导入失败"。</summary>
    [ObservableProperty]
    private string? _importStatusMessage;

    public GlossaryViewModel(
        IGlossaryRepository glossaryRepository, IDialogService dialogService, ISettingsService settingsService)
    {
        _glossaryRepository = glossaryRepository;
        _dialogService = dialogService;
        _settingsService = settingsService;
        _ = RefreshAsync();
    }

    /// <summary>供 MainViewModel 在切换到这个页面时调用——每次进入页面都
    /// 重新拉一次最新数据，理由跟 HistoryViewModel/HomeViewModel 的
    /// RefreshXxxAsync 是一样的：词典数据可能在别处（虽然目前只有这个
    /// 页面会改）发生变化，进页面时刷新一次比指望内存里的缓存一直
    /// 保持最新更省心。</summary>
    public async Task RefreshAsync()
    {
        var selectedId = SelectedDictionary?.Id;

        Dictionaries.Clear();
        foreach (var dictionary in await _glossaryRepository.GetDictionariesAsync())
        {
            Dictionaries.Add(dictionary);
        }

        // 尽量保留刷新前的选中项（按 Id 找，因为重新查出来的是全新的
        // GlossaryDictionary 实例，引用不相等）——避免用户正在看某个
        // 词典的词条时，因为一次刷新（比如新增了另一个词典触发的整体
        // 重载）而莫名其妙跳回"没有选中任何词典"的状态。
        SelectedDictionary = selectedId is not null
            ? Dictionaries.FirstOrDefaultById(selectedId.Value)
            : Dictionaries.Count > 0 ? Dictionaries[0] : null;
    }

    partial void OnSelectedDictionaryChanged(GlossaryDictionary? value)
    {
        SelectedDictionaryEntries.Clear();
        if (value is null) return;

        foreach (var entry in value.Entries)
        {
            SelectedDictionaryEntries.Add(entry);
        }
    }

    [RelayCommand]
    private async Task AddDictionaryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewDictionaryName)) return;

        var dictionary = await _glossaryRepository.AddDictionaryAsync(NewDictionaryName.Trim());
        NewDictionaryName = string.Empty;
        await RefreshAsync();

        // 新建的词典直接选中，用户接下来大概率就是要往里面加词条，
        // 不用新建完了还要自己再点一下才能开始编辑。
        SelectedDictionary = Dictionaries.FirstOrDefaultById(dictionary.Id);
    }

    [RelayCommand]
    private async Task DeleteDictionaryAsync(GlossaryDictionary dictionary)
    {
        // 删词典是破坏性操作（连带删掉它名下所有词条，删了找不回来），
        // 二次确认——用法跟项目里其他破坏性操作（删角色、删历史记录）
        // 是同一个 IDialogService.ShowConfirmationAsync 模式。
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "删除词典",
            $"确定要删除词典\"{dictionary.Name}\"吗？这个词典下的 {dictionary.Entries.Count} 条术语会一并删除，删除后无法恢复。",
            "删除");
        if (!confirmed) return;

        await _glossaryRepository.DeleteDictionaryAsync(dictionary.Id);

        // 删掉的正好是首页当前选中的那个词典时，要把
        // ActiveGlossaryDictionaryId 也一并清空，不然首页下拉框会一直
        // 记着一个已经不存在的词典 Id——RefreshAsync 只刷新这个页面
        // 自己的列表，不会碰首页那边的状态，两件事要分开处理。
        // 这里选择"删了就静默回落到不使用词典"，而不是弹窗额外提醒，
        // 是因为用户主动删词典时大概率已经不打算再用它了。
        if (_settingsService.Current.ActiveGlossaryDictionaryId == dictionary.Id)
        {
            _settingsService.Current.ActiveGlossaryDictionaryId = null;
            await _settingsService.SaveAsync();
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task AddGlossaryEntryAsync()
    {
        if (SelectedDictionary is null) return;
        if (string.IsNullOrWhiteSpace(NewGlossarySourceTerm) || string.IsNullOrWhiteSpace(NewGlossaryTargetTerm))
            return;

        await _glossaryRepository.AddEntryAsync(
            SelectedDictionary.Id, NewGlossarySourceTerm.Trim(), NewGlossaryTargetTerm.Trim());
        NewGlossarySourceTerm = string.Empty;
        NewGlossaryTargetTerm = string.Empty;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteGlossaryEntryAsync(GlossaryEntry entry)
    {
        await _glossaryRepository.DeleteEntryAsync(entry.Id);
        SelectedDictionaryEntries.Remove(entry);

        // 词典列表里显示的词条数（如果界面上有展示）也要跟着刷新，
        // 不只是刷新右边的词条列表本身。
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ImportDictionaryAsync()
    {
        ImportStatusMessage = null;

        var json = await _dialogService.ShowOpenJsonFileAsync("选择要导入的词典 JSON 文件");
        if (json is null) return; // 用户取消了选择，不算失败，不用提示

        try
        {
            var dictionary = await _glossaryRepository.ImportFromJsonAsync(json, nameOverride: null);
            await RefreshAsync();
            SelectedDictionary = Dictionaries.FirstOrDefaultById(dictionary.Id);
            ImportStatusMessage = $"已导入词典\"{dictionary.Name}\"，共 {dictionary.Entries.Count} 条术语";
        }
        catch (JsonException ex)
        {
            ImportStatusMessage = $"导入失败：JSON 格式不正确（{ex.Message}）";
        }
        catch (System.InvalidOperationException ex)
        {
            ImportStatusMessage = $"导入失败：{ex.Message}";
        }
    }
}

/// <summary>按 Id 找 GlossaryDictionary 的小helper——每次刷新拿到的都是
/// 全新的实例（引用不相等），要恢复"刷新前选中的是哪个词典"这个状态
/// 只能按 Id 比较，不能按引用比较，所以单独抽一个扩展方法，避免在
/// ViewModel 里到处写同一句 LINQ。</summary>
internal static class GlossaryDictionaryCollectionExtensions
{
    public static GlossaryDictionary? FirstOrDefaultById(
        this ObservableCollection<GlossaryDictionary> dictionaries, int id)
    {
        foreach (var dictionary in dictionaries)
        {
            if (dictionary.Id == id) return dictionary;
        }
        return null;
    }
}
