"""
SenseVoice HTTP 服务入口。

接口约定（跟 C# 端已确认过一版）：
- GET  /health          健康检查，进程/HTTP服务器是否就绪（不代表模型已加载）
- POST /model/load      加载指定精度的模型，body: {"precision": "int8" | "fp32"}
- POST /model/unload    卸载模型，进程不退出
- POST /recognize       识别语音+情绪，body 为原始 PCM16LE/16kHz/单声道字节流，
                         query 参数: language(默认auto), use_itn(默认true)
- POST /shutdown        整个服务进程退出（仅在 C# 主程序退出前调用一次）

错误处理约定：出错返回对应 HTTP 状态码 + {"error": "..."}，不是统一 200。
"""
import asyncio
import logging

from fastapi import FastAPI, Request, Response
from fastapi.responses import JSONResponse
from pydantic import BaseModel

from config import config
from model_manager import manager, ModelNotLoadedError

logging.basicConfig(level=logging.INFO, format="%(asctime)s [%(levelname)s] %(message)s")
logger = logging.getLogger("sense_voice_service")

app = FastAPI(title="Sonvert SenseVoice Service")


class LoadModelRequest(BaseModel):
    precision: str  # "int8" | "fp32"


@app.get("/health")
async def health():
    return {"status": "ok", "model_loaded": manager.is_loaded}


@app.post("/model/load")
async def load_model(req: LoadModelRequest):
    if req.precision not in ("int8", "fp32"):
        return JSONResponse(
            status_code=400,
            content={"error": f"不支持的精度参数: {req.precision}，只能是 int8 或 fp32"},
        )
    try:
        elapsed_ms = manager.load(req.precision)
        return {"success": True, "load_time_ms": round(elapsed_ms, 1)}
    except Exception as e:
        logger.exception("模型加载失败")
        return JSONResponse(status_code=500, content={"error": str(e)})


@app.post("/model/unload")
async def unload_model():
    manager.unload()
    return {"success": True}


@app.post("/recognize")
async def recognize(request: Request, language: str = "auto", use_itn: bool = True):
    pcm_bytes = await request.body()

    if not pcm_bytes:
        return JSONResponse(status_code=400, content={"error": "请求体为空，需要传原始 PCM 字节"})

    try:
        result = manager.recognize(pcm_bytes, language=language, use_itn=use_itn)
        return result
    except ModelNotLoadedError as e:
        return JSONResponse(status_code=409, content={"error": str(e)})
    except Exception as e:
        logger.exception("识别过程出错")
        return JSONResponse(status_code=500, content={"error": str(e)})


@app.post("/shutdown")
async def shutdown():
    """
    先把响应发出去，再异步退出进程 —— 不能在这个函数里直接退出，
    否则 C# 端的 HTTP 请求会因为连接被中断而报错，看不到正常的成功响应。
    """
    logger.info("收到关闭服务请求，即将退出进程")

    async def _delayed_exit():
        await asyncio.sleep(0.2)  # 留出时间让响应先发送完成
        import os
        os._exit(0)

    asyncio.create_task(_delayed_exit())
    return {"success": True}


if __name__ == "__main__":
    import uvicorn

    logger.info("启动 SenseVoice 服务，host=%s port=%s", config["host"], config["port"])
    uvicorn.run(app, host=config["host"], port=config["port"])
