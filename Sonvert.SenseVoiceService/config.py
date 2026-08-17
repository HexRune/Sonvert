"""
服务配置。端口需要能在 C# 的设置界面里修改，所以从外部 JSON 文件读取，
不写死在代码里。C# 端负责在启动子进程前把用户选的端口写进这个文件。
"""
import json
from pathlib import Path

DEFAULT_CONFIG = {
    "port": 8878,       # 默认端口，尽量避开常见程序占用的端口段
    "host": "127.0.0.1",  # 只监听本地回环，不对外网开放
}

CONFIG_PATH = Path(__file__).parent / "service_config.json"


def load_config() -> dict:
    if CONFIG_PATH.exists():
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            user_config = json.load(f)
        return {**DEFAULT_CONFIG, **user_config}
    return DEFAULT_CONFIG.copy()


config = load_config()
