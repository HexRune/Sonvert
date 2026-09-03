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
using Sonvert.App.Services.Dialogs;

namespace Sonvert.App.ViewModels;

/// <summary>
/// 声音克隆——每个角色的参考音频现在按"语言 x 情绪"两个维度组织：
/// 中文/英文各自独立一套 7 种情绪的录音（NEUTRAL/HAPPY/SAD/ANGRY/
/// FEARFUL/DISGUSTED/SURPRISED）。界面上用语言标签页切换（复用首页
/// "本地/API"那套浏览器式标签页的视觉样式，样式定义直接复制了一份到
/// VoiceCloningView.axaml 自己的 UserControl.Styles 里，没有跟首页共用
/// 同一份资源——两个页面各自独立维护一份完全一样的样式，是为了不去动
/// 首页已经调好的东西，如果以后想统一成一份共享资源，可以把这两处样式
/// 提取到 Styles/ 下的一个新文件里，现在先不做这层重构）。
///
/// 每种语言下，NEUTRAL 是"解锁"这个语言其他情绪录制入口的前提——
/// 没先录 NEUTRAL，其他情绪的录音卡片是锁住的、不能点。两种语言互相
/// 独立：可以只录中文、只录英文、或者两个都录，角色整体能不能用于
/// 合成的判断标准是"两种语言的 NEUTRAL 至少有一个存在"（见
/// Character.HasNoUsableVoice 和 ICharacterRepository.ResolveClipAsync）。
/// </summary>
public partial class VoiceCloningViewModel : ViewModelBase
{
    private readonly ICharacterRepository _characterRepository;
    private readonly IAudioRecordingService _recordingService;
    private readonly IAudioPlaybackService _playbackService;
    private readonly IDialogService _dialogService;

    public ObservableCollection<Character> Characters { get; } = new();

    [ObservableProperty]
    private Character? _selectedCharacter;

    /// <summary>当前在看哪种语言的情绪槽位——"zh"或"en"，驱动语言标签页
    /// 的选中状态，也决定 EmotionSlots 里装的是哪种语言的槽位。</summary>
    [ObservableProperty]
    private string _selectedLanguage = "zh";

    public bool IsChineseTabSelected
    {
        get => SelectedLanguage == "zh";
        set { if (value) SelectedLanguage = "zh"; }
    }

    public bool IsEnglishTabSelected
    {
        get => SelectedLanguage == "en";
        set { if (value) SelectedLanguage = "en"; }
    }

    public ObservableCollection<EmotionSlotViewModel> EmotionSlots { get; } = new();

    public VoiceCloningViewModel(
        ICharacterRepository characterRepository,
        IAudioRecordingService recordingService,
        IAudioPlaybackService playbackService,
        IDialogService dialogService)
    {
        _characterRepository = characterRepository;
        _recordingService = recordingService;
        _playbackService = playbackService;
        _dialogService = dialogService;

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
        RebuildEmotionSlots();
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        OnPropertyChanged(nameof(IsChineseTabSelected));
        OnPropertyChanged(nameof(IsEnglishTabSelected));
        RebuildEmotionSlots();
    }

