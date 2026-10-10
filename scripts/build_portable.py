"""Build the single-file Windows release from the app and local offline runtime inputs.

Requires the .NET 8 SDK, Python 3, embedded TTS Python/model/voices and WebView2 Fixed.
The last two may be supplied in tts-server/ and webview2-fixed/, or reused from publish/.
"""

from __future__ import annotations

import hashlib
import shutil
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BUILD = ROOT / "build"
RUNTIME = BUILD / "runtime"
PAYLOAD = ROOT / "portable-payload.zip"


def source(*candidates: Path) -> Path:
    for candidate in candidates:
        if candidate.exists():
            return candidate
    raise FileNotFoundError("Missing packaging input: " + ", ".join(map(str, candidates)))


def main() -> None:
    BUILD.mkdir(exist_ok=True)
    if RUNTIME.exists():
        shutil.rmtree(RUNTIME)
    subprocess.run(
        ["dotnet", "publish", str(ROOT / "src/JingTingYue/JingTingYue.csproj"),
         "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", str(RUNTIME)],
        check=True,
    )

    tts = RUNTIME / "tts-server"
    tts.mkdir(exist_ok=True)
    for name in ("server.py", "mixed_phonemes.py", "punctuation_audio.py", "model_uint8.onnx", "voices.npz"):
        shutil.copy2(source(ROOT / "tts-server" / name, ROOT / "publish/tts-server" / name), tts / name)
    shutil.copytree(
        source(ROOT / "tts-server/python", ROOT / "publish/tts-server/python"),
        tts / "python",
        dirs_exist_ok=True,
    )
    shutil.copytree(
        source(ROOT / "webview2-fixed", ROOT / "publish/WebView2Fixed"),
        RUNTIME / "WebView2Fixed",
        dirs_exist_ok=True,
    )
    for needed in (RUNTIME / "JingTingYue.exe", tts / "python/python.exe",
                   tts / "model_uint8.onnx", RUNTIME / "WebView2Fixed/msedgewebview2.exe"):
        if not needed.is_file():
            raise FileNotFoundError(needed)

    with zipfile.ZipFile(PAYLOAD, "w", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=6, allowZip64=True) as archive:
        for path in RUNTIME.rglob("*"):
            if path.is_file():
                archive.write(path, path.relative_to(RUNTIME))
    with PAYLOAD.open("rb") as stream:
        checksum = hashlib.file_digest(stream, "sha256").hexdigest()
    (ROOT / "portable-payload.sha256").write_text(checksum, encoding="ascii")

    launcher_out = BUILD / "launcher"
    subprocess.run(
        ["dotnet", "publish", str(ROOT / "src/JingTingYue.Portable/JingTingYue.Portable.csproj"),
         "-c", "Release", "-r", "win-x64", "--self-contained", "true", "-o", str(launcher_out)],
        check=True,
    )
    dist = ROOT / "dist"
    dist.mkdir(exist_ok=True)
    target = dist / "JingTingYue-v1.2.0-win-x64.exe"
    shutil.copy2(launcher_out / "JingTingYue.Portable.exe", target)
    print(f"Created {target} ({target.stat().st_size:,} bytes)")
    with target.open("rb") as stream:
        print("SHA256:", hashlib.file_digest(stream, "sha256").hexdigest())


if __name__ == "__main__":
    main()
