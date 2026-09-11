"""
服务配置。端口需要能在 C# 的设置界面里修改，所以从外部 JSON 文件读取，
不写死在代码里。C# 端负责在启动子进程前把用户选的端口写进这个文件。
"""
import sys
import json
from pathlib import Path

DEFAULT_CONFIG = {
    "port": 8878,       # 默认端口，尽量避开常见程序占用的端口段
    "host": "127.0.0.1",  # 只监听本地回环，不对外网开放
    # 模型资源目录。开发期默认相对路径 "models"（就在 main.py 旁边，
    # 手动跑 python main.py 时沿用老行为）；打包后由 Sonvert.App 在
    # 拉起子进程前把这个字段覆写成软件安装目录下 models\sensevoice
    # 这样的绝对路径——pathlib.Path 对绝对/相对路径处理方式一样，
    # model_manager.py 那边不需要区分这两种情况。
    "resource_dir": "models",
}

# 打包成 PyInstaller onedir 产物后，这个模块是从 _internal\ 目录下的 PYZ
# 归档里加载的，Path(__file__).parent 解析出来的是 _internal\，不是 exe
# 实际所在的目录——C# 端写 service_config.json 是写在 exe 旁边，两边对
# 不上，会导致这个文件被判定为"不存在"，永远读默认配置，且不会报错，
# 排查起来很隐蔽。用 sys.frozen 判断是否处于冻结状态，冻结时改用
# sys.executable 所在目录，跟源码运行时（用 __file__）分开处理。
if getattr(sys, "frozen", False):
    _APP_DIR = Path(sys.executable).parent
else:
    _APP_DIR = Path(__file__).parent

CONFIG_PATH = _APP_DIR / "service_config.json"


def load_config() -> dict:
    if CONFIG_PATH.exists():
        with open(CONFIG_PATH, "r", encoding="utf-8") as f:
            user_config = json.load(f)
        return {**DEFAULT_CONFIG, **user_config}
    return DEFAULT_CONFIG.copy()


config = load_config()