    /// <summary>切换角色或者切换语言标签页时都要重建——两种情况下
    /// "该显示哪些槽位、哪些槽位该锁住"这两件事都要重新算一遍，
    /// 干脆合并成一个方法，不用在两个 partial 方法里各写一份。</summary>
    private void RebuildEmotionSlots()
    {
        EmotionSlots.Clear();
        if (SelectedCharacter is null) return;

        var language = SelectedLanguage;
        var scripts = EmotionScripts.GetDefaults(language);

        var clipsForLanguage = SelectedCharacter.EmotionClips
            .Where(c => c.Language == language)
            .ToList();

        // 这个语言的 NEUTRAL 有没有录——没录的话，除了 NEUTRAL 本身，
        // 其他情绪槽位全部锁住，不允许在没有 NEUTRAL 打底的情况下先录
        // 别的情绪（见类注释里"NEUTRAL 是解锁前提"的规则）。
        var hasNeutral = clipsForLanguage.Any(c => c.Emotion == "NEUTRAL");

        foreach (var (emotion, scriptText) in scripts)
        {
            var existingClip = clipsForLanguage.FirstOrDefault(c => c.Emotion == emotion);

            var savedAudioPath = existingClip is null
                ? null
                : Path.Combine(AppDbContext.AppDataRoot, existingClip.RelativeAudioPath);

            var isNeutral = emotion == "NEUTRAL";

            EmotionSlots.Add(new EmotionSlotViewModel(
                emotion,
                language,
                existingClip?.PromptText ?? scriptText,
                isRecorded: existingClip is not null,
                isRequired: isNeutral,
                isLocked: !isNeutral && !hasNeutral,
                savedAudioPath,
                _recordingService,
                _playbackService,
                SaveClipAsync));
        }
    }

    private async Task SaveClipAsync(string emotion, string language, string promptText, byte[] audioData)
    {
        if (SelectedCharacter is null) return;

        var characterId = SelectedCharacter.Id;

        var trimmedAudio = AudioTrimming.TrimSilence(audioData);
        await _characterRepository.SaveEmotionClipAsync(characterId, emotion, language, trimmedAudio, promptText);

        await LoadCharactersAsync();
        SelectedCharacter = Characters.FirstOrDefault(c => c.Id == characterId);
    }

    [RelayCommand]
    private async Task CreateCharacterAsync()
    {
        // 弹窗输入名字，不再用常驻的文本框——见类注释里对左侧角色列表
        // 视觉改版的说明。用户取消或者没输入任何内容都直接返回，不创建
        // 空名字的角色。
        var name = await _dialogService.ShowTextInputAsync("新建角色", "输入角色名称", "创建");
        if (string.IsNullOrWhiteSpace(name)) return;

        var character = await _characterRepository.CreateAsync(name);
        await LoadCharactersAsync();
        SelectedCharacter = Characters.FirstOrDefault(c => c.Id == character.Id);
    }

    [RelayCommand]
    private async Task DeleteCharacterAsync(Character character)
    {
        // 删除会把这个角色录制过的所有音频文件一起删掉，属于破坏性操作，
        // 弹一个确认框——跟"新建角色"那个纯输入框不一样，这个用的是
        // ConfirmationDialog（确认按钮是红色的，视觉上强调这个操作的
        // 严重程度）。
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "删除角色",
            $"确定要删除角色\"{character.Name}\"吗？这会同时删除它录制过的所有参考音频，且无法恢复。",
            "删除");
        if (!confirmed) return;

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
    private readonly Func<string, string, string, byte[], Task> _onSave;

    /// <summary>已保存在磁盘上的录音路径——刚打开软件、还没重新录制过时，
    /// 试听靠的就是这个；一旦录了新的（_pendingAudio 有值），优先播放
    /// 刚录的，不是这份旧的。</summary>
    private readonly string? _savedAudioPath;

    public string Emotion { get; }

    /// <summary>情绪的中文展示文本——跟 RecognitionResultItem.EmotionDisplay
    /// 用的是同一套翻译，两个地方都把 SenseVoice 的原始英文标签换成
    /// 用户看得懂的中文，保持全项目术语一致（这里不直接复用那个类的
    /// 静态方法，是因为两者所在的命名空间/职责不同，各自维护一份小
    /// switch 没什么维护成本，硬要抽公共方法反而绕）。</summary>
    public string EmotionDisplay => Emotion switch
    {
        "NEUTRAL" => "平静",
        "HAPPY" => "开心",
        "SAD" => "难过",
        "ANGRY" => "生气",
        "FEARFUL" => "害怕",
        "DISGUSTED" => "厌恶",
        "SURPRISED" => "惊讶",
        _ => Emotion,
    };

