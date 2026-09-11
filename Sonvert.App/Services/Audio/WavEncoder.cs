using System.IO;
using NAudio.Wave;

namespace Sonvert.App.Services.Audio;

/// <summary>
/// SenseVoice 识别一句话时，顺带把这句话对应的原始波形以
/// float[]（IEEE 浮点采样）的形式传回来（RecognitionResultEventArgs.
/// AudioSamples），这不是标准的 wav 文件格式，只是裸的采样数据——
/// 历史记录要把它存成一个能被任意播放器打开的 .wav 文件，这个类
/// 就是做这个转换的，跟"录音/合成"两条业务逻辑都没关系，纯粹是个
/// 格式转换工具。
/// </summary>
public static class WavEncoder
{
    public static byte[] EncodeFloatSamplesToWav(float[] samples, int sampleRate)
    {
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(
            stream, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels: 1)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }
        return stream.ToArray();
    }

    /// <summary>把裸 PCM 字节（没有任何文件头，比如流式 TTS 收到的、已经
    /// 剥离了容器格式头的音频数据）包成一个能被任意播放器/NAudio 直接
    /// 打开的合法 wav 文件。用于流式合成路径：边收边播的同时也要把完整
    /// 音频存进历史记录，历史记录要求是能独立播放的文件，不能是裸 PCM。</summary>
    public static byte[] EncodePcmBytesToWav(byte[] pcmBytes, int sampleRate, int channels, int bitsPerSample)
    {
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(stream, new WaveFormat(sampleRate, bitsPerSample, channels)))
        {
            writer.Write(pcmBytes, 0, pcmBytes.Length);
        }
        return stream.ToArray();
    }
}