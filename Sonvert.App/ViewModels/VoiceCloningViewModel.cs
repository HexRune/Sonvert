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
using Sonvert.App.Services.Characters;

namespace Sonvert.App.ViewModels;

public partial class VoiceCloningViewModel : ViewModelBase
{
    private readonly ICharacterRepository _characterRepository;
    private readonly IAudioRecordingService _recordingService;
    private readonly IAudioPlaybackService _playbackService;

    public ObservableCollection<Character> Characters { get; } = new();

    [ObservableProperty]
    private Character? _selectedCharacter;

    [ObservableProperty]
    private string _newCharacterName = string.Empty;

    public ObservableCollection<EmotionSlotViewModel> EmotionSlots { get; } = new();

    public VoiceCloningViewModel(
        ICharacterRepository characterRepository,
        IAudioRecordingService recordingService,
        IAudioPlaybackService playbackService)
    {
        _characterRepository = characterRepository;
        _recordingService = recordingService;
        _playbackService = playbackService;

        _ = LoadCharactersAsync();
    }

    private async Task LoadCharactersAsync()
    {
        Characters.Clear();
        foreach (var character in await _characterRepository.GetAllAsync())
        {
            Characters.Add(character);
        }
    }

    partial void OnSelectedCharacterChanged(Character? value)
    {
        EmotionSlots.Clear();
        if (value is null) return;

        foreach (var (emotion, scriptText) in EmotionScripts.Defaults)
        {
            var existingClip = value.EmotionClips.FirstOrDefault(c => c.Emotion == emotion);

            var savedAudioPath = existingClip is null
                ? null
                : Path.Combine(AppDbContext.AppDataRoot, existingClip.RelativeAudioPath);

            EmotionSlots.Add(new EmotionSlotViewModel(
                emotion,
                existingClip?.PromptText ?? scriptText,
                isRecorded: existingClip is not null,
                savedAudioPath,
                _recordingService,
                _playbackService,
                SaveClipAsync));
        }
    }

    private async Task SaveClipAsync(string emotion, string promptText, byte[] audioData)
    {
        if (SelectedCharacter is null) return;

        var characterId = SelectedCharacter.Id;

        var trimmedAudio = AudioTrimming.TrimSilence(audioData);
        await _characterRepository.SaveEmotionClipAsync(characterId, emotion, trimmedAudio, promptText);

        await LoadCharactersAsync();
        SelectedCharacter = Characters.FirstOrDefault(c => c.Id == characterId);
    }

    [RelayCommand]
    private async Task CreateCharacterAsync()
    {
        if (string.IsNullOrWhiteSpace(NewCharacterName)) return;

        var character = await _characterRepository.CreateAsync(NewCharacterName);
        NewCharacterName = string.Empty;
        await LoadCharactersAsync();
        SelectedCharacter = Characters.FirstOrDefault(c => c.Id == character.Id);
    }

    [RelayCommand]
    private async Task DeleteCharacterAsync(Character character)
    {
        await _characterRepository.DeleteAsync(character.Id);
        if (SelectedCharacter?.Id == character.Id)
        {
            SelectedCharacter = null;
        }
        await LoadCharactersAsync();
    }
}

public partial class EmotionSlotViewModel : ObservableObject
{
    private readonly IAudioRecordingService _recordingService;
    private readonly IAudioPlaybackService _playbackService;
    private readonly Func<string, string, byte[], Task> _onSave;

    /// <summary>已保存在磁盘上的录音路径——刚打开软件、还没重新录制过时，
    /// 试听靠的就是这个；一旦录了新的（_pendingAudio 有值），优先播放
    /// 刚录的，不是这份旧的。</summary>
    private readonly string? _savedAudioPath;

    public string Emotion { get; }

    [ObservableProperty]
    private string _scriptText;

    [ObservableProperty]
    private bool _isRecorded;

    [ObservableProperty]
    private bool _isRecording;

    private byte[]? _pendingAudio;

    public bool HasPendingAudio => _pendingAudio is not null;

    /// <summary>试听按钮是否可用——有刚录的，或者磁盘上已经有保存过的录音，
    /// 满足其一就能试听。</summary>
    public bool CanPreview => _pendingAudio is not null || _savedAudioPath is not null;

    public EmotionSlotViewModel(
        string emotion,
        string initialScriptText,
        bool isRecorded,
        string? savedAudioPath,
        IAudioRecordingService recordingService,
        IAudioPlaybackService playbackService,
        Func<string, string, byte[], Task> onSave)
    {
        Emotion = emotion;
        _scriptText = initialScriptText;
        _isRecorded = isRecorded;
        _savedAudioPath = savedAudioPath;
        _recordingService = recordingService;
        _playbackService = playbackService;
        _onSave = onSave;
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        if (IsRecording)
        {
            _ = StopAndCaptureAsync();
        }
        else
        {
            _recordingService.StartRecording();
            IsRecording = true;
        }
    }

    private async Task StopAndCaptureAsync()
    {
        var rawAudio = await _recordingService.StopRecordingAsync();
        _pendingAudio = AudioTrimming.TrimSilence(rawAudio);
        IsRecording = false;
        OnPropertyChanged(nameof(HasPendingAudio));
        OnPropertyChanged(nameof(CanPreview));
    }

    [RelayCommand]
    private async Task PlayPendingAsync()
    {
        if (_pendingAudio is not null)
        {
            await _playbackService.PlayAsync(_pendingAudio);
        }
        else if (_savedAudioPath is not null && File.Exists(_savedAudioPath))
        {
            var savedBytes = await File.ReadAllBytesAsync(_savedAudioPath);
            await _playbackService.PlayAsync(savedBytes);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_pendingAudio is null) return;

        await _onSave(Emotion, ScriptText, _pendingAudio);
        IsRecorded = true;
        _pendingAudio = null;
        OnPropertyChanged(nameof(HasPendingAudio));
        // 注意：这里不重置 CanPreview——保存之后这个槽位对应的
        // VoiceCloningViewModel 会整体刷新重建（因为 SelectedCharacter
        // 会重新赋值触发 OnSelectedCharacterChanged），这个实例本身
        // 接下来就会被新实例替换掉，不需要特地维护 _savedAudioPath
        // 指向"刚保存的这份"，下次重建时会正确指向新路径。
    }
}