using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sonvert.App.Data;
using Sonvert.App.Models;

namespace Sonvert.App.Services.Characters;

/// <summary>
/// 每个方法都自己 new 一个 AppDbContext——DbContext 设计上就不是拿来长期
/// 持有、跨多次操作复用的（它内部有状态跟踪，长期存活容易导致数据不同步/
/// 内存增长），每次操作开一个新的、用完即扔，是 EF Core 官方推荐的用法。
/// </summary>
public class CharacterRepository : ICharacterRepository
{
    public async Task<List<Character>> GetAllAsync()
    {
        await using var db = new AppDbContext();
        return await db.Characters.Include(c => c.EmotionClips).ToListAsync();
    }

    public async Task<Character?> GetByIdAsync(int id)
    {
        await using var db = new AppDbContext();
        return await db.Characters.Include(c => c.EmotionClips)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Character> CreateAsync(string name)
    {
        await using var db = new AppDbContext();
        var character = new Character { Name = name };
        db.Characters.Add(character);
        await db.SaveChangesAsync();
        return character;
    }

    public async Task DeleteAsync(int id)
    {
        await using var db = new AppDbContext();
        var character = await db.Characters.Include(c => c.EmotionClips)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (character is null) return;

        var characterDir = Path.Combine(AppDbContext.CharactersAudioRoot, id.ToString());
        if (Directory.Exists(characterDir))
        {
            Directory.Delete(characterDir, recursive: true);
        }

        db.Characters.Remove(character); // 级联删除 EmotionClips 记录
        await db.SaveChangesAsync();
    }

    public async Task SaveEmotionClipAsync(
        int characterId, string emotion, byte[] audioData, string promptText)
    {
        await using var db = new AppDbContext();

        var characterDir = Path.Combine(AppDbContext.CharactersAudioRoot, characterId.ToString());
        Directory.CreateDirectory(characterDir);

        var relativePath = Path.Combine("Characters", characterId.ToString(), $"{emotion}.wav");
        var absolutePath = Path.Combine(AppDbContext.AppDataRoot, relativePath);
        await File.WriteAllBytesAsync(absolutePath, audioData);

        var existing = await db.CharacterEmotionClips
            .FirstOrDefaultAsync(clip => clip.CharacterId == characterId && clip.Emotion == emotion);

        if (existing is not null)
        {
            existing.RelativeAudioPath = relativePath;
            existing.PromptText = promptText;
            existing.RecordedAt = DateTime.UtcNow;
        }
        else
        {
            db.CharacterEmotionClips.Add(new CharacterEmotionClip
            {
                CharacterId = characterId,
                Emotion = emotion,
                RelativeAudioPath = relativePath,
                PromptText = promptText,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<ResolvedTtsClip?> ResolveClipAsync(int characterId, string emotion)
    {
        await using var db = new AppDbContext();

        var clip = await db.CharacterEmotionClips
            .FirstOrDefaultAsync(c => c.CharacterId == characterId && c.Emotion == emotion);

        clip ??= await db.CharacterEmotionClips
            .FirstOrDefaultAsync(c => c.CharacterId == characterId && c.Emotion == "NEUTRAL");

        if (clip is null) return null;

        return new ResolvedTtsClip
        {
            AudioPath = Path.Combine(AppDbContext.AppDataRoot, clip.RelativeAudioPath),
            PromptText = clip.PromptText,
        };
    }
}