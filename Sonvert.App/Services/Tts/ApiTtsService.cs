using System;
using System.Threading.Tasks;
using Sonvert.App.Settings;

namespace Sonvert.App.Services.Tts;

public class ApiTtsService : ITtsService
{
    private readonly ISettingsService _settingsService;

    public ApiTtsService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task StartAsync() => Task.CompletedTask;

    public Task<TtsResult> SynthesizeAsync(string text, string language, string emotion)
    {
        throw new NotImplementedException(
            "第三方 TTS API 尚未接入。请在设置里把 TTSProvider 改回 \"local\"。");
    }
    public Task PrewarmReferenceAudioAsync(int characterId) => Task.CompletedTask; // 远程 API 场景不需要这个优化
    public Task StopAsync() => Task.CompletedTask;
}