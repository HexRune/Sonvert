"""
运行一次即可：从 HuggingFace 下载 opus-mt-zh-en / opus-mt-en-zh 的 PyTorch 权重，
用 Optimum 导出成 ONNX，存到 ../models/{direction}/ 下，供 model_manager.py 加载。

用法（在 Sonvert.MTService 目录下）：
    python scripts/export_onnx.py
"""
from pathlib import Path

from optimum.onnxruntime import ORTModelForSeq2SeqLM
from transformers import AutoTokenizer

MODELS = {
    "zh-en": "Helsinki-NLP/opus-mt-zh-en",
    "en-zh": "Helsinki-NLP/opus-mt-en-zh",
}

OUTPUT_ROOT = Path(__file__).parent.parent / "models"


def main():
    for direction, hf_name in MODELS.items():
        out_dir = OUTPUT_ROOT / direction
        print(f"[{direction}] 正在导出 {hf_name} -> {out_dir} ...")

        model = ORTModelForSeq2SeqLM.from_pretrained(hf_name, export=True)
        tokenizer = AutoTokenizer.from_pretrained(hf_name)

        out_dir.mkdir(parents=True, exist_ok=True)
        model.save_pretrained(out_dir)
        tokenizer.save_pretrained(out_dir)

        print(f"[{direction}] 完成")


if __name__ == "__main__":
    main()