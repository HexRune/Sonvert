"""
MT HTTP 服务入口。

接口约定：
- GET  /health      健康检查，附带已加载的翻译方向列表
- POST /translate   body: {"text": "...", "source_lang": "zh"|"en", "target_lang": "zh"|"en"}
                     返回: {"translated_text": "..."}
- POST /shutdown     进程退出（仅在 C# 主程序退出前调用一次）

错误处理约定跟 SenseVoiceService 保持一致：出错返回对应 HTTP 状态码 + {"error": "..."}。
"""
import sys
import asyncio
import logging

from fastapi import FastAPI, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from config import config
from model_manager import manager

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    stream=sys.stdout,
)
logger = logging.getLogger("mt_service")

app = FastAPI(title="Sonvert MT Service")


class TranslateRequest(BaseModel):
    text: str
    source_lang: str
    target_lang: str


@app.get("/health")
async def health():
    return {"status": "ok", "loaded_directions": manager.loaded_directions}


@app.post("/translate")
async def translate(req: TranslateRequest):
    if not req.text.strip():
        return JSONResponse(status_code=400, content={"error": "text 不能为空"})
    try:
        translated = manager.translate(req.text, req.source_lang, req.target_lang)
        return {"translated_text": translated}
    except FileNotFoundError as e:
        return JSONResponse(status_code=500, content={"error": str(e)})
    except ValueError as e:
        return JSONResponse(status_code=400, content={"error": str(e)})
    except Exception as e:
        logger.exception("翻译过程出错")
        return JSONResponse(status_code=500, content={"error": str(e)})


@app.post("/shutdown")
async def shutdown():
    logger.info("收到关闭服务请求，即将退出进程")

    async def _delayed_exit():
        await asyncio.sleep(0.2)
        import os
        os._exit(0)

    asyncio.create_task(_delayed_exit())
    return {"success": True}

@app.post("/model/load")
async def load_model():
    try:
        elapsed_ms = manager.preload_all()
        return {"success": True, "load_time_ms": round(elapsed_ms, 1)}
    except Exception as e:
        logger.exception("模型预加载失败")
        return JSONResponse(status_code=500, content={"error": str(e)})

if __name__ == "__main__":
    import uvicorn

    logger.info("启动 MT 服务，host=%s port=%s", config["host"], config["port"])
    uvicorn.run(app, host=config["host"], port=config["port"])