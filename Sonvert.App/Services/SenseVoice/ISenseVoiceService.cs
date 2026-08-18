using System.Threading.Tasks;

namespace Sonvert.App.Services.SenseVoice;

/// <summary>
/// 语音识别 + 情绪识别服务。底层是拉起一个 Python 子进程、通过本地 HTTP
/// 调用它，但这层细节对外不暴露——调用方（ViewModel）只需要知道这几个方法。
/// </summary>
public interface ISenseVoiceService
{
    /// <summary>
    /// 拉起 Python 子进程，并轮询 /health 直到服务就绪。
    /// 调用后子进程已经在运行，但模型还没加载（还需要调 LoadModelAsync）。
    /// 如果进程已经在运行，直接返回，不会重复拉起。
    /// </summary>
    Task StartAsync();

    /// <summary>加载指定精度的模型。已加载时会先卸载旧的再加载新的。</summary>
    Task LoadModelAsync(ModelPrecision precision);

    /// <summary>卸载模型，子进程不退出，可以再次调用 LoadModelAsync 重新加载。</summary>
    Task UnloadModelAsync();

    /// <summary>
    /// 识别一段已经 VAD 切分好的语音。pcmBytes 要求 PCM16LE / 16kHz / 单声道。
    /// 内部做了串行化处理，即使调用方并发调用多次，也会排队执行，
    /// 不会真的并发打到 Python 那边（模型推理本身不是线程安全的并发设计）。
    /// </summary>
    Task<RecognitionResult> RecognizeAsync(byte[] pcmBytes, string language = "auto", bool useItn = true);

    /// <summary>
    /// 通知子进程正常退出（调 /shutdown），并等待进程实际退出；
    /// 超时未退出则强制 Kill 兜底。程序退出前必须调用这个，
    /// 否则子进程可能变成孤儿进程留在后台。
    /// </summary>
    Task StopAsync();
}
