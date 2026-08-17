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

logger = logging.getLogger("sense_voice_service")

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

    def load(self, precision: str) -> float:
        """Load the given precision's model, return load time in ms.
        If a model is already loaded, unload it first (idempotent)."""
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
            device_id=-1,  # TODO: wire up GPU device id later if needed
            intra_op_num_threads=4,
        )
        self._precision = precision

        elapsed_ms = (time.time() - start) * 1000
        logger.info("Model loaded, precision=%s, took %.1fms", precision, elapsed_ms)
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


# Singleton, shared by the whole service process. resource_dir points at the
# folder containing am.mvn / embedding.npy / *.onnx / *.bpe.model - see README
# for how to obtain these (already downloaded once during local testing).
manager = ModelManager(resource_dir="models")