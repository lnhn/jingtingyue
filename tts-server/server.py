import io
import os
import threading
import time
from functools import lru_cache
import onnxruntime as ort
import soundfile as sf
from fastapi import FastAPI
from fastapi.responses import Response
from pydantic import BaseModel
from kokoro_onnx import Kokoro
from misaki.zh import ZHG2P
from mixed_phonemes import has_chinese, to_phonemes
from punctuation_audio import synthesize
from espeak_paths import configure_espeak

MODEL = "model_uint8.onnx" if os.path.exists("model_uint8.onnx") else "model.onnx"
VOICES = "voices.npz"

ZH_VOICES = {
    "女声·晓晓": "zf_xiaoxiao",
    "女声·小北": "zf_xiaobei",
    "男声·云健": "zm_yunjian",
    "男声·云希": "zm_yunxi",
    "男声·云扬": "zm_yunyang",
}

print("loading kokoro...", flush=True)
session_options = ort.SessionOptions()
# Small CPUs can spend more time coordinating ONNX worker threads than inferring.
session_options.intra_op_num_threads = min(2, os.cpu_count() or 1)
session_options.inter_op_num_threads = 1
session = ort.InferenceSession(MODEL, sess_options=session_options,
                               providers=["CPUExecutionProvider"])
kokoro = Kokoro.from_session(session, VOICES)
configure_espeak()
voice_styles = {voice: kokoro.get_voice_style(voice) for voice in ZH_VOICES.values()}
zh_g2p = ZHG2P()
_g2p_lock = threading.Lock()
_synthesis_slots = threading.BoundedSemaphore(2)
print("kokoro ready", flush=True)

app = FastAPI()

class TtsReq(BaseModel):
    text: str
    voice: str = "zf_xiaoxiao"
    speed: float = 1.0

@app.get("/voices")
def voices():
    return {"voices": [{"id": k, "name": v} for k, v in ZH_VOICES.items()]}

@lru_cache(maxsize=256)
def english_phonemes(text: str) -> str:
    return kokoro.tokenizer.phonemize(text, "en-us")

@app.post("/tts")
def tts(req: TtsReq):
    text = (req.text or "").strip()
    if not text:
        return Response(status_code=204)
    voice = req.voice if req.voice in ZH_VOICES.values() else "zf_xiaoxiao"
    speed = max(0.6, min(1.8, req.speed))
    started = time.perf_counter()

    try:
        with _synthesis_slots:
            # Only text conversion is serialized; clauses share one request and one WAV.
            with _g2p_lock:
                ipa = to_phonemes(text, zh_g2p, english_phonemes)
            lang = "cmn" if has_chinese(text) else "en-us"
            samples, sr = synthesize(ipa, speed, lambda fragment: kokoro.create(
                fragment, voice=voice_styles[voice], speed=speed, lang=lang,
                is_phonemes=True))
    except Exception as e:
        print(f"tts error: {e!r}", flush=True)
        return Response(content=f"synthesis failed: {e!r}", media_type="text/plain", status_code=500)

    buf = io.BytesIO()
    sf.write(buf, samples, sr, format="WAV")
    print(f"tts complete: chars={len(text)} elapsed={time.perf_counter() - started:.2f}s "
          f"audio={len(samples) / sr:.2f}s", flush=True)
    return Response(content=buf.getvalue(), media_type="audio/wav")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="127.0.0.1", port=8123, log_level="warning")
