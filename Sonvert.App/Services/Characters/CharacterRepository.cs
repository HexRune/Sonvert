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
        int characterId, string emotion, string language, byte[] audioData, string promptText)
    {
        await using var db = new AppDbContext();

        // 文件路径按语言分子目录存放（Characters/{id}/{language}/{emotion}.wav），
        // 不是像以前那样直接摊平在角色目录下——同一种情绪现在中英文各有
        // 一份，不分子目录会互相覆盖（比如中英文 NEUTRAL 都想叫
        // NEUTRAL.wav）。老数据（迁移时统一标成 "zh"）的文件本身还留在
        // 原来的位置，数据库里存的相对路径没有变过，不受这次改动影响，
        // 只有新录制/覆盖的文件才会用这个新的子目录结构。
        var characterDir = Path.Combine(AppDbContext.CharactersAudioRoot, characterId.ToString(), language);
        Directory.CreateDirectory(characterDir);

        var relativePath = Path.Combine("Characters", characterId.ToString(), language, $"{emotion}.wav");
        var absolutePath = Path.Combine(AppDbContext.AppDataRoot, relativePath);
        await File.WriteAllBytesAsync(absolutePath, audioData);

        var existing = await db.CharacterEmotionClips.FirstOrDefaultAsync(clip =>
            clip.CharacterId == characterId && clip.Emotion == emotion && clip.Language == language);

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
                Language = language,
                RelativeAudioPath = relativePath,
                PromptText = promptText,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task<ResolvedTtsClip?> ResolveClipAsync(int characterId, string emotion, string targetLanguage)
    {
        await using var db = new AppDbContext();

        // 一次性把这个角色的所有参考音频都拿出来，两层兜底逻辑都在内存里
        // 算——单个角色的参考音频最多十几条（7 种情绪 x 2 种语言），
        // 这点数据量没必要为了"选语言"和"选情绪"这两步分别打两次数据库。
        var clips = await db.CharacterEmotionClips
            .Where(c => c.CharacterId == characterId)
            .ToListAsync();

        // 第一层：选语言。目标语言对应的 NEUTRAL 存在就用目标语言；
        // 不存在就整体改用这个角色唯一支持的那种语言（两种 NEUTRAL 都
        // 存在的角色，目标语言一定会在下面直接命中，不会走到"整体改用
        // 另一种语言"这个分支——因为项目目前只支持 zh/en 这两种语言，
        // 只要两种都有，目标语言必然是其中之一）。
        var supportedLanguages = clips
            .Where(c => c.Emotion == "NEUTRAL")
            .Select(c => c.Language)
            .Distinct()
            .ToList();

        if (supportedLanguages.Count == 0) return null; // 两种语言的 NEUTRAL 都没有，角色还不能用

        var effectiveLanguage = supportedLanguages.Contains(targetLanguage)
            ? targetLanguage
            : supportedLanguages[0];

        // 第二层：选情绪。这个语言下有目标情绪就用，没有就退回这个语言的
        // NEUTRAL（上面已经保证了 effectiveLanguage 这个语言的 NEUTRAL
        // 一定存在，这里不会再退到 null）。
        var clip = clips.FirstOrDefault(c => c.Emotion == emotion && c.Language == effectiveLanguage)
                   ?? clips.First(c => c.Emotion == "NEUTRAL" && c.Language == effectiveLanguage);

        return new ResolvedTtsClip
        {
            AudioPath = Path.Combine(AppDbContext.AppDataRoot, clip.RelativeAudioPath),
            PromptText = clip.PromptText,
            Language = clip.Language,
        };
    }
}