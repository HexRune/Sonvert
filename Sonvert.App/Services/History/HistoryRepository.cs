using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sonvert.App.Data;
using Sonvert.App.Models;

namespace Sonvert.App.Services.History;

/// <summary>
/// 每个方法自己 new 一个 AppDbContext，用完即扔——跟 CharacterRepository
/// 是同一套约定，DbContext 不适合长期持有跨多次操作复用。
/// </summary>
public class HistoryRepository : IHistoryRepository
{
    public async Task<List<HistoryEntry>> GetAllAsync()
    {
        await using var db = new AppDbContext();
        return await db.HistoryEntries.OrderByDescending(e => e.Timestamp).ToListAsync();
    }

    public async Task<List<HistoryEntry>> GetByDateAsync(DateOnly date)
    {
        await using var db = new AppDbContext();
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        return await db.HistoryEntries
            .Where(e => e.Timestamp >= start && e.Timestamp < end)
            .OrderByDescending(e => e.Timestamp)
            .ToListAsync();
    }

    public async Task<List<DateOnly>> GetDistinctDatesAsync()
    {
        await using var db = new AppDbContext();

        // 在内存里去重而不是让 SQLite 做日期截断再 DISTINCT——EF Core 对
        // SQLite 日期函数的翻译不够可靠，数据量在"一场直播一天的量级"下，
        // 全量拉出来再在内存里处理，性能完全不是问题，换来的是逻辑更简单可靠。
        var timestamps = await db.HistoryEntries.Select(e => e.Timestamp).ToListAsync();

        return timestamps
            .Select(DateOnly.FromDateTime)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();
    }

    public async Task AddAsync(
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
        int? ttsLatencyMs)
    {
        var dateFolderName = timestamp.ToString("yyyy-MM-dd");
        var dayDirectory = Path.Combine(AppDbContext.HistoryRoot, dateFolderName);
        Directory.CreateDirectory(dayDirectory);

        // 文件名 = 时分秒 + 4 位随机十六进制。理论上极小概率会撞名
        // （同一秒内识别到两句话、又刚好抽中同一个随机值），所以生成后
        // 检查一下是否已存在，撞了就换一个，保证绝不会覆盖已有文件。
        string baseName;
        string sourceRelativePath;
        string sourceAbsolutePath;
        do
        {
            var randomSuffix = Guid.NewGuid().ToString("N")[..4];
            baseName = $"{timestamp:HH-mm-ss}_{randomSuffix}";
            sourceRelativePath = Path.Combine("History", dateFolderName, $"{baseName}_source.wav");
            sourceAbsolutePath = Path.Combine(AppDbContext.AppDataRoot, sourceRelativePath);
        } while (File.Exists(sourceAbsolutePath));

        await File.WriteAllBytesAsync(sourceAbsolutePath, sourceAudioWav);

        string? translatedRelativePath = null;
        if (translatedAudioWav is not null)
        {
            translatedRelativePath = Path.Combine("History", dateFolderName, $"{baseName}_translated.wav");
            var translatedAbsolutePath = Path.Combine(AppDbContext.AppDataRoot, translatedRelativePath);
            await File.WriteAllBytesAsync(translatedAbsolutePath, translatedAudioWav);
        }

        await using var db = new AppDbContext();
        db.HistoryEntries.Add(new HistoryEntry
        {
            Timestamp = timestamp,
            SourceText = sourceText,
            TranslatedText = translatedText,
            Emotion = emotion,
            Event = eventTag,
            CharacterId = characterId,
            TargetLanguage = targetLanguage,
            SourceAudioRelativePath = sourceRelativePath,
            TranslatedAudioRelativePath = translatedRelativePath,
            AsrLatencyMs = asrLatencyMs,
            TranslationLatencyMs = translationLatencyMs,
            TtsLatencyMs = ttsLatencyMs,
        });
        await db.SaveChangesAsync();
    }

    public async Task DeleteEntryAsync(int id)
    {
        await using var db = new AppDbContext();
        var entry = await db.HistoryEntries.FindAsync(id);
        if (entry is null) return;

        DeleteFileIfExists(entry.SourceAudioRelativePath);
        if (entry.TranslatedAudioRelativePath is not null)
        {
            DeleteFileIfExists(entry.TranslatedAudioRelativePath);
        }

        db.HistoryEntries.Remove(entry);
        await db.SaveChangesAsync();
    }

    public async Task DeleteDateAsync(DateOnly date)
    {
        var dateFolderName = date.ToString("yyyy-MM-dd");
        var dayDirectory = Path.Combine(AppDbContext.HistoryRoot, dateFolderName);

        // 整个文件夹一次删掉，比"查出这一天所有记录、逐条删各自的文件"
        // 快得多，也更简单——文件夹本来就是按天隔离的，天然适合整体删除。
        if (Directory.Exists(dayDirectory))
        {
            Directory.Delete(dayDirectory, recursive: true);
        }

        await using var db = new AppDbContext();
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var entriesToRemove = db.HistoryEntries.Where(e => e.Timestamp >= start && e.Timestamp < end);
        db.HistoryEntries.RemoveRange(entriesToRemove);
        await db.SaveChangesAsync();
    }

    private static void DeleteFileIfExists(string relativePath)
    {
        var absolutePath = Path.Combine(AppDbContext.AppDataRoot, relativePath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }
    }
}