"""Measure punctuation synthesis and verify pauses in the real model output."""
import io
import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

import numpy as np
import soundfile as sf

server_dir = Path(__file__).resolve().parents[1] / "tts-server"
os.chdir(server_dir)
sys.path.insert(0, str(server_dir))
import server
from punctuation_audio import clauses, synthesize

texts = [
    "风来疏竹，风过而竹不留声；",
    "雁度寒潭，雁去而潭不留影。",
    "我们使用 Python，继续学习 AI；",
    "她翻开书，继续读下一页。",
    "请稍等，准备好了吗？",
    "她说：“Hello world，请继续。”",
]
with server._g2p_lock:
    converted = [server.to_phonemes(text, server.zh_g2p, server.english_phonemes) for text in texts]


def baseline(ipa):
    with server._synthesis_slots:
        # Compare against punctuation normalization alone using the same model/voices.
        audio, sr = server.kokoro.create(ipa.translate(str.maketrans("，、；：。！？", ",,;:.!?")),
                                        voice=server.voice_styles["zf_xiaoxiao"], is_phonemes=True)
        return len(audio) / sr


def measured(ipa):
    records = []

    def create(fragment):
        audio, sr = server.kokoro.create(fragment, voice=server.voice_styles["zf_xiaoxiao"],
                                        is_phonemes=True)
        records.append((audio, sr))
        return audio, sr

    with server._synthesis_slots:
        output, sr = synthesize(ipa, 1, create)
    offset = 0
    for (fragment, pause), (original, rate) in zip(clauses(ipa, 1), records):
        active = np.flatnonzero(np.abs(original) > np.max(np.abs(original)) * .001)
        trailing = len(original) - active[-1] - 1 if len(active) else len(original)
        padding = max(0, round(pause * rate) - trailing)
        end = offset + len(original) + padding
        if pause:
            tail = output[end - round(pause * rate):end]
            assert len(tail) == round(pause * rate)
            assert np.max(np.abs(tail)) <= np.max(np.abs(original)) * .001
        np.testing.assert_array_equal(output[offset:offset + len(original)], original)
        offset = end
    assert offset == len(output)
    return len(output) / sr


with ThreadPoolExecutor(max_workers=2) as pool:
    start = time.perf_counter()
    baseline_audio = sum(pool.map(baseline, converted))
    baseline_seconds = time.perf_counter() - start
    start = time.perf_counter()
    corrected_audio = sum(pool.map(measured, converted))
    corrected_seconds = time.perf_counter() - start
response = server.tts(server.TtsReq(text=texts[2]))
assert response.status_code == 200, response.body
decoded, rate = sf.read(io.BytesIO(response.body))
assert len(decoded) > 0
assert np.max(np.abs(decoded[-round(.30 * rate):])) < .001
print(f"PASS: real comma/semicolon/end pauses, untouched speech and one response WAV. "
      f"Normalized only={baseline_seconds:.2f}s ({baseline_audio:.2f}s audio), "
      f"with pauses={corrected_seconds:.2f}s ({corrected_audio:.2f}s audio).", flush=True)
