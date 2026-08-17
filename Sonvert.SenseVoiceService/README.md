# Sonvert.SenseVoiceService

Python + onnxruntime 本地 HTTP 服务，负责语音识别（文本）+ 情绪 + 事件识别。
被 C# 主程序（`Sonvert.App`）作为子进程按需拉起/关闭。

## 为什么单独做一个 Python 服务

最初尝试过用 sherpa-onnx 的 C++/C# 绑定直接调用 SenseVoice，但实测发现
sherpa-onnx 自己的 SenseVoice 实现在情绪识别上有 bug（不管音频实际情绪如何，
`emotion` 字段永远输出 `NEUTRAL`）。用同一段测试音频交叉验证过官方 ModelScope
在线 demo、`lovemefan/SenseVoice-python`（`sensevoice-onnx` pip 包）、
`lovemefan/SenseVoice.cpp`（GGML 移植版）之后，确认：

- **官方 demo 和 `sensevoice-onnx`**：结果正确（能识别出 ANGRY 等真实情绪）
- **sherpa-onnx 的 C API**：情绪固定输出 NEUTRAL，判定为其自身实现问题
- **SenseVoice.cpp（GGML 版）**：不仅情绪不对，连文本都识别错误（"想听点恐怖的
  吗"被识别成"职业的人"），判定工程成熟度不足，不可用

所以最终选择直接复用 `sensevoice-onnx` 这套已验证正确的 Python 实现，包一层
HTTP API 供 C# 调用，而不是继续在 C++ 生态里找/写绑定。

## 架构

```
本服务 (Sonvert.SenseVoiceService, FastAPI)
  ├─ model_manager.py  加载/卸载/推理，包了 sensevoice-onnx 包的内部调用
  ├─ config.py          端口等配置，从 service_config.json 读取（C# 端可改写此文件）
  └─ main.py            FastAPI 入口，5 个接口
```
音频约定：**已经 VAD 切分好的单段语音**，PCM16LE / 16kHz / 单声道，
本服务不做二次 VAD（sensevoice-onnx 自带的 VAD 实测有个已知 bug——
`fsmn-config.yaml` 里存的模型路径是绝对路径，跟外部传入的 resource_dir
再拼接一次会导致路径重复、加载失败——所以即便将来想用也得先绕开这个问题）。

## API 接口

本地回环地址，端口默认 `8878`（`service_config.json` 里 `port` 字段可改）。

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/health` | 健康检查，返回 `{"status": "ok", "model_loaded": bool}` |
| POST | `/model/load` | body: `{"precision": "int8"\|"fp32"}`，幂等（已加载会先卸载再加载） |
| POST | `/model/unload` | 卸载模型，进程不退出 |
| POST | `/recognize` | body 为原始 PCM 字节；query: `language`(默认auto), `use_itn`(默认true) |
| POST | `/shutdown` | 响应后异步退出整个进程，仅在 C# 主程序退出前调用一次 |

错误统一返回对应 HTTP 状态码 + `{"error": "..."}`（不是统一 200 包一层）。
`/recognize` 在模型未加载时返回 `409`。

### `/recognize` 响应示例
```json
{
  "text": "想听点恐怖的吗？",
  "language": "zh",
  "emotion": "ANGRY",
  "event": "Speech"
}
```

`language` 支持值：`auto`(仅请求用) / `zh` / `en` / `yue` / `ja` / `ko` / `nospeech`
（对应 `model_manager.py` 里 `LANGUAGE_IDS`）。

`emotion`/`event` 是从模型解码结果的 `<|xxx|>` 标签里解析出来的，已知取值集合见
`model_manager.py` 里 `KNOWN_EMOTIONS`/`KNOWN_EVENTS`——如果测试中在服务日志里
看到 `Unrecognized tag(s)` 的 WARNING，说明遇到了未收录的新标签，需要补充这两个
集合，否则对应字段会是 `null`。

## 已验证的重要发现

1. **int8 量化会削弱 `event`（事件分类）的准确性**：同一段音频，fp32 正确识别为
   `Speech`，int8 误判为 `BGM`（文本和情绪两个字段两种精度下都正确，只有 event
   这个分支受影响）。做精度选择 UI 时应该提示用户这个权衡，不要让用户在不知情
   的情况下觉得"情绪识别不准"是 bug。
2. **模型资源不是只有一个 onnx 文件**，加载需要以下几个文件配套（放在本服务的
   `models/` 目录下，不进 git 仓库，需要手动下载/拷贝）：
   - `am.mvn`
   - `embedding.npy`
   - `sense-voice-encoder.onnx` / `sense-voice-encoder-int8.onnx`
   - `chn_jpn_yue_eng_ko_spectok.bpe.model`

## 本地开发环境搭建

```powershell
# 建虚拟环境（VS 的"添加环境"功能，或手动 python -m venv env）
pip install -r requirements.txt -r requirements-dev.txt

# 把 4 个模型资源文件放进 models/ 目录（见上）

python main.py
```

测试脚本：`scripts/test_recognize.py`，用法见文件内注释。

## TODO

- [ ] C# 端子进程生命周期管理代码（Process.Start / 写 service_config.json / 健康检查轮询）
- [ ] PyInstaller 打包成独立 exe，供最终分发（不要求用户机器装 Python）
- [ ] GPU 推理支持（当前 `device_id` 写死 -1，即 CPU）
