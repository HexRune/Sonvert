"""
测试脚本：读取任意格式的 wav 文件（用 soundfile，不用手动猜文件头偏移量），
转成服务要求的 PCM16LE/16kHz/单声道，发给 /recognize 接口。

用法：
    python scripts/test_recognize.py <wav文件路径> [language] [use_itn] [--vad]

加 --vad 参数时，会先用 sensevoice 包自带的 FSMNVad 切出有效语音段，
只把切好的片段发给 /recognize —— 用来模拟真实场景下 C# 端先做 VAD
再调用服务的流程，排查"整段原始音频 vs VAD切分后音频"这个变量。

示例：
    python scripts/test_recognize.py D:\code\Sonvert\test1.wav zh true --vad
"""
import sys

import numpy as np
import requests
import soundfile as sf

BASE_URL = "http://127.0.0.1:8878"


def wav_to_pcm16_16k_mono(wav_path: str) -> bytes:
    # soundfile 会正确处理各种位深/声道数/文件头格式，不管是 16bit 还是 32bit
    waveform, sample_rate = sf.read(wav_path, dtype="float32")

    if waveform.ndim > 1:
        # 多声道取平均，转单声道
        waveform = waveform.mean(axis=1)

    if sample_rate != 16000:
        raise ValueError(
            f"采样率是 {sample_rate}Hz，需要先转成 16000Hz "
            f"（用 ffmpeg -ar 16000 转换，这个脚本暂不做重采样）"
        )

    # float32 [-1, 1] -> int16 PCM，四舍五入而不是直接截断（astype 对浮点数
    # 做的是向零截断，会引入系统性的量化偏差，对着标准做法改成 round）
    pcm16 = np.round(waveform * 32767.0).clip(-32768, 32767).astype(np.int16)
    return pcm16.tobytes()


def vad_trim(wav_path: str, resource_dir: str = "models") -> bytes:
    """用 sensevoice 包自带的 FSMNVad 切出有效语音段，模拟 C# 端会做的事。
    resource_dir 需要包含 VAD 相关文件（fsmn-config.yaml 等）——注意这些文件
    我们平时的 models/ 目录里没有（生产环境不需要 VAD），这里主要是为了
    诊断测试，可以传 env\\Lib\\site-packages\\sensevoice\\resource 这种已经
    有完整文件的目录。"""
    from sensevoice.utils.fsmn_vad import FSMNVad

    vad = FSMNVad(resource_dir)
    segments = vad.segments_offline(wav_path)
    if not segments:
        raise RuntimeError("VAD 没有切出任何有效语音段，检查音频是不是全程静音")

    waveform, sample_rate = sf.read(wav_path, dtype="float32")
    if waveform.ndim > 1:
        waveform = waveform.mean(axis=1)

    # 只取第一段做测试；segments 的时间单位是毫秒，*16 是因为 16kHz = 16 samples/ms
    start, end = segments[0]
    print(f"VAD 切出片段: [{start / 1000:.2f}s - {end / 1000:.2f}s]（共 {len(segments)} 段，只测第一段）")
    trimmed = waveform[start * 16: end * 16]

    pcm16 = (trimmed * 32768.0).clip(-32768, 32767).astype(np.int16)
    return pcm16.tobytes()


def main():
    if len(sys.argv) < 2:
        print("用法: python test_recognize.py <wav文件路径> [language=auto] [use_itn=true] [--vad]")
        sys.exit(1)

    use_vad = "--vad" in sys.argv
    args = [a for a in sys.argv[1:] if a != "--vad"]

    wav_path = args[0]
    language = args[1] if len(args) > 1 else "auto"
    use_itn = args[2] if len(args) > 2 else "true"

    if use_vad:
        # 诊断用：VAD 需要的文件目前只在 site-packages 里是齐全的
        pcm_bytes = vad_trim(wav_path, resource_dir=r"env\Lib\site-packages\sensevoice\resource")
    else:
        pcm_bytes = wav_to_pcm16_16k_mono(wav_path)
    print(f"PCM 字节数: {len(pcm_bytes)}（约 {len(pcm_bytes) / 2 / 16000:.2f} 秒）")

    resp = requests.post(
        f"{BASE_URL}/recognize",
        params={"language": language, "use_itn": use_itn},
        data=pcm_bytes,
        headers={"Content-Type": "application/octet-stream"},
    )

    print(f"HTTP {resp.status_code}")
    print(resp.json())


if __name__ == "__main__":
    main()