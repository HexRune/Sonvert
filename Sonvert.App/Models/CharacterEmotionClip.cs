using System;

namespace Sonvert.App.Models;

public class CharacterEmotionClip
{
    public int Id { get; set; }

    public int CharacterId { get; set; }
    public Character Character { get; set; } = null!;

    /// <summary>NEUTRAL/HAPPY/ANGRY/SURPRISED 等，跟 SenseVoice 输出的标签完全一致。</summary>
    public required string Emotion { get; set; }

    /// <summary>这条参考音频是哪种语言录的（"zh"/"en"）。每个角色的每种
    /// 情绪现在可以有中/英两条独立的参考音频——同一个人用中文和用英文
    /// 说话时的音色/语调本来就有差异，分开录制、分开使用效果更好。
    /// 中文 NEUTRAL 和英文 NEUTRAL 至少要有一个存在，角色才能真正用于
    /// 合成（见 ICharacterRepository.ResolveClipAsync 的兜底逻辑）。</summary>
    public required string Language { get; set; }

    /// <summary>相对路径，比如 "Characters/3/NEUTRAL.wav"——相对于 AppData\Sonvert 这个根目录，
    /// 不存绝对路径，整个 AppData 目录搬家/换电脑时不会失效。</summary>
    public required string RelativeAudioPath { get; set; }

    /// <summary>参考音频里实际说的内容，逐字对应——通过录音向导录制时，
    /// 默认预填内置固定脚本的文字，用户可以在保存前修改；如果是导入
    /// 已有音频，这个字段需要用户自己手动填写。</summary>
    public required string PromptText { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}