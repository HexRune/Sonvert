using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sonvert.App.Data;
using Sonvert.App.Models;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// 每个方法都自己 new 一个 AppDbContext——理由跟 CharacterRepository 一样：
/// DbContext 不是拿来长期持有、跨多次操作复用的，每次操作开一个新的、
/// 用完即扔，是 EF Core 官方推荐的用法，项目里所有仓储类都是这个模式，
/// 这里跟着保持一致，不用 IDbContextFactory 这类需要额外注册 DI 的方式。
/// </summary>
public class GlossaryRepository : IGlossaryRepository
{
    public async Task<List<GlossaryEntry>> GetEntriesForDictionaryAsync(int? dictionaryId)
    {
        if (dictionaryId is null) return new List<GlossaryEntry>();

        await using var db = new AppDbContext();
        return await db.GlossaryEntries
            .Where(e => e.DictionaryId == dictionaryId)
            .ToListAsync();
    }

    public async Task<List<GlossaryDictionary>> GetDictionariesAsync()
    {
        await using var db = new AppDbContext();
        return await db.GlossaryDictionaries
            .Include(d => d.Entries)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<GlossaryDictionary> AddDictionaryAsync(string name)
    {
        await using var db = new AppDbContext();
        var dictionary = new GlossaryDictionary { Name = name };
        db.GlossaryDictionaries.Add(dictionary);
        await db.SaveChangesAsync();
        return dictionary;
    }

    public async Task DeleteDictionaryAsync(int dictionaryId)
    {
        await using var db = new AppDbContext();
        var dictionary = await db.GlossaryDictionaries.FindAsync(dictionaryId);
        if (dictionary is null) return;

        // 不用手动先删 Entries 再删 Dictionary——AppDbContext.OnModelCreating
        // 里已经把这对关系配置成级联删除，EF Core 发出的 DELETE 语句会
        // 让数据库自己处理子表，这里删父表这一条记录就够了。
        db.GlossaryDictionaries.Remove(dictionary);
        await db.SaveChangesAsync();
    }

    public async Task AddEntryAsync(int dictionaryId, string sourceTerm, string targetTerm)
    {
        await using var db = new AppDbContext();
        db.GlossaryEntries.Add(new GlossaryEntry
        {
            DictionaryId = dictionaryId,
            SourceTerm = sourceTerm,
            TargetTerm = targetTerm,
        });
        await db.SaveChangesAsync();
    }

    public async Task DeleteEntryAsync(int entryId)
    {
        await using var db = new AppDbContext();
        var entry = await db.GlossaryEntries.FindAsync(entryId);
        if (entry is null) return;

        db.GlossaryEntries.Remove(entry);
        await db.SaveChangesAsync();
    }

    public async Task<GlossaryDictionary> ImportFromJsonAsync(string json, string? nameOverride)
    {
        // 反序列化失败（格式不对、不是合法 JSON）会直接抛
        // JsonException，调用方（GlossaryViewModel）负责捕获并转成用户
        // 看得懂的提示——这一层不吞异常、不返回 null 假装成功，导入
        // 到底成没成功、失败的原因是什么，都应该如实往上传。
        var file = JsonSerializer.Deserialize<GlossaryImportFile>(json)
            ?? throw new InvalidOperationException("JSON 内容为空或格式不正确");

        var name = !string.IsNullOrWhiteSpace(nameOverride) ? nameOverride!
            : !string.IsNullOrWhiteSpace(file.Name) ? file.Name!
            : throw new InvalidOperationException("没有指定词典名称——请在界面上填写，或者在 JSON 文件里加上 \"name\" 字段");

        // 过滤掉原文/译文任一为空的词条，而不是让它们混进数据库——
        // 一条 source 或 target 是空字符串的"术语"，在 GlossaryReplacer
        // 里要么匹配不到任何东西、要么会把原文里所有位置替换成空，
        // 都不是用户想要的效果，与其让它悄悄产生怪异结果，不如在导入
        // 这一步就跳过。
        var validEntries = file.Entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Target))
            .ToList();

        await using var db = new AppDbContext();
        var dictionary = new GlossaryDictionary { Name = name };
        db.GlossaryDictionaries.Add(dictionary);

        foreach (var entry in validEntries)
        {
            db.GlossaryEntries.Add(new GlossaryEntry
            {
                Dictionary = dictionary,
                SourceTerm = entry.Source,
                TargetTerm = entry.Target,
            });
        }

        await db.SaveChangesAsync();
        return dictionary;
    }
}
