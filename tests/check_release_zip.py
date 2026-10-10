"""Check green release integrity and extract an isolated smoke-test copy."""
import argparse
import hashlib
from pathlib import Path, PurePosixPath
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--extract-to", type=Path)
    args = parser.parse_args()
    archive_path = args.archive.resolve()
    expected = archive_path.with_suffix(".sha256").read_text(encoding="ascii").split()[0]
    with archive_path.open("rb") as stream:
        actual = hashlib.file_digest(stream, "sha256").hexdigest()
    assert actual == expected, "Archive checksum does not match"
    root = archive_path.stem
    required = {
        "JingTingYue.exe", "JingTingYue.dll", "hostfxr.dll", "coreclr.dll",
        "Microsoft.ui.xaml.dll", "portable.flag", "使用说明.txt",
        "tts-server/server.py", "tts-server/mixed_phonemes.py", "tts-server/punctuation_audio.py",
        "tts-server/espeak_paths.py",
        "tts-server/model_uint8.onnx", "tts-server/voices.npz", "tts-server/python/python.exe",
        "tts-server/python/Lib/site-packages/kokoro_onnx/__init__.py",
        "tts-server/python/Lib/site-packages/misaki/zh.py",
        "WebView2Fixed/msedgewebview2.exe", "WebView2Fixed/msedge.dll",
        "Assets/reader/reader.html", "Assets/samples/chapter1.html",
        "Assets/Fonts/NotoSerifSC/OFL.txt", "Assets/Fonts/LXGWWenKai/OFL.txt",
    }
    private_files = {"books.json", "notes.json", "snippets.json", "theme.txt", "tts.log"}
    with zipfile.ZipFile(archive_path) as archive:
        names = set(archive.namelist())
        for name in names:
            path = PurePosixPath(name)
            assert not path.is_absolute() and ".." not in path.parts and "\\" not in name
            assert path.parts[0] == root, "Archive must have one top-level app directory"
            assert path.name not in private_files, "User data must not be included"
            assert "__pycache__" not in path.parts, "Python caches must not be included"
        missing = {f"{root}/{name}" for name in required} - names
        assert not missing, f"Missing dependencies: {sorted(missing)}"
        assert archive.testzip() is None, "Archive CRC failure"
        if args.extract_to:
            args.extract_to.mkdir(parents=True, exist_ok=False)
            archive.extractall(args.extract_to)
            print(f"Extracted: {args.extract_to.resolve() / root}", flush=True)
    print(f"PASS: SHA256, CRC, {len(names)} files, offline dependencies and no user data.", flush=True)


if __name__ == "__main__":
    main()
