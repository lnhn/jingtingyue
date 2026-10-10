"""Compare serial and two concurrent requests on the same model; no audio files are saved."""
import io
import os
import sys
import time
import wave
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

server_dir = Path(__file__).resolve().parents[1] / "tts-server"
os.chdir(server_dir)
sys.path.insert(0, str(server_dir))
import server

texts = [
    "从前有座山，山里有座庙。",
    "庙里有个老和尚，正在给小和尚讲故事。",
    "窗外的风轻轻吹过，故事又开始了。",
    "天色渐渐暗下来，远处亮起了灯。",
    "她翻开书，继续读下一页。",
    "雨停了，阳光照进窗户。",
]


def synthesize(text):
    response = server.tts(server.TtsReq(text=text, voice="zf_xiaoxiao", speed=1.0))
    assert response.status_code == 200, response.body
    with wave.open(io.BytesIO(response.body)) as wav:
        assert wav.getnframes() > 0
        return wav.getnframes() / wav.getframerate(), wav.readframes(wav.getnframes())


start = time.perf_counter()
serial = [synthesize(text) for text in texts]
serial_seconds = time.perf_counter() - start
start = time.perf_counter()
with ThreadPoolExecutor(max_workers=2) as pool:
    parallel = list(pool.map(synthesize, texts))
parallel_seconds = time.perf_counter() - start
assert [duration for duration, _ in serial] == [duration for duration, _ in parallel]
assert all(a[1] == b[1] for a, b in zip(serial, parallel)), "Concurrent requests changed the synthesized audio."
print(f"PASS: six valid WAVs, concurrent audio identical to serial. "
      f"Serial={serial_seconds:.2f}s parallel={parallel_seconds:.2f}s "
      f"audio={sum(duration for duration, _ in serial):.2f}s", flush=True)
