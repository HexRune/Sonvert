"""
Model manager: wraps load/unload/recognize on top of the actual
sensevoice-onnx package internals (verified locally to produce correct
text + emotion, unlike sherpa-onnx's SenseVoice implementation).

Reference for how these calls are supposed to be wired up came directly
from reading sensevoice/sense_voice.py and sense_voice_ort_session.py in
the installed package - not guessed from memory, since we already got
burned once assuming field/function names on sherpa-onnx's C API.
"""
import gc
import logging
import re
import time
from pathlib import Path
from typing import Optional

import numpy as np
from onnxruntime import get_available_providers, get_device

from config import config

logger = logging.getLogger("sense_voice_service")


def gpu_available() -> bool:
    """当前 onnxruntime 安装是否真的能跑 CUDAExecutionProvider。

    只有装了 onnxruntime-gpu（而不是普通 onnxruntime）、且机器上的 CUDA/cuDNN
    版本跟这个 onnxruntime-gpu 编译时依赖的版本匹配，这里才会是 True。
    单纯"有没有 NVIDIA 显卡"不是这里判断的依据 —— get_available_providers()
    问的是 onnxruntime 这个进程实际加载成功的 EP 列表，两者不匹配时（比如装了
    onnxruntime-gpu 但系统没装对应版本的 cuDNN）这里如实返回 False，
    UI 才不会把用户导向一个"选了也用不了"的选项。
    """
    return get_device() == "GPU" and "CUDAExecutionProvider" in get_available_providers()

# Same as SenseVoice-python's sense_voice.py `languages` dict
LANGUAGE_IDS = {"auto": 0, "zh": 3, "en": 4, "yue": 7, "ja": 11, "ko": 12, "nospeech": 13}

# SenseVoice's decoded text looks like: "<|zh|><|ANGRY|><|Speech|><|withitn|>xxx"
# - pull out the <|...|> tags in order, whatever's left after stripping them is the transcript.
TAG_PATTERN = re.compile(r"<\|([^|]+)\|>")

# Best-known tag vocabularies, used to figure out which tag is emotion vs event
# (not positional, in case a future model version omits one). If a run logs an
# unrecognized tag, add it here - see the "unmatched tag" warning in _parse_result.
KNOWN_EMOTIONS = {
    "NEUTRAL", "HAPPY", "SAD", "ANGRY", "FEARFUL",
    "DISGUSTED", "SURPRISED", "EMO_UNKNOWN",
}
KNOWN_EVENTS = {
    "Speech", "BGM", "Applause", "Laughter", "Cry", "Sneeze", "Breath", "Cough",
}


class ModelNotLoadedError(Exception):
    pass


class ModelManager:
    def __init__(self, resource_dir: str):
        self.resource_dir = Path(resource_dir)
        self._session = None
        self._frontend = None
        self._precision: Optional[str] = None

    @property
    def is_loaded(self) -> bool:
        return self._session is not None

    def load(self, precision: str, device_id: int = -1) -> float:
        """Load the given precision's model, return load time in ms.
        If a model is already loaded, unload it first (idempotent).

        device_id: -1 = CPU, 0 = 第一块 GPU. 透传给 sensevoice-onnx 内部的
        OrtInferRuntimeSession —— 它自己会检查 CUDAExecutionProvider 是否真的
        可用，不可用时自动回退 CPU 并打 warning，这里不用重复做判断（调用方
        应该先看 gpu_available() 再决定要不要传 0，把"能不能用GPU"和"要不要
        用GPU"这两件事分开：前者是环境事实，后者是用户选择）。
        """
        if self.is_loaded:
            logger.info("Model already loaded (precision=%s), unloading first", self._precision)
            self.unload()

        start = time.time()

        from sensevoice.onnx.sense_voice_ort_session import SenseVoiceInferenceSession
        from sensevoice.utils.frontend import WavFrontend

        encoder_filename = (
            "sense-voice-encoder-int8.onnx" if precision == "int8" else "sense-voice-encoder.onnx"
        )
        encoder_path = self.resource_dir / encoder_filename
        if not encoder_path.exists():
            raise FileNotFoundError(f"Encoder model not found: {encoder_path}")

        self._frontend = WavFrontend(str(self.resource_dir / "am.mvn"))
        self._session = SenseVoiceInferenceSession(
            str(self.resource_dir / "embedding.npy"),
            str(encoder_path),
            str(self.resource_dir / "chn_jpn_yue_eng_ko_spectok.bpe.model"),
            device_id=device_id,
            intra_op_num_threads=4,
        )
        self._precision = precision

        elapsed_ms = (time.time() - start) * 1000
        logger.info(
            "Model loaded, precision=%s, device_id=%s, took %.1fms",
            precision, device_id, elapsed_ms,
        )
        return elapsed_ms

    def unload(self):
        if not self.is_loaded:
            return
        self._session = None
        self._frontend = None
        self._precision = None
        gc.collect()
        logger.info("Model unloaded")

    def recognize(self, pcm_bytes: bytes, language: str = "auto", use_itn: bool = True) -> dict:
        """pcm_bytes: raw PCM16LE / 16kHz / mono, already VAD-segmented on the C# side."""
        if not self.is_loaded:
            raise ModelNotLoadedError("Model is not loaded yet")

        if language not in LANGUAGE_IDS:
            raise ValueError(f"Unsupported language: {language}")

        waveform = np.frombuffer(pcm_bytes, dtype=np.int16).astype(np.float32) / 32768.0

        audio_feats = self._frontend.get_features(waveform)
        raw_result = self._session(
            audio_feats[None, ...],
            language=LANGUAGE_IDS[language],
            use_itn=use_itn,
        )

        return self._parse_result(raw_result)

    @staticmethod
    def _parse_result(raw_text: str) -> dict:
        tags = TAG_PATTERN.findall(raw_text)
        text = TAG_PATTERN.sub("", raw_text).strip()

        language = next((t for t in tags if t in LANGUAGE_IDS), None)
        emotion = next((t for t in tags if t in KNOWN_EMOTIONS), None)
        event = next((t for t in tags if t in KNOWN_EVENTS), None)

        unmatched = [t for t in tags if t not in LANGUAGE_IDS
                     and t not in KNOWN_EMOTIONS and t not in KNOWN_EVENTS
                     and t not in ("withitn", "woitn")]
        if unmatched:
            logger.warning("Unrecognized tag(s) in model output, add to KNOWN_* sets: %s", unmatched)

        return {
            "text": text,
            "language": language,
            "emotion": emotion,
            "event": event,
        }


# Singleton, shared by the whole service process. resource_dir 来自
# service_config.json 的 resource_dir 字段（见 config.py 的注释）——
# 开发期是相对路径 "models"，打包后 C# 端会覆写成软件目录下的绝对路径，
# 这里不用关心具体是哪种，Path() 两种都能正确处理。
manager = ModelManager(resource_dir=config["resource_dir"])