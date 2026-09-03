using System.IO;
using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Characters;

/// <summary>
/// 程序启动时跑一次：如果检测到旧版全局参考音频配置、且还没迁移过，
/// 就创建一个"默认角色"，把配置里的 NEUTRAL 音频文件和对应文本
/// 复制进新的角色数据里。
/// </summary>
public class LegacySettingsMigrator
{
    private readonly ISettingsService _settingsService;
    private readonly ICharacterRepository _characterRepository;

    public LegacySettingsMigrator(
        ISettingsService settingsService, ICharacterRepository characterRepository)
    {
        _settingsService = settingsService;
        _characterRepository = characterRepository;
    }

    public async Task RunAsync()
    {
        var settings = _settingsService.Current;

        if (settings.LegacyTtsReferenceAudioMigrated)
        {
            return;
        }

        if (!settings.TTSReferenceAudioByEmotion.TryGetValue("NEUTRAL", out var neutralClip)
            || string.IsNullOrWhiteSpace(neutralClip.AudioPath)
            || !File.Exists(neutralClip.AudioPath))
        {
            settings.LegacyTtsReferenceAudioMigrated = true;
            await _settingsService.SaveAsync();
            return;
        }

        var defaultCharacter = await _characterRepository.CreateAsync("默认角色");
        var audioBytes = await File.ReadAllBytesAsync(neutralClip.AudioPath);
        // 旧版全局参考音频配置本来就只支持中文（那时候还没有"语言"这个
        // 概念，隐式全部当中文处理），迁移过来时固定标成 "zh"，
        // 跟新增 Language 列时给老数据库记录做的迁移默认值保持一致。
        await _characterRepository.SaveEmotionClipAsync(
            defaultCharacter.Id, "NEUTRAL", "zh", audioBytes, neutralClip.PromptText);

        settings.ActiveCharacterId = defaultCharacter.Id;
        settings.LegacyTtsReferenceAudioMigrated = true;
        await _settingsService.SaveAsync();
    }
}