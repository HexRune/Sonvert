using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.Services.Characters;
using Sonvert.App.Settings;

namespace Sonvert.App.ViewModels;

public class LanguageOption
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
}

public partial class HomeViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ICharacterRepository _characterRepository;

    [ObservableProperty]
    private string _modelPrecision;

    [ObservableProperty]
    private string _targetLanguage;
    [ObservableProperty]
    private string _recognitionLanguage;

    public ObservableCollection<LanguageOption> RecognitionLanguageOptions { get; } = new()
    {
        new LanguageOption { Code = "auto", DisplayName = "自动识别" },
        new LanguageOption { Code = "zh", DisplayName = "只说中文" },
        new LanguageOption { Code = "en", DisplayName = "只说英文" },
    };

    partial void OnRecognitionLanguageChanged(string value)
    {
        _settingsService.Current.RecognitionLanguage = value;
        _ = _settingsService.SaveAsync();
    }
    public ObservableCollection<LanguageOption> TargetLanguageOptions { get; } = new()
    {
        new LanguageOption { Code = "zh", DisplayName = "中文" },
        new LanguageOption { Code = "en", DisplayName = "英文" },
    };

    public ObservableCollection<Character> Characters { get; } = new();

    [ObservableProperty]
    private Character? _selectedCharacter;

    public bool IsFp32Selected
    {
        get => ModelPrecision == "fp32";
        set { if (value) ModelPrecision = "fp32"; }
    }

    public bool IsInt8Selected
    {
        get => ModelPrecision == "int8";
        set { if (value) ModelPrecision = "int8"; }
    }

    public event EventHandler? StartTranslationRequested;

    public HomeViewModel(ISettingsService settingsService, ICharacterRepository characterRepository)
    {
        _settingsService = settingsService;
        _characterRepository = characterRepository;
        _recognitionLanguage = settingsService.Current.RecognitionLanguage;

        _modelPrecision = settingsService.Current.ModelPrecision;
        _targetLanguage = settingsService.Current.TargetLanguage;

        _ = RefreshCharactersAsync();
    }

    /// <summary>重新拉一次角色列表——在"声音克隆"页面新建/删除角色之后，
    /// 回到首页需要看到最新列表，不能只在构造函数里加载一次。
    /// MainViewModel 在导航切换到"主页"时会调用这个方法。</summary>
    public async Task RefreshCharactersAsync()
    {
        var currentActiveId = _settingsService.Current.ActiveCharacterId;

        Characters.Clear();
        foreach (var character in await _characterRepository.GetAllAsync())
        {
            Characters.Add(character);
        }

        // 恢复之前选中的角色（按 Id 重新匹配对象，不能直接复用旧的
        // SelectedCharacter 引用——那是上一次加载出来的旧对象实例）。
        SelectedCharacter = Characters.FirstOrDefault(c => c.Id == currentActiveId);
    }

    partial void OnModelPrecisionChanged(string value)
    {
        _settingsService.Current.ModelPrecision = value;
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(IsFp32Selected));
        OnPropertyChanged(nameof(IsInt8Selected));
    }

    partial void OnTargetLanguageChanged(string value)
    {
        _settingsService.Current.TargetLanguage = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnSelectedCharacterChanged(Character? value)
    {
        _settingsService.Current.ActiveCharacterId = value?.Id;
        _ = _settingsService.SaveAsync();
    }

    [RelayCommand]
    private void StartTranslation()
    {
        StartTranslationRequested?.Invoke(this, EventArgs.Empty);
    }
}