using CommunityToolkit.Mvvm.ComponentModel;

namespace Sonvert.App.Models;

/// <summary>
/// 界面上展示用的一条识别结果。Text/Emotion/Event 识别完成时就确定了，
/// 用 init-only；TranslatedText 是识别完成后异步补上的，所以做成
/// ObservableProperty——翻译结果回来时更新它，界面能自动刷新。
/// </summary>
public partial class RecognitionResultItem : ObservableObject
{
    public required string Text { get; init; }

    /// <summary>SenseVoice 原样返回的情绪标签（NEUTRAL/HAPPY/SAD/...），
    /// 界面显示请用下面的 EmotionDisplay，这个原始值目前只在
    /// EmotionDisplay 内部转换时用到，先保留是因为以后落历史记录/调试
    /// 排查问题时可能用得上原始标签。</summary>
    public string? Emotion { get; init; }

    /// <summary>SenseVoice 原样返回的事件/场景标签（Speech/BGM/...），
    /// 同上，界面显示请用 EventDisplay。</summary>
    public string? Event { get; init; }

    [ObservableProperty]
    private string? _translatedText;

    // ---- 延迟统计（毫秒）----
    // AsrLatencyMs 识别完成的那一刻就确定了，用 init-only；后两段是
    // 异步补上的（翻译、合成都完成之后才知道各自耗时多久），做成
    // ObservableProperty，界面上的"总延迟"数字会随着这句话的处理进度
    // 逐步增长，不是一开始就显示最终值——跟 TranslatedText 是同样的
    // "先显示部分结果，后面再补全"的设计。
    public int AsrLatencyMs { get; init; }

    [ObservableProperty]
    private int? _translationLatencyMs;

    [ObservableProperty]
    private int? _ttsLatencyMs;

    /// <summary>总延迟——三段里已经知道的加起来，用于界面上显示一个
    /// 总数。跟 HistoryEntry.TotalLatencyMs 是同样的算法，这里单独
    /// 再写一份而不是共用，是因为两个类分别属于"实时展示"和"数据库
    /// 实体"两层，没有必要为了共用几行加法逻辑而在两层之间建立依赖。</summary>
    public int TotalLatencyMs => AsrLatencyMs + (TranslationLatencyMs ?? 0) + (TtsLatencyMs ?? 0);

    /// <summary>界面上直接显示的延迟文字，比如"890ms"。</summary>
    public string TotalLatencyDisplay => $"{TotalLatencyMs}ms";

    partial void OnTranslationLatencyMsChanged(int? value)
    {
        OnPropertyChanged(nameof(TotalLatencyMs));
        OnPropertyChanged(nameof(TotalLatencyDisplay));
    }

    partial void OnTtsLatencyMsChanged(int? value)
    {
        OnPropertyChanged(nameof(TotalLatencyMs));
        OnPropertyChanged(nameof(TotalLatencyDisplay));
    }

    /// <summary>情绪的中文展示文本——之前是把 Emotion 原样显示出来
    /// （"NEUTRAL"、"EMO_UNKNOWN"这种大写英文标签，用户看不懂），
    /// 现在翻译成中文。取值集合对应 Python 端 model_manager.py 里的
    /// KNOWN_EMOTIONS 常量，两边要保持同步——以后 Python 那边如果扩充了
    /// 新的情绪标签，这里也要补一个对应的翻译分支，不然新标签会落到
    /// 最后的兜底分支，原样显示英文（至少不会显示空白，比直接丢失信息
    /// 好）。</summary>
    public string? EmotionDisplay => Emotion switch
    {
        "NEUTRAL" => "平静",
        "HAPPY" => "开心",
        "SAD" => "难过",
        "ANGRY" => "生气",
        "FEARFUL" => "害怕",
        "DISGUSTED" => "厌恶",
        "SURPRISED" => "惊讶",
        "EMO_UNKNOWN" => "情绪未知",
        null => null,
        _ => Emotion,
    };

    /// <summary>场景/事件的中文展示文本，取值集合对应 Python 端
    /// KNOWN_EVENTS 常量。"Speech"（正常说话）特意翻译成 null 隐藏掉——
    /// 截图里看到几乎每一条结果的 Event 都是 "Speech"，这是最常见的
    /// 默认状态，每条都挂一个"说话中"标签没有任何信息量，只有真正出现
    /// 背景音乐/掌声/笑声这些"值得注意"的场景时才需要显示提醒用户，
    /// 这样能看到标签的时候才是真正有用的信息。</summary>
    public string? EventDisplay => Event switch
    {
        "Speech" => null,
        "BGM" => "背景音乐",
        "Applause" => "掌声",
        "Laughter" => "笑声",
        "Cry" => "哭声",
        "Sneeze" => "喷嚏声",
        "Breath" => "呼吸声",
        "Cough" => "咳嗽声",
        null => null,
        _ => Event,
    };
}