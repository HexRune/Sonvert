"""服务配置，模式跟 SenseVoiceService 的 config.py 完全一致。"""
import json
from pathlib import Path

DEFAULT_CONFIG = {
    "port": 8879,          # 跟 SenseVoiceService 的 8878 错开
    "host": "127.0.0.1",
}

CONFIG_PATH = Path(__file__).parent / "service_config.json"


def load_config() -> dict:
    if CONFIG_PATH.exists():
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            user_config = json.load(f)
        return {**DEFAULT_CONFIG, **user_config}
    return DEFAULT_CONFIG.copy()


config = load_config()