# 朗读缓冲与发音验证

无需语音模型的调度测试：

```powershell
dotnet run --project tests/JingTingYue.BufferTests
```

验证启动前确实准备好五句、最多两个并发请求、每句请求去重、持续补充到十句以后、停止取消、单句失败和章节末尾。

使用本机模型对比同一批文字的串行与并发合成，并检查音频内容一致：

```powershell
tts-server/python/python.exe tests/tts_server_smoke.py
```

Windows 实际播放验证需要 `127.0.0.1:8123` 的语音服务已启动；会播放测试语句。先关闭阅读器，单独启动服务后运行：

```powershell
cd tts-server
python/python.exe server.py
```

另一个终端在项目根目录运行：

```powershell
dotnet run --project tests/JingTingYue.PlaybackTests
```

验证缓冲期间暂停不出声、十二句按序播放、停止合成后重启不混入旧音频，并报告播放中重新缓冲的次数。性能计时和播放测试应分开运行，以免争抢 CPU 影响结果。

中英混读的分段和真实发音转换测试，无需加载语音模型：

```powershell
tts-server/python/python.exe tests/test_mixed_phonemes.py -v
```

用模型比较修复前后的中英混合句，检查单段 WAV 输出，并报告新增转换时间和合成耗时：

```powershell
tts-server/python/python.exe tests/tts_mixed_smoke.py
```

服务已启动后，连续播放十二句中英混合文本：

```powershell
dotnet run --project tests/JingTingYue.PlaybackTests -- --mixed
```

逗号、分号停顿验证：

```powershell
tts-server/python/python.exe tests/test_punctuation_audio.py -v
tts-server/python/python.exe tests/tts_punctuation_smoke.py
dotnet run --project tests/JingTingYue.PlaybackTests -- --punctuation
```

前两项检查标点规范化、语速、引号、已有静音、拼接位置和实际模型耗时；最后一项需要独立启动语音服务，验证连续播放十二句。

当前模型仅输出 waveform，无法直接定位整句中的标点。因此服务在一次请求内分句合成、补足静音并拼成一个 WAV。常速下逗号至少 0.18 秒、分号 0.30 秒、句末 0.40 秒，已有静音计入目标时长，目标随语速调整。分句会增加模型调用次数；实测六句约从 17.62 秒变为 33.54 秒，十二句播放未触发中途缓冲，但不能据此保证任意长篇持续播放都不重新缓冲。

绿色版发布包完整性检查及隔离解压：

```powershell
tts-server/python/python.exe tests/check_release_zip.py dist/JingTingYue-v1.3.0-win-x64.zip --extract-to "build/绿色版验证 空格"
```

检查 SHA-256、所有文件的 CRC、运行组件和字体许可，并确保没有打入用户数据。解压目标需尚不存在。随后直接运行解压后的 EXE，检查数据保存在其目录、语音服务使用同目录 Python，以及阅读页面使用随包 WebView2。
