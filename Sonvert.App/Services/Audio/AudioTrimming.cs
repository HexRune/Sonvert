using System;
using System.IO;
using NAudio.Wave;

namespace Sonvert.App.Services.Audio;

/// <summary>
/// 掐掉录音开头结尾的静音/杂音。用简单的能量阈值检测——不需要 VAD 模型，
/// 这里要解决的是"一段录音本身首尾的死寂"，不是"长音频里切句子"，
/// 复杂度不在一个量级，用不着复用 Sherpa-onnx 那套。
/// </summary>
public static class AudioTrimming
{
    /// <summary>
    /// thresholdRatio：判定为"有声音"的振幅阈值，相对 16bit 最大值
    /// （32768）的比例，默认 2%，参考正常说话音量与麦克风底噪的常见差距，
    /// 有明显环境噪声可以调高一些。
    /// marginMs：在检测到的有效语音范围前后各保留多少毫秒余量，
    /// 避免把说话开头结尾的正常语气切得太死。
    /// </summary>
    public static byte[] TrimSilence(byte[] wavBytes, float thresholdRatio = 0.02f, int marginMs = 100)
    {
        using var inputStream = new MemoryStream(wavBytes);
        using var reader = new WaveFileReader(inputStream);

        var format = reader.WaveFormat;
        var bytesPerSample = format.BitsPerSample / 8;
        var totalSamples = (int)(reader.Length / bytesPerSample);

        var samples = new short[totalSamples];
        var buffer = new byte[reader.Length];
        reader.Read(buffer, 0, buffer.Length);
        Buffer.BlockCopy(buffer, 0, samples, 0, buffer.Length);

        var threshold = (short)(short.MaxValue * thresholdRatio);

        var firstLoudSample = FindFirstAboveThreshold(samples, threshold);
        var lastLoudSample = FindLastAboveThreshold(samples, threshold);

        if (firstLoudSample < 0 || lastLoudSample < 0)
        {
            // 整段都没检测到超过阈值的声音（比如录到的全是静音），
            // 原样返回，不做任何裁剪——总比裁出一个空文件要安全。
            return wavBytes;
        }

        var marginSamples = format.SampleRate * marginMs / 1000;
        var startSample = Math.Max(0, firstLoudSample - marginSamples);
        var endSample = Math.Min(totalSamples - 1, lastLoudSample + marginSamples);

        var trimmedSampleCount = endSample - startSample + 1;
        var trimmedSamples = new short[trimmedSampleCount];
        Array.Copy(samples, startSample, trimmedSamples, 0, trimmedSampleCount);

        using var outputStream = new MemoryStream();
        using (var writer = new WaveFileWriter(outputStream, format))
        {
            var trimmedBytes = new byte[trimmedSampleCount * bytesPerSample];
            Buffer.BlockCopy(trimmedSamples, 0, trimmedBytes, 0, trimmedBytes.Length);
            writer.Write(trimmedBytes, 0, trimmedBytes.Length);
        }

        return outputStream.ToArray();
    }

    private static int FindFirstAboveThreshold(short[] samples, short threshold)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) > threshold) return i;
        }
        return -1;
    }

    private static int FindLastAboveThreshold(short[] samples, short threshold)
    {
        for (var i = samples.Length - 1; i >= 0; i--)
        {
            if (Math.Abs(samples[i]) > threshold) return i;
        }
        return -1;
    }
}