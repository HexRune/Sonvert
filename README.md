# Sonvert

实时同声传译（语音转语音翻译）应用，中英双向。

## 功能

- 实时语音识别 + 翻译（中↔英）
- 双语字幕 + 合成语音同步输出
- 情绪识别（标注说话人语气：中性/愤怒/开心等）
- 音色克隆（规划中）
- 弹幕/聊天文本翻译
- 历史记录（原文/译文/音频，可配置自动清理）

## 架构

```
Sonvert.App/              C# 主程序（Avalonia UI）
Sonvert.SenseVoiceService/ Python + onnxruntime，HTTP API，负责语音识别与情绪识别
```

- **C# 端**：负责 UI、音频采集、VAD 切分、机器翻译（ONNX Runtime）、TTS、历史记录（SQLite）。
- **Python 服务端**：加载 SenseVoice 模型，对外提供本地 HTTP API（识别文本 + 语言 + 情绪 + 事件），随主程序按需启动/关闭，详见 `Sonvert.SenseVoiceService/README.md`。

两者通过本地回环 HTTP 通信，端口可在设置界面中修改。

## 环境要求

- Windows，Visual Studio（含 .NET 桌面开发 + Python 开发 工作负载）
- Python 3.10/3.11（不建议 3.12+，部分依赖版本兼容性存在问题）
- 目标硬件：CPU-only 直播机 或 NVIDIA GPU 设备均需支持

## 模型文件

模型文件（`.onnx`）体积较大，不随仓库分发，需自行下载后放入对应 `models/` 目录，具体路径和下载地址见各子项目 README。

## 开发状态

个人开发中，无固定 MVP 截止日期，优先保证质量。
