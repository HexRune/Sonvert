using System.Threading.Tasks;

namespace Sonvert.App.Services.Translation;

/// <summary>
/// 翻译服务。ViewModel 只依赖这个接口，不关心背后是本地模型子进程
/// 还是第三方 API——两种实现由 TranslationRouter 按设置选择。
/// </summary>
public interface ITranslationService
{
    /// <summary>准备就绪（本地实现：拉起子进程；API 实现：目前是空操作）。</summary>
    Task StartAsync();

    /// <summary>翻译一段文本。sourceLanguage/targetLanguage 用 "zh"/"en" 这类简写。</summary>
    Task<TranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage);

    /// <summary>释放资源（本地实现：关闭子进程；API 实现：目前是空操作）。</summary>
    Task StopAsync();

    /// <summary>提前把翻译模型加载进内存，避免第一句翻译时才现加载导致明显卡顿。
    /// API 实现可以是空操作（不需要本地加载）。</summary>
    Task LoadModelAsync();
}