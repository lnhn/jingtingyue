"""Run with the chosen runtime's python.exe and its TTS directory as argument."""
import os
import sys
from pathlib import Path

source_dir = Path(__file__).resolve().parents[1] / "tts-server"
sys.path.insert(0, str(source_dir))
from espeak_paths import configure_espeak
from kokoro_onnx.tokenizer import Tokenizer

os.chdir(sys.argv[1])
tokenizer = Tokenizer()
configure_espeak()
for phrase in ("Hello world", "Python", "AI"):
    phonemes = tokenizer.phonemize(phrase, "en-us")
    assert phonemes and phrase not in phonemes, f"English conversion failed for {phrase}"
print("PASS: bundled English phonemes in the chosen working directory.", flush=True)
