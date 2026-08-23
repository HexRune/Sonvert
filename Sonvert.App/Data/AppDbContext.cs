using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Sonvert.App.Models;

namespace Sonvert.App.Data;

/// <summary>
/// 整个应用唯一的数据落盘方式：SQLite 数据库存结构化的元数据（文字、
/// 时间戳、各种关联 Id），大文件（音频）单独存在磁盘文件夹里，数据库
/// 只存指向这些文件的相对路径——这是"角色"和"历史记录"两块功能共用的
/// 存储模式，不是各自发明了一套。选相对路径而不是绝对路径，是为了让
/// 整个 AppData\Sonvert 目录可以被用户整体搬家/备份而不失效。
///
/// 当前用 EnsureCreated() 建表（见 App.axaml.cs），没有引入正式的
/// EF Core Migrations——这意味着"新增一张表"没问题（这次的 HistoryEntries
/// 就是新增的），但"修改一张已经建过的表的结构"不会被 EnsureCreated
/// 自动应用到已存在的数据库文件上。现在项目还在快速迭代阶段，这个限制
/// 先记在这里，等表结构不再频繁变动时应该换成正式的 Migrations。
/// </summary>
public class AppDbContext : DbContext
{
    public DbSet<Character> Characters => Set<Character>();
    public DbSet<CharacterEmotionClip> CharacterEmotionClips => Set<CharacterEmotionClip>();
    public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();

    public static string AppDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Sonvert");

    public static string CharactersAudioRoot { get; } = Path.Combine(AppDataRoot, "Characters");

    /// <summary>历史记录音频的根目录，下面按日期分文件夹存放
    /// （History/2026-08-21/、History/2026-08-22/ ...）。</summary>
    public static string HistoryRoot { get; } = Path.Combine(AppDataRoot, "History");

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        Directory.CreateDirectory(AppDataRoot);
        var dbPath = Path.Combine(AppDataRoot, "sonvert.db");
        options.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Character>()
            .HasMany(c => c.EmotionClips)
            .WithOne(clip => clip.Character)
            .HasForeignKey(clip => clip.CharacterId)
            .OnDelete(DeleteBehavior.Cascade);

        // 历史记录经常按"某一天"这个条件查询/删除，给 Timestamp 加个索引，
        // 数据量大了之后这类按时间范围过滤的查询不会随记录数线性变慢。
        modelBuilder.Entity<HistoryEntry>()
            .HasIndex(e => e.Timestamp);
    }
}