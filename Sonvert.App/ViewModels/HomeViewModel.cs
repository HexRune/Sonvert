using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.Services.Audio;
using Sonvert.App.Services.Characters;
using Sonvert.App.Services.Subtitle;
using Sonvert.App.Settings;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Sonvert.App.ViewModels;

public class LanguageOption
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
}

/// <summary>
/// 首页——开播前最后确认一遍关键参数的地方，按"识别->翻译->合成"这条
/// 处理链路的顺序，分成三个板块。每个字段改了立刻写入 settings.json
/// （不等点保存按钮），因为这里的定位就是"随时调、随时生效"，跟"设置"
/// 页面那种"改完要点保存才生效"的定位不一样。
/// </summary>
public partial class HomeViewModel : ViewModelBase
{
    // 悬浮字幕
    private readonly ISubtitleWindowService _subtitleWindowService;

    [ObservableProperty]
    private bool _subtitleEnabled;
    private readonly ISettingsService _settingsService;
    private readonly ICharacterRepository _characterRepository;

    // ---- 语音识别板块 ----
    [ObservableProperty] private string _recognitionLanguage;
    [ObservableProperty] private string _modelPrecision;

    public ObservableCollection<AudioInputDeviceOption> AudioInputDevices { get; } = new();
    public ObservableCollection<AudioOutputDeviceOption> AudioOutputDevices { get; } = new();

    [ObservableProperty]
    private AudioOutputDeviceOption? _selectedOutputDevice;

    [ObservableProperty]
    private AudioInputDeviceOption? _selectedAudioInputDevice;

    [ObservableProperty]
    private bool _enableTtsPlayback;

    public ObservableCollection<LanguageOption> RecognitionLanguageOptions { get; } = new()
    {
        new LanguageOption { Code = "auto", DisplayName = "自动" },
        new LanguageOption { Code = "zh", DisplayName = "中文" },
        new LanguageOption { Code = "en", DisplayName = "英文" },
    };

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

    // ---- 翻译板块 ----
    [ObservableProperty] private string _targetLanguage;
    [ObservableProperty] private bool _glossaryEnabled;
    [ObservableProperty] private string _translationProvider;

    public ObservableCollection<LanguageOption> TargetLanguageOptions { get; } = new()
    {
        new LanguageOption { Code = "zh", DisplayName = "中文" },
        new LanguageOption { Code = "en", DisplayName = "英文" },
    };

    public bool IsTranslationLocalSelected
    {
        get => TranslationProvider == "local";
        set { if (value) TranslationProvider = "local"; }
    }

    public bool IsTranslationApiSelected
    {
        get => TranslationProvider == "api";
        set { if (value) TranslationProvider = "api"; }
    }

    // ---- 语音合成板块 ----
    [ObservableProperty] private Character? _selectedCharacter;
    [ObservableProperty] private string _ttsProvider;

    public ObservableCollection<Character> Characters { get; } = new();

    public bool IsTtsLocalSelected
    {
        get => TtsProvider == "local";
        set { if (value) TtsProvider = "local"; }
    }

    public bool IsTtsApiSelected
    {
        get => TtsProvider == "api";
        set { if (value) TtsProvider = "api"; }
    }

    public event EventHandler? StartTranslationRequested;

    public HomeViewModel(ISettingsService settingsService, 
        ICharacterRepository characterRepository,
        ISubtitleWindowService subtitleWindowService)
    {
        _settingsService = settingsService;
        _characterRepository = characterRepository;
        _subtitleWindowService = subtitleWindowService;

        _subtitleEnabled = settingsService.Current.SubtitleEnabled;

        var s = settingsService.Current;
        _recognitionLanguage = s.RecognitionLanguage;
        _modelPrecision = s.ModelPrecision;
        _targetLanguage = s.TargetLanguage;
        _glossaryEnabled = s.GlossaryEnabled;
        _translationProvider = s.TranslationProvider;
        _ttsProvider = s.TTSProvider;
        _enableTtsPlayback = s.EnableTtsPlayback;

        foreach (var device in Sonvert.App.Services.Audio.AudioInputDeviceEnumerator.GetAllOptions())
        {
            AudioInputDevices.Add(device);
        }
        foreach (var device in AudioOutputDeviceEnumerator.GetAllOptions())
        {
            AudioOutputDevices.Add(device);
        }
        _selectedOutputDevice = AudioOutputDevices.FirstOrDefault(d => d.Id == s.OutputDeviceId);

        // 按存储的设置值，找到对应的那个设备对象重新选中——设置里存的是
        // Kind+Id 这两个字符串，界面上要匹配回具体的 AudioInputDeviceOption
        // 实例，两者不能直接比较相等（不是同一个对象引用）。
        _selectedAudioInputDevice = AudioInputDevices.FirstOrDefault(d =>
            d.Kind.ToString() == s.InputDeviceKind && d.Id == s.InputDeviceId);

        _ = RefreshCharactersAsync();
    }

    partial void OnSubtitleEnabledChanged(bool value)
    {
        _settingsService.Current.SubtitleEnabled = value;
        _ = _settingsService.SaveAsync();

        if (value) _subtitleWindowService.Show();
        else _subtitleWindowService.Hide();
    }

    [RelayCommand]
    private void UnlockSubtitle()
    {
        _subtitleWindowService.Unlock();
    }

    partial void OnSelectedOutputDeviceChanged(AudioOutputDeviceOption? value)
    {
        if (value is null) return;
        _settingsService.Current.OutputDeviceId = value.Id;
        _ = _settingsService.SaveAsync();
    }

    partial void OnSelectedAudioInputDeviceChanged(AudioInputDeviceOption? value)
    {
        if (value is null) return;
        _settingsService.Current.InputDeviceKind = value.Kind.ToString();
        _settingsService.Current.InputDeviceId = value.Id;
        _ = _settingsService.SaveAsync();
    }

    partial void OnEnableTtsPlaybackChanged(bool value)
    {
        _settingsService.Current.EnableTtsPlayback = value;
        _ = _settingsService.SaveAsync();
    }

    public async Task RefreshCharactersAsync()
    {
        var currentActiveId = _settingsService.Current.ActiveCharacterId;

        Characters.Clear();
        foreach (var character in await _characterRepository.GetAllAsync())
        {
            Characters.Add(character);
        }

        SelectedCharacter = Characters.FirstOrDefault(c => c.Id == currentActiveId);
    }

    partial void OnRecognitionLanguageChanged(string value)
    {
        _settingsService.Current.RecognitionLanguage = value;
        _ = _settingsService.SaveAsync();
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

    partial void OnGlossaryEnabledChanged(bool value)
    {
        _settingsService.Current.GlossaryEnabled = value;
        _ = _settingsService.SaveAsync();
    }

    partial void OnTranslationProviderChanged(string value)
    {
        _settingsService.Current.TranslationProvider = value;
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(IsTranslationLocalSelected));
        OnPropertyChanged(nameof(IsTranslationApiSelected));
    }

    partial void OnTtsProviderChanged(string value)
    {
        _settingsService.Current.TTSProvider = value;
        _ = _settingsService.SaveAsync();
        OnPropertyChanged(nameof(IsTtsLocalSelected));
        OnPropertyChanged(nameof(IsTtsApiSelected));
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