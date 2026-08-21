"""
MT model manager，基于 Optimum 的 ORTModelForSeq2SeqLM（底层还是 ONNX Runtime）。

跟 SenseVoiceService 的差异：这里没有做 int8/fp32 切换、没有单独的
/model/load 步骤——OPUS-MT 单方向模型只有几十 MB，首次调用时按需加载
（懒加载）即可，加载耗时可以忽略不计，没必要为了这点耗时单独设计一个
"预加载"接口增加复杂度。每个方向加载一次后缓存住，同一进程内不会重复加载。
"""
import logging
import time
from pathlib import Path
from typing import Dict, Tuple

logger = logging.getLogger("mt_service")

SUPPORTED_DIRECTIONS = {"zh-en", "en-zh"}


class ModelManager:
    def __init__(self, resource_dir: str):
        self.resource_dir = Path(resource_dir)
        # direction ("zh-en" / "en-zh") -> (model, tokenizer)，懒加载后缓存
        self._loaded: Dict[str, Tuple[object, object]] = {}

    @property
    def loaded_directions(self):
        return list(self._loaded.keys())

    def _get_or_load(self, direction: str):
        if direction in self._loaded:
            return self._loaded[direction]

        model_dir = self.resource_dir / direction
        if not model_dir.exists():
            raise FileNotFoundError(
                f"找不到 {direction} 方向的模型目录: {model_dir}，"
                "先运行 scripts/export_onnx.py 把模型导出到这里"
            )

        from optimum.onnxruntime import ORTModelForSeq2SeqLM
        from transformers import AutoTokenizer

        start = time.time()
        model = ORTModelForSeq2SeqLM.from_pretrained(str(model_dir))
        tokenizer = AutoTokenizer.from_pretrained(str(model_dir))
        elapsed_ms = (time.time() - start) * 1000
        logger.info("MT model loaded, direction=%s, took %.1fms", direction, elapsed_ms)

        self._loaded[direction] = (model, tokenizer)
        return self._loaded[direction]

    def translate(self, text: str, source_lang: str, target_lang: str) -> str:
        direction = f"{source_lang}-{target_lang}"
        if direction not in SUPPORTED_DIRECTIONS:
            raise ValueError(
                f"不支持的翻译方向: {direction}，目前只支持 {SUPPORTED_DIRECTIONS}"
            )

        model, tokenizer = self._get_or_load(direction)

        inputs = tokenizer(text, return_tensors="pt")
        outputs = model.generate(**inputs, max_length=512)
        return tokenizer.decode(outputs[0], skip_special_tokens=True)

    def preload_all(self) -> float:
        """一次性把 zh-en 和 en-zh 两个方向都加载好，返回耗时（毫秒）。
        翻译方向只有这两个，直接都加载，不用像 SenseVoice 那样按需选一个精度。"""
        start = time.time()
        for direction in SUPPORTED_DIRECTIONS:
            self._get_or_load(direction)
        return (time.time() - start) * 1000


# resource_dir 下预期结构：models/zh-en/、models/en-zh/，每个目录里是
# export_onnx.py 导出的 ONNX 模型 + tokenizer 文件，参见 README。
manager = ModelManager(resource_dir="models")