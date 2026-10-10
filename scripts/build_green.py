"""Build a self-contained Windows x64 ZIP that runs directly after extraction."""
from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import uuid
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "src/JingTingYue/JingTingYue.csproj"


def source(*candidates: Path) -> Path:
    for candidate in candidates:
        if candidate.exists():
            return candidate
    raise FileNotFoundError("Missing runtime input: " + ", ".join(map(str, candidates)))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--webview2", type=Path, help="WebView2 Fixed Version x64 directory")
    args = parser.parse_args()
    version = ET.parse(PROJECT).findtext("./PropertyGroup/Version")
    if not version or any(c not in "0123456789." for c in version):
        raise ValueError("Project Version must be a numeric release version")
    name = f"JingTingYue-v{version}-win-x64"
    webview = source(*([args.webview2] if args.webview2 else []),
                     ROOT / "webview2-fixed", ROOT / "publish/WebView2Fixed")
    if not (webview / "msedgewebview2.exe").is_file():
        raise FileNotFoundError("WebView2 Fixed directory lacks msedgewebview2.exe")
    python = source(ROOT / "tts-server/python", ROOT / "publish/tts-server/python")
    inputs = {filename: source(ROOT / "tts-server" / filename, ROOT / "publish/tts-server" / filename)
              for filename in ("server.py", "mixed_phonemes.py", "punctuation_audio.py", "espeak_paths.py",
                               "model_uint8.onnx", "voices.npz")}
    stage = ROOT / "build" / f"green-{version}-{uuid.uuid4().hex[:8]}"
    runtime = stage / name
    runtime.mkdir(parents=True)
    print(f"Publishing {version} into {runtime}", flush=True)
    subprocess.run(["dotnet", "publish", str(PROJECT), "-c", "Release", "-r", "win-x64",
                    "--self-contained", "true", "-o", str(runtime)], check=True)
    tts = runtime / "tts-server"
    tts.mkdir()
    for filename, path in inputs.items():
        shutil.copy2(path, tts / filename)
    ignored = shutil.ignore_patterns("__pycache__", "*.pyc", "*.log")
    print("Copying offline Python, speech model and WebView2...", flush=True)
    shutil.copytree(python, tts / "python", ignore=ignored)
    shutil.copytree(webview, runtime / "WebView2Fixed", ignore=ignored)
    (runtime / "portable.flag").write_text("Keep this file to store data next to JingTingYue.exe.\n", encoding="utf-8")
    (runtime / "使用说明.txt").write_text(
        f"静听阅 v{version} · Windows x64 绿色版\n\n"
        "1. 完整解压整个文件夹，双击 JingTingYue.exe。不要在压缩包内直接运行。\n"
        "2. 无需安装 .NET、Python、WebView2；首次朗读需等待本地模型加载及语音缓冲。\n"
        "3. 请放在可写入的目录。书籍、笔记、阅读进度、朗读片段、浏览器数据和日志均保存在本目录。\n"
        "4. 迁移时先关闭程序，再复制整个文件夹。保留 portable.flag、tts-server、WebView2Fixed 等组件。\n"
        "5. 升级时先关闭旧版，将 books.json、notes.json、snippets.json、theme.txt 和 epubs 文件夹复制到新版目录。\n"
        "6. 适用于 Windows 10/11 x64。本地语音使用 CPU，生成速度取决于设备性能。\n\n"
        "项目与反馈：https://github.com/lnhn/jingtingyue\n", encoding="utf-8-sig")
    for needed in (runtime / "JingTingYue.exe", runtime / "hostfxr.dll", tts / "python/python.exe",
                   tts / "model_uint8.onnx", runtime / "WebView2Fixed/msedgewebview2.exe"):
        if not needed.is_file():
            raise FileNotFoundError(needed)
    dist = ROOT / "dist"
    dist.mkdir(exist_ok=True)
    archive_path = dist / f"{name}.zip"
    print(f"Compressing {archive_path}...", flush=True)
    with zipfile.ZipFile(archive_path, "w", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=6, allowZip64=True) as archive:
        for path in sorted(runtime.rglob("*")):
            if path.is_file():
                archive.write(path, path.relative_to(stage))
    with archive_path.open("rb") as stream:
        checksum = hashlib.file_digest(stream, "sha256").hexdigest()
    (dist / f"{name}.sha256").write_text(f"{checksum}  {archive_path.name}\n", encoding="ascii")
    (dist / "green-build.json").write_text(json.dumps({
        "version": version, "runtime": str(runtime), "zip": str(archive_path),
        "sha256": checksum, "bytes": archive_path.stat().st_size,
    }, indent=2), encoding="utf-8")
    print(f"Created {archive_path} ({archive_path.stat().st_size:,} bytes)\nSHA256: {checksum}", flush=True)


if __name__ == "__main__":
    main()
