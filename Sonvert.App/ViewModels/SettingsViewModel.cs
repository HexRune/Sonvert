using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sonvert.App.Models;
using Sonvert.App.Services.SenseVoice;
using Sonvert.App.Services.Translation;
using Sonvert.App.Settings;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using static Sonvert.App.Settings.AppSettings;

namespace Sonvert.App.ViewModels;

/// <summary>
/// "设置"页面的 ViewModel。设计思路：构造时把 AppSettings.Current 的值
/// 逐一拷贝到这里的 [ObservableProperty] 字段上（不直接绑定 AppSettings
/// 本身，因为 AppSettings 不是 ObservableObject，改了属性界面不会自动刷新，
/// 而且这样也能做到"改了但没点保存，切换页面/重启不生效"这个符合直觉的
/// 行为，而不是改一个字符边框就立刻生效）。点"保存"时再整体写回
/// AppSettings.Current 并落盘。
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _historyRetentionDays;
    private readonly ISettingsService _settingsService;

    // ---- SenseVoice ----
    [ObservableProperty] private int _senseVoicePort;
    [ObservableProperty] private string _senseVoiceExecutablePath = string.Empty;
    [ObservableProperty] private string _senseVoiceWorkingDirectory = string.Empty;
    [ObservableProperty] private string _vadModelPath = string.Empty;
    [ObservableProperty] private string _modelPrecision = "fp32";

    // ---- MT ----
    [ObservableProperty] private int _mtPort;
    [ObservableProperty] private string _mtExecutablePath = string.Empty;
    [ObservableProperty] private string _mtWorkingDirectory = string.Empty;
    [ObservableProperty] private string _translationProvider = "local";
    [ObservableProperty] private string _translationApiEndpoint = string.Empty;
    [ObservableProperty] private string _translationApiKey = string.Empty;
    [ObservableProperty] private string _translationApiModel = string.Empty;

    // ---- TTS ----
    [ObservableProperty] private int _ttsPort;
    [ObservableProperty] private string _ttsExecutablePath = string.Empty;
    [ObservableProperty] private string _ttsWorkingDirectory = string.Empty;
    [ObservableProperty] private string _ttsReferenceAudioLanguage = "zh";
    [ObservableProperty] private string _ttsProvider = "local";
    [ObservableProperty] private string _ttsApiEndpoint = string.Empty;
    [ObservableProperty] private string _ttsApiKey = string.Empty;
    [ObservableProperty] private string _ttsApiModel = string.Empty;

    [ObservableProperty]
    private string? _saveStatusMessage;

    public SettingsViewModel(ISettingsService settingsService, IGlossaryRepository glossaryRepository)
    {
        _settingsService = settingsService;
        _glossaryRepository = glossaryRepository;
        LoadFromSettings();
        _ = LoadGlossaryAsync();
    }

    private void LoadFromSettings()
    {
        var s = _settingsService.Current;
        HistoryRetentionDays = s.HistoryRetentionDays ?? 0;

        SenseVoicePort = s.SenseVoicePort;
        SenseVoiceExecutablePath = s.SenseVoiceExecutablePath;
        SenseVoiceWorkingDirectory = s.SenseVoiceWorkingDirectory;
        VadModelPath = s.VadModelPath;
        ModelPrecision = s.ModelPrecision;

        MtPort = s.MTPort;
        MtExecutablePath = s.MTExecutablePath;
        MtWorkingDirectory = s.MTWorkingDirectory;
        TranslationProvider = s.TranslationProvider;
        TranslationApiEndpoint = s.TranslationApiEndpoint;
        TranslationApiKey = s.TranslationApiKey;
        TranslationApiModel = s.TranslationApiModel;

        TtsPort = s.TTSPort;
        TtsExecutablePath = s.TTSExecutablePath;
        TtsWorkingDirectory = s.TTSWorkingDirectory;
        TtsReferenceAudioLanguage = s.TTSReferenceAudioLanguage;
        TtsProvider = s.TTSProvider;
        TtsApiEndpoint = s.TTSApiEndpoint;
        TtsApiKey = s.TTSApiKey;
        TtsApiModel = s.TTSApiModel;

    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var s = _settingsService.Current;
        s.HistoryRetentionDays = HistoryRetentionDays > 0 ? HistoryRetentionDays : null;

        s.SenseVoicePort = SenseVoicePort;
        s.SenseVoiceExecutablePath = SenseVoiceExecutablePath;
        s.SenseVoiceWorkingDirectory = SenseVoiceWorkingDirectory;
        s.VadModelPath = VadModelPath;
        s.ModelPrecision = ModelPrecision;

        s.MTPort = MtPort;
        s.MTExecutablePath = MtExecutablePath;
        s.MTWorkingDirectory = MtWorkingDirectory;
        s.TranslationProvider = TranslationProvider;
        s.TranslationApiEndpoint = TranslationApiEndpoint;
        s.TranslationApiKey = TranslationApiKey;
        s.TranslationApiModel = TranslationApiModel;

        s.TTSPort = TtsPort;
        s.TTSExecutablePath = TtsExecutablePath;
        s.TTSWorkingDirectory = TtsWorkingDirectory;
        s.TTSReferenceAudioLanguage = TtsReferenceAudioLanguage;
        s.TTSProvider = TtsProvider;
        s.TTSApiEndpoint = TtsApiEndpoint;
        s.TTSApiKey = TtsApiKey;
        s.TTSApiModel = TtsApiModel;

        s.TTSReferenceAudioByEmotion.Clear();

        await _settingsService.SaveAsync();

        // 已经启动的子进程不会因为改了设置就自动重启去应用新配置——
        // 端口/路径这类设置要重启程序才会在下次启动子进程时生效，
        // 这里明确提示一下，避免用户改完以为立刻生效了。
        SaveStatusMessage = "已保存，重启程序后生效";
    }
    // 关键词替换
    private readonly IGlossaryRepository _glossaryRepository;

    public ObservableCollection<GlossaryEntry> GlossaryEntries { get; } = new();

    [ObservableProperty]
    private string _newGlossarySourceTerm = string.Empty;

    [ObservableProperty]
    private string _newGlossaryTargetTerm = string.Empty;

    private async Task LoadGlossaryAsync()
    {
        GlossaryEntries.Clear();
        foreach (var entry in await _glossaryRepository.GetAllAsync())
        {
            GlossaryEntries.Add(entry);
        }
    }

    [RelayCommand]
    private async Task AddGlossaryEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGlossarySourceTerm) || string.IsNullOrWhiteSpace(NewGlossaryTargetTerm))
            return;

        await _glossaryRepository.AddAsync(NewGlossarySourceTerm, NewGlossaryTargetTerm);
        NewGlossarySourceTerm = string.Empty;
        NewGlossaryTargetTerm = string.Empty;
        await LoadGlossaryAsync();
    }

    [RelayCommand]
    private async Task DeleteGlossaryEntryAsync(GlossaryEntry entry)
    {
        await _glossaryRepository.DeleteAsync(entry.Id);
        GlossaryEntries.Remove(entry);
    }
}