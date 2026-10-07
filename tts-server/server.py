import io
import re
import soundfile as sf
from fastapi import FastAPI
from fastapi.responses import Response
from pydantic import BaseModel
from kokoro_onnx import Kokoro
from misaki.zh import ZHG2P

import os
MODEL = "model_uint8.onnx" if os.path.exists("model_uint8.onnx") else "model.onnx"
VOICES = "voices.npz"

# 中文音色。只暴露真实存在的。
ZH_VOICES = {
    "女声·晓晓": "zf_xiaoxiao",
    "女声·小北": "zf_xiaobei",
    "男声·云健": "zm_yunjian",
    "男声·云希": "zm_yunxi",
    "男声·云扬": "zm_yunyang",
}

print("loading kokoro...", flush=True)
kokoro = Kokoro(MODEL, VOICES)
zh_g2p = ZHG2P()
print("kokoro ready", flush=True)

app = FastAPI()

class TtsReq(BaseModel):
    text: str
    voice: str = "zf_xiaoxiao"
    speed: float = 1.0

@app.get("/voices")
def voices():
    return {"voices": [{"id": k, "name": v} for k, v in ZH_VOICES.items()]}

def to_phonemes(text: str) -> str:
    # 含中文时用 misaki 的 pypinyin G2P 转 IPA；否则交给 espeak
    if re.search(r'[\u4e00-\u9fff]', text):
        return zh_g2p(text)
    return text

@app.post("/tts")
def tts(req: TtsReq):
    text = (req.text or "").strip()
    if not text:
        return Response(status_code=204)
    voice = req.voice if req.voice in ZH_VOICES.values() else "zf_xiaoxiao"
    speed = max(0.6, min(1.8, req.speed))
    ipa = to_phonemes(text)
    samples, sr = kokoro.create(ipa, voice=voice, speed=speed, lang="cmn", is_phonemes=True)
    buf = io.BytesIO()
    sf.write(buf, samples, sr, format="WAV")
    return Response(content=buf.getvalue(), media_type="audio/wav")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8123, log_level="warning")
