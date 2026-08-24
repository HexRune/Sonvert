using System.Threading.Tasks;

namespace Sonvert.App.Services.Tts;

/// <summary>
/// 语音合成服务。ViewModel 只依赖这个接口，本地 GPT-SoVITS 还是未来的
/// 第三方 TTS API 由 TtsRouter 按设置选择，跟 ITranslationService 设计对称。
/// </summary>
public interface ITtsService
{
    Task StartAsync();

    /// <summary>预热：把指定角色已经录制过的所有情绪参考音频，提前提交给
    /// GPT-SoVITS 做一次特征提取（调用它的 /set_refer_audio 接口）。
    /// 这一步能不能真正加速后续合成还没有 100% 确认（GPT-SoVITS 官方
    /// 没有明确文档说明 /tts 请求会不会复用这次预热的结果），实现这个方法
    /// 是为了实测验证——调用方应该在开始识别前调一次，然后对比第一次真实
    /// 合成的耗时跟之前的数据，看是否有实质性改善。</summary>
    Task PrewarmReferenceAudioAsync(int characterId);

    Task<TtsResult> SynthesizeAsync(string text, string language, string emotion);

    Task StopAsync();
}