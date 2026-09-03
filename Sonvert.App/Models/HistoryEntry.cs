using System;
using System.Collections.Generic;
using System.Linq;

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

    // ---- 延迟统计（毫秒）----
    // 拆成三段分别记录而不是只存一个总数，是为了以后能对比"不同模型/
    // 服务商在真实使用中各自的延迟表现"（比如本地 OPUS-MT vs DeepSeek
    // vs Azure 翻译，谁的翻译这一段更快；本地 GPT-SoVITS vs Azure 语音
    // 合成，谁的合成这一段更快）——只存一个总数的话，没法看出延迟差异
    // 具体是哪一段贡献的。三个字段都是可空的：
    // - AsrLatencyMs：从这句话的音频交给 SenseVoice 识别，到识别结果
    //   返回，这一段几乎总是有值（识别失败那句话本身就不会生成记录）。
    // - TranslationLatencyMs：null 表示这句话没有经过翻译（识别语言正好
    //   就是目标语言），不是翻译"耗时 0ms"，是压根没跑这一步。
    // - TtsLatencyMs：null 表示没有语音合成这一步（TTS 播放被关闭，或者
    //   前面翻译失败/语言不支持导致根本没走到合成这一步）。
    // 三段只要发生了对应的调用，不管调用最终成功还是失败/抛异常，都会
    // 记录耗时（失败也是耗时的一部分，比如"这次翻译调用卡了 5 秒才
    // 超时失败"这种信息本身就有诊断价值，不应该因为失败就丢弃这段耗时）。
    public int? AsrLatencyMs { get; set; }
    public int? TranslationLatencyMs { get; set; }
    public int? TtsLatencyMs { get; set; }

    /// <summary>三段延迟里实际发生过的加起来，用于界面上显示"总延迟"。
    /// 不单独存一个数据库字段——总数完全由这三段推算得出，存一份冗余
    /// 数据没有必要，还多一个"改了某一段却忘了同步改总数"的风险。</summary>
    public int? TotalLatencyMs
    {
        get
        {
            var values = new[] { AsrLatencyMs, TranslationLatencyMs, TtsLatencyMs }
                .Where(v => v.HasValue)
                .Select(v => v!.Value)
                .ToList();
            return values.Count == 0 ? null : values.Sum();
        }
    }

    /// <summary>三段延迟各自的展示文字拼在一起，比如"识别 210ms ·
    /// 翻译 850ms · 合成 620ms"——只列出真正发生过的那几段（某段是 null
    /// 就跳过，不显示"翻译 0ms"这种误导性的假数据）。这次改动之前的
    /// 老记录三段全是 null，这个属性会返回空字符串，界面上绑 IsVisible
    /// 到"这个字符串是否非空"就能让老记录自然不显示这一行，不需要额外
    /// 判断"是不是老数据"。</summary>
    public string LatencyBreakdownDisplay
    {
        get
        {
            var segments = new List<string>();
            if (AsrLatencyMs.HasValue) segments.Add($"识别 {AsrLatencyMs}ms");
            if (TranslationLatencyMs.HasValue) segments.Add($"翻译 {TranslationLatencyMs}ms");
            if (TtsLatencyMs.HasValue) segments.Add($"合成 {TtsLatencyMs}ms");
            return string.Join(" · ", segments);
        }
    }

    /// <summary>总延迟的展示文字，比如"总计 1680ms"；三段都没有时返回
    /// 空字符串（老数据），界面上跟 LatencyBreakdownDisplay 一样用
    /// IsVisible 绑这个字符串是否非空。</summary>
    public string TotalLatencyDisplay => TotalLatencyMs.HasValue ? $"总计 {TotalLatencyMs}ms" : "";
}