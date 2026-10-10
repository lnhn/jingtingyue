using System;
using System.Diagnostics;
using System.Net.Http;
using System.IO;

namespace JingTingYue.Services;

/// <summary>
/// 拉起本机 Kokoro TTS 微服务（127.0.0.1:8123）。
/// 已在运行则直接复用；找不到 Python 或模型时不影响普通阅读。
/// </summary>
public static class TtsServer
{
    private static readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(3) };
    private static Process? _proc;

    // 打包后：exe 同级 tts-server\；开发时：相对源码目录。
    private static string Dir
    {
        get
        {
            var packed = Path.Combine(AppContext.BaseDirectory, "tts-server");
            if (File.Exists(Path.Combine(packed, "server.py"))) return Path.GetFullPath(packed);
            return Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "tts-server"));
        }
    }

    public static async Task EnsureStartedAsync()
    {
        if (await IsUpAsync()) return;
        try
        {
            var dir = Dir;
            var srv = Path.Combine(dir, "server.py");
            // 打包后：python\python.exe；开发期：venv\Scripts\python.exe
            var py = Path.Combine(dir, "python", "python.exe");
            if (!File.Exists(py)) py = Path.Combine(dir, "venv", "Scripts", "python.exe");
            if (!File.Exists(py) || !File.Exists(srv)) return;
            _proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = py,
                    Arguments = "server.py",
                    WorkingDirectory = dir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            // 不继承代理，保证 127.0.0.1 直连
            _proc.StartInfo.EnvironmentVariables["HTTP_PROXY"] = "";
            _proc.StartInfo.EnvironmentVariables["HTTPS_PROXY"] = "";
            _proc.OutputDataReceived += (_, e) => { if (e.Data != null) TtsService.Log("server: " + e.Data); };
            _proc.ErrorDataReceived += (_, e) => { if (e.Data != null) TtsService.Log("server: " + e.Data); };
            _proc.Start();
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
            // 等待模型加载（首次约 10-20 秒）
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(1000);
                if (await IsUpAsync()) return;
                if (_proc.HasExited) return;
            }
        }
        catch { }
    }

    public static void Stop()
    {
        try
        {
            if (_proc is { HasExited: false })
            {
                _proc.Kill(entireProcessTree: true);
                _proc.WaitForExit(3000);
            }
            _proc?.Dispose();
            _proc = null;
        }
        catch { }
    }

    private static async Task<bool> IsUpAsync()
    {
        try { var r = await _http.GetAsync("http://127.0.0.1:8123/voices"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }
}