    /// <summary>这条槽位是哪种语言的——保存时要带上这个信息，
    /// ICharacterRepository.SaveEmotionClipAsync 需要知道存到中文那份
    /// 还是英文那份。</summary>
    public string Language { get; }

    /// <summary>是不是"必需"槽位——目前只有每种语言的 NEUTRAL 是必需的，
    /// 界面上必需和可选两种槽位要有不同的视觉标记（见
    /// VoiceCloningView.axaml 里 Required/Optional 这两个样式类）。</summary>
    public bool IsRequired { get; }

    /// <summary>必需/可选标签上显示的文字——沿用项目里一贯的"给 XAML
    /// 暴露已经算好的展示值"写法，不用专门为这一处建 IValueConverter。</summary>
    public string RequirementLabel => IsRequired ? "必需" : "可选";

    [ObservableProperty]
    private string _scriptText;

    [ObservableProperty]
    private bool _isRecorded;

    [ObservableProperty]
    private bool _isRecording;

    /// <summary>录音按钮上显示的文字——原来的实现直接用
    /// StringFormat 把 IsRecording 这个布尔值格式化出来，按钮上会显示
    /// 字面意义上的 "True"/"False"，不是真的按钮文案，这次顺手修掉。</summary>
    public string RecordButtonText => IsRecording ? "■ 停止" : "● 录音";

    /// <summary>是不是锁住状态——这个语言的 NEUTRAL 还没录时，除了
    /// NEUTRAL 本身，其他情绪槽位都是锁住的（IsRequired 为 true 的槽位
    /// 永远不会被锁，不然就死锁了：锁住的原因正是"NEUTRAL 还没录"，
    /// NEUTRAL 自己不可能被自己锁住）。锁住状态下录音/试听/保存这些
    /// 交互整体禁用，见 View 里绑到卡片外层容器 IsEnabled 的地方。</summary>
    public bool IsLocked { get; }

    private byte[]? _pendingAudio;

    public bool HasPendingAudio => _pendingAudio is not null;

    /// <summary>试听按钮是否可用——有刚录的，或者磁盘上已经有保存过的录音，
    /// 满足其一就能试听。</summary>
    public bool CanPreview => _pendingAudio is not null || _savedAudioPath is not null;

    public EmotionSlotViewModel(
        string emotion,
        string language,
        string initialScriptText,
        bool isRecorded,
        bool isRequired,
        bool isLocked,
        string? savedAudioPath,
        IAudioRecordingService recordingService,
        IAudioPlaybackService playbackService,
        Func<string, string, string, byte[], Task> onSave)
    {
        Emotion = emotion;
        Language = language;
        _scriptText = initialScriptText;
        _isRecorded = isRecorded;
        IsRequired = isRequired;
        IsLocked = isLocked;
        _savedAudioPath = savedAudioPath;
        _recordingService = recordingService;
        _playbackService = playbackService;
        _onSave = onSave;
    }

    partial void OnIsRecordingChanged(bool value)
    {
        OnPropertyChanged(nameof(RecordButtonText));
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

        await _onSave(Emotion, Language, ScriptText, _pendingAudio);
        IsRecorded = true;
        _pendingAudio = null;
        OnPropertyChanged(nameof(HasPendingAudio));
        // 注意：这里不重置 CanPreview——保存之后这个槽位对应的
        // VoiceCloningViewModel 会整体刷新重建（因为 SelectedCharacter
        // 会重新赋值触发 OnSelectedCharacterChanged），这个实例本身
        // 接下来就会被新实例替换掉，不需要特地维护 _savedAudioPath
        // 指向"刚保存的这份"，下次重建时会正确指向新路径。
        //
        // 还有一件事这次改动之后会自然发生：如果刚保存的是 NEUTRAL，
        // RebuildEmotionSlots 重建时 hasNeutral 会变成 true，其他情绪
        // 槽位的 IsLocked 会正确地从 true 变成 false——不需要在这里
        // 手动解锁别的槽位实例，反正整批都会重建。
    }
}
