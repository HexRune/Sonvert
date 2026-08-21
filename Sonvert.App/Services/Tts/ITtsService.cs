using System.Threading.Tasks;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// 语音合成服务。ViewModel 只依赖这个接口，本地 GPT-SoVITS 还是未来的
/// 第三方 TTS API 由 TtsRouter 按设置选择，跟 ITranslationService 设计对称。
/// </summary>
public interface ITtsService
{
    Task StartAsync();

    /// <summary>
    /// 合成一段文本对应的语音。emotion 用于查找对应的参考音频
    /// （NEUTRAL/HAPPY/ANGRY 等，直接传 SenseVoice 识别出的标签即可），
    /// 找不到对应情绪的参考音频时自动退回 NEUTRAL。
    /// </summary>
    Task<TtsResult> SynthesizeAsync(string text, string language, string emotion);

    Task StopAsync();
}