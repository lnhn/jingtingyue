import urllib.request, os, sys
proxy = urllib.request.ProxyHandler({"http": "http://127.0.0.1:7890", "https": "http://127.0.0.1:7890"})
opener = urllib.request.build_opener(proxy)
opener.addheaders = [("User-Agent", "Mozilla/5.0")]
urllib.request.install_opener(opener)

urls = [
    ("kokoro-v1.0.onnx", "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx"),
    ("voices-v1.0.bin", "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin"),
]
for fn, u in urls:
    if os.path.exists(fn) and os.path.getsize(fn) > 1_000_000:
        print("skip", fn, os.path.getsize(fn), flush=True); continue
    for attempt in range(4):
        try:
            with urllib.request.urlopen(u, timeout=600) as r, open(fn, "wb") as f:
                while True:
                    chunk = r.read(1 << 20)
                    if not chunk: break
                    f.write(chunk)
            print("ok", fn, os.path.getsize(fn), flush=True); break
        except Exception as e:
            print("retry", attempt, fn, repr(e)[:120], flush=True)
    else:
        print("FAILED", fn, flush=True)
