import urllib.request, os
base = "https://modelscope.cn/models/onnx-community/Kokoro-82M-v1.0-ONNX/resolve/master/"
files = [
    ("model_uint8.onnx", "onnx/model_uint8.onnx"),
    ("zf_xiaoxiao.bin", "voices/zf_xiaoxiao.bin"),
    ("zf_xiaobei.bin", "voices/zf_xiaobei.bin"),
    ("zm_yunjian.bin", "voices/zm_yunjian.bin"),
    ("zm_yunxi.bin", "voices/zm_yunxi.bin"),
    ("zm_yunyang.bin", "voices/zm_yunyang.bin"),
]
for fn, sub in files:
    if os.path.exists(fn) and os.path.getsize(fn) > 1000:
        print("skip", fn, os.path.getsize(fn), flush=True); continue
    try:
        req = urllib.request.Request(base + sub, headers={"User-Agent": "Mozilla/5.0"})
        with urllib.request.urlopen(req, timeout=300) as r, open(fn, "wb") as f:
            while True:
                c = r.read(1 << 20)
                if not c: break
                f.write(c)
        print("ok", fn, os.path.getsize(fn), flush=True)
    except Exception as e:
        print("fail", fn, repr(e)[:150], flush=True)
