using System;

namespace Sonvert.App.Services.Audio;

/// <summary>
/// 从 IAudioInputSource.DataAvailable 拿到的一批 float 采样里算出"这批
/// 声音有多响"，给音量条/波形图这类可视化用。项目里有两处需要它：
/// 首页麦克风旁边的小电平指示、顶部横幅栏运行中的滚动波形——两处都是
/// 从同一种数据（归一化到 [-1,1] 的单声道采样）算响度，逻辑完全一样，
/// 所以抽成一个静态工具方法，不在两个地方各写一遍。
/// </summary>
public static class AudioLevelCalculator
{
    /// <summary>算这批采样的 RMS（均方根），比直接取最大值(峰值)更接近
    /// 人耳感知的"平均响度"，波形看起来更平滑、不会因为一两个瞬间尖峰
    /// 就跳到满格。</summary>
    private static double CalculateRms(float[] samples)
    {
        if (samples.Length == 0) return 0.0;

        double sumOfSquares = 0.0;
        foreach (var sample in samples)
        {
            sumOfSquares += sample * sample;
        }
        return Math.Sqrt(sumOfSquares / samples.Length);
    }

    /// <summary>把一批采样转换成 [0, 1] 区间的"电平"，给 UI 直接拿去当
    /// 高度/进度百分比用。
    ///
    /// 这里没有直接返回线性 RMS 值，而是先转成 dBFS 再映射到 [0,1]——
    /// 原因是人耳感知响度是对数关系，正常说话音量的线性 RMS 值其实很小
    /// （可能只有 0.05~0.1），如果直接拿线性值去映射进度条高度，平时
    /// 说话看起来"条基本不怎么动"，很不直观。转成 dB 后用
    /// [MinDb, 0]（比如 [-50dB, 0dB]）这个区间做线性映射，正常说话的
    /// 响度变化在这个区间里能占到大半个量程，条形动起来才有意义，
    /// 这也是真实音频软件的 VU 表/电平表普遍采用的做法。</summary>
    public static double CalculateLevel(float[] samples, double minDb = -50.0)
    {
        var rms = CalculateRms(samples);
        if (rms <= 0.0) return 0.0;

        // 20*log10(rms) 是标准的"线性幅度转 dBFS"公式，0dB 对应满幅度 1.0。
        var db = 20.0 * Math.Log10(rms);

        // 把 [minDb, 0] 线性映射到 [0, 1]，低于 minDb 的部分（几乎无声）
        // 直接钳到 0，避免出现负值。
        var level = (db - minDb) / (0.0 - minDb);
        return Math.Clamp(level, 0.0, 1.0);
    }
}
