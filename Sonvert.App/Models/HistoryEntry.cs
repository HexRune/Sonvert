using System;

namespace Sonvert.App.Models;

/// <summary>
/// 一条历史记录对应一句话的完整生命周期：识别到的原文/情绪/场景在识别完成的
/// 那一刻就确定了，一定会有值；TranslatedText/TranslatedAudioRelativePath
/// 只有在"这句话真的经过了翻译"时才会有值——如果识别语言正好就是目标语言，
/// 不需要翻译，这两个字段留 null，界面上据此判断"这句话是直接播报原文的"，
/// 不是漏翻了。CharacterId/TargetLanguage 是"记录当时用的是哪个设置"，
/// 纯粹为了以后方便按角色/语言筛选，不影响任何业务逻辑。
/// </summary>
public class HistoryEntry
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }

    public required string SourceText { get; set; }
    public string? TranslatedText { get; set; }

    public string? Emotion { get; set; }
    public string? Event { get; set; }

    public int? CharacterId { get; set; }
    public string? TargetLanguage { get; set; }

    /// <summary>相对路径（相对 AppDbContext.AppDataRoot），指向识别到的
    /// 原始语音——这份音频不是我们自己录的，是 SenseVoice 识别这句话时
    /// 顺带传回来的那段波形（RecognitionResultEventArgs.AudioSamples），
    /// 之前一直没被存下来，这次顺手落盘。</summary>
    public required string SourceAudioRelativePath { get; set; }

    /// <summary>相对路径，指向 GPT-SoVITS 合成出的语音——只有翻译+合成
    /// 都成功时才会有值。</summary>
    public string? TranslatedAudioRelativePath { get; set; }
}