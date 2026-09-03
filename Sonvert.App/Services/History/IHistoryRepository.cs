using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sonvert.App.Models;

namespace Sonvert.App.Services.History;

public interface IHistoryRepository
{
    Task<List<HistoryEntry>> GetAllAsync();
    Task<List<HistoryEntry>> GetByDateAsync(DateOnly date);

    /// <summary>拿到所有有记录的日期，按新到旧排序——用来填历史记录页面
    /// 那个"按日期筛选"的下拉框选项。</summary>
    Task<List<DateOnly>> GetDistinctDatesAsync();

    /// <summary>写入一条历史记录：负责把两段音频落盘（translatedAudioWav
    /// 传 null 表示这句话没有合成出语音，比如翻译/合成失败，或者根本
    /// 不需要翻译）、生成不重名的文件名、再把元数据写进数据库。
    /// 三个延迟参数各自可空——含义见 HistoryEntry 里对应字段的注释。</summary>
    Task AddAsync(
        DateTime timestamp,
        string sourceText,
        string? translatedText,
        string? emotion,
        string? eventTag,
        int? characterId,
        string? targetLanguage,
        byte[] sourceAudioWav,
        byte[]? translatedAudioWav,
        int? asrLatencyMs,
        int? translationLatencyMs,
        int? ttsLatencyMs);

    /// <summary>删除单条记录——连同它指向的一或两个音频文件一起删。</summary>
    Task DeleteEntryAsync(int id);

    /// <summary>删除某一天的全部记录——直接整个删掉那一天的文件夹
    /// （比逐条删文件再删数据库行更高效），再批量清掉数据库里
    /// 落在这一天的所有行。</summary>
    Task DeleteDateAsync(DateOnly date);
}