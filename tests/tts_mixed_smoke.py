"""Validate mixed-language synthesis and measure conversion/synthesis costs locally."""
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
import soundfile as sf
import server

texts = [
    "这是 Hello world 的意思。",
    "我们使用 Python 编程。",
    "这项研究使用 AI 技术。",
    "请阅读 machine learning 这一段。",
    "这台电脑运行 Windows 11 系统。",
    "这是 Hello world 的意思。",
]


def audio_info(data):
    with wave.open(io.BytesIO(data)) as wav:
        assert wav.getnframes() > 0
        return wav.getnframes() / wav.getframerate()


def old_synthesize(text):
    with server._synthesis_slots:
        with server._g2p_lock:
            ipa = server.zh_g2p(text)
        samples, sr = server.kokoro.create(ipa, voice=server.voice_styles["zf_xiaoxiao"],
                                           speed=1.0, lang="cmn", is_phonemes=True)
        output = io.BytesIO()
        sf.write(output, samples, sr, format="WAV")
        return output.getvalue()


def new_synthesize(text):
    response = server.tts(server.TtsReq(text=text))
    assert response.status_code == 200, response.body
    return response.body


# Exclude jieba's existing cold-start cost when measuring the added English conversion.
with server._g2p_lock:
    server.zh_g2p("中文。")
for text in texts[:-1]:
    start = time.perf_counter()
    with server._g2p_lock:
        server.zh_g2p(text)
    old_ms = (time.perf_counter() - start) * 1000
    start = time.perf_counter()
    with server._g2p_lock:
        ipa = server.to_phonemes(text, server.zh_g2p, server.english_phonemes)
    print(f"convert chars={len(text)} old={old_ms:.1f}ms new={(time.perf_counter() - start) * 1000:.1f}ms "
          f"phonemes={ipa}", flush=True)

with ThreadPoolExecutor(max_workers=2) as pool:
    start = time.perf_counter()
    old_audio = list(pool.map(old_synthesize, texts))
    old_seconds = time.perf_counter() - start
    start = time.perf_counter()
    new_audio = list(pool.map(new_synthesize, texts))
    new_seconds = time.perf_counter() - start
assert new_audio[0] == new_audio[-1], "Concurrent conversion mixed up identical sentences."
assert new_audio[0] != old_audio[0], "The English pronunciation did not change."
old_duration = sum(map(audio_info, old_audio))
new_duration = sum(map(audio_info, new_audio))
print(f"PASS: six mixed sentences produce valid single WAVs, repeated input is identical. "
      f"Old={old_seconds:.2f}s ({old_duration:.2f}s audio), "
      f"new={new_seconds:.2f}s ({new_duration:.2f}s audio).", flush=True)
