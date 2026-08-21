using Sonvert.App.Settings;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// ITranslationService 的第三方 API 实现——目前只是占位，接口先留好，
/// 等确定要接入哪个 API（大模型 API / 专门的翻译 API）时再实现
/// TranslateAsync 里的实际 HTTP 调用逻辑。AppSettings 里
/// TranslationApiEndpoint/Key/Model 三个字段已经预留，届时直接读取即可，
/// 不需要再改设置结构或迁移用户已有配置。
/// </summary>
public class ApiTranslationService : ITranslationService
{
    private readonly ISettingsService _settingsService;

    public ApiTranslationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public Task StartAsync() => Task.CompletedTask; // 调远程 API，不需要拉子进程

    public Task<TranslationResult> TranslateAsync(
        string text, string sourceLanguage, string targetLanguage)
    {
        throw new NotImplementedException(
            "第三方翻译 API 尚未接入。请在设置里把 TranslationProvider 改回 \"local\"，" +
            "或者等这部分实现完成后再启用 \"api\"。");
    }

    public Task LoadModelAsync() => Task.CompletedTask; // 远程 API 不需要本地预加载

    public Task StopAsync() => Task.CompletedTask;
}