using System;

namespace Sonvert.App.Models;

public class CharacterEmotionClip
{
    public int Id { get; set; }

    public int CharacterId { get; set; }
    public Character Character { get; set; } = null!;

    /// <summary>NEUTRAL/HAPPY/ANGRY/SURPRISED 等，跟 SenseVoice 输出的标签完全一致。</summary>
    public required string Emotion { get; set; }

    /// <summary>相对路径，比如 "Characters/3/NEUTRAL.wav"——相对于 AppData\Sonvert 这个根目录，
    /// 不存绝对路径，整个 AppData 目录搬家/换电脑时不会失效。</summary>
    public required string RelativeAudioPath { get; set; }

    /// <summary>参考音频里实际说的内容，逐字对应——通过录音向导录制时，
    /// 默认预填内置固定脚本的文字，用户可以在保存前修改；如果是导入
    /// 已有音频，这个字段需要用户自己手动填写。</summary>
    public required string PromptText { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}