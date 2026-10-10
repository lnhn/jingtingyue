using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace JingTingYue.Services;

/// <summary>朗读到的一句。</summary>
public record SpokenSentence(string Text, int CharStart, int CharEnd);

/// <summary>本机 Kokoro 神经 TTS 的可用音色。</summary>
public sealed record VoiceInfo(string Id, string Name);

/// <summary>
/// 本地神经 TTS：逐句 POST 到本机 Kokoro 服务（127.0.0.1:8123），拿回 WAV 播放。
/// 持续预取后续句子，每句只提交一次请求。
/// </summary>
public sealed class TtsService
{
    private const string Base = "http://127.0.0.1:8123";
    private static readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(90) };

    private readonly MediaPlayer _player = new();
    private IReadOnlyList<SpokenSentence> _sentences = new List<SpokenSentence>();
    private int _index;
    private bool _playing;
    private bool _paused;
    private bool _clipActive;
    private byte[]? _pendingWav;
    private string _voice = "zf_xiaoxiao";
    private double _rate = 1.0;
    private Timer? _poll;
    private bool _sawPlaying;
    private long _clipStartedAt;
    private InMemoryRandomAccessStream? _keepAlive;
    private Microsoft.UI.Dispatching.DispatcherQueue? _disp;
    private int _generation;
    private int _consecutiveFailures;
    private SentenceAudioBuffer? _buffer;
    private bool _needsBuffer;
    private MediaSource? _activeSource;
    private Windows.Foundation.TypedEventHandler<MediaPlayer, object>? _endedHandler;
    private Windows.Foundation.TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? _failedHandler;

    public event Action<int>? SentenceStart;
    public event Action? Finished;
    public event Action<string>? Error;
    public event Action? Generating;  // 正在合成音频（等待服务器返回）
    public event Action<int, int>? Buffering;

    internal static void Log(string msg)
    {
        try { File.AppendAllText(Path.Combine(
            DataPaths.Root, "tts.log"),
            $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    public bool IsPlaying => _playing;
    public bool IsBuffering => _playing && !_clipActive && _pendingWav == null;

    public static async Task<List<VoiceInfo>> ChineseVoicesAsync()
    {
        try
        {
            var json = await _http.GetStringAsync($"{Base}/voices");
            using var d = JsonDocument.Parse(json);
            var list = new List<VoiceInfo>();
            foreach (var v in d.RootElement.GetProperty("voices").EnumerateArray())
            {
                var code = v.GetProperty("name").GetString() ?? "";
                list.Add(new VoiceInfo(code, FriendlyName(code)));
            }
            return list;
        }
        catch { return new(); }
    }

    private static string FriendlyName(string code) => code switch
    {
        "zf_xiaoxiao" => "女声 · 晓晓",
        "zf_xiaobei" => "女声 · 小北",
        "zm_yunjian" => "男声 · 云健",
        "zm_yunxi" => "男声 · 云希",
        "zm_yunyang" => "男声 · 云扬",
        _ => code,
    };

    public void SelectVoice(string voiceId)
    {
        if (_voice == voiceId) return;
        _voice = voiceId;
        RefreshSettingsBuffer();
    }

    public void SetRate(double rate)
    {
        rate = Math.Clamp(rate, 0.6, 1.8);
        if (_rate == rate) return;
        _rate = rate;
        RefreshSettingsBuffer();
    }

    private void RefreshSettingsBuffer()
    {
        if (!_playing) return;
        if (!_clipActive) _generation++;
        _pendingWav = null;
        _consecutiveFailures = 0;
        CreateBuffer();
        if (_clipActive) _buffer!.Fill(_index + 1);
        else PlayCurrent();
    }

    public void Start(IReadOnlyList<SpokenSentence> sentences, int startIndex)
    {
        _disp = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Stop();
        if (sentences.Count == 0) { Finished?.Invoke(); return; }
        _sentences = sentences.ToArray();
        _index = Math.Clamp(startIndex, 0, sentences.Count - 1);
        _playing = true;
        CreateBuffer();
        PlayCurrent();
    }

    private void CreateBuffer()
    {
        _buffer?.Dispose();
        var sentences = _sentences;
        string voice = _voice;
        double rate = _rate;
        _buffer = new SentenceAudioBuffer(sentences.Count,
            (i, token) => FetchAsync(sentences[i].Text, i, voice, rate, token));
        _needsBuffer = true;
    }

    private async void PlayCurrent()
    {
        if (!_playing || _index >= _sentences.Count)
        {
            StopInternal(); Finished?.Invoke(); return;
        }
        int generation = _generation;
        int index = _index;
        try
        {
            // 连续失败时尝试重启 TTS 服务
            if (_consecutiveFailures == 3)
            {
                Log($"consecutive failures={_consecutiveFailures}, restarting server...");
                Error?.Invoke("语音服务断开，正在重连…");
                _buffer?.Dispose();
                TtsServer.Stop();
                await TtsServer.EnsureStartedAsync();
                if (!_playing || generation != _generation) return;
                CreateBuffer();
            }

            var buffer = _buffer!;
            buffer.Fill(index);
            if (_needsBuffer || !buffer.IsReady(index))
            {
                Generating?.Invoke();
                Log($"buffering from #{index}");
                await buffer.PrepareAsync(index, (done, total) =>
                {
                    if (_playing && generation == _generation) Buffering?.Invoke(done, total);
                });
                if (!_playing || generation != _generation || index != _index) return;
                _needsBuffer = false;
                Log($"buffer ready from #{index}");
            }
            byte[] wav = await buffer.GetAsync(index);
            if (!_playing || generation != _generation || index != _index) return;
            _consecutiveFailures = 0;
            Log($"got wav {wav.Length}B for #{_index}");
            Dispatch(() =>
            {
                if (!_playing || generation != _generation || index != _index) return;
                if (_paused) _pendingWav = wav;
                else StartPlayback(wav);
            });
        }
        catch (Exception ex)
        {
            if (!_playing || generation != _generation || index != _index) return;
            _consecutiveFailures++;
            Log($"skip sentence #{_index} (fail #{_consecutiveFailures}) : " + ex.Message);
            // 超过上限就停播，让用户知道出问题了
            if (_consecutiveFailures >= 5)
            {
                Log("too many failures, stopping playback");
                StopInternal();
                Error?.Invoke("语音服务无响应，已停止朗读。请检查后重试。");
                Finished?.Invoke();
                return;
            }
            _index++;
            PlayCurrent();
        }
    }

    private async Task<byte[]> FetchAsync(string text, int i, string voice, double rate, CancellationToken cancellationToken)
    {
        Log($"fetching #{i} len={text.Length}");
        var elapsed = Stopwatch.StartNew();
        var payload = JsonSerializer.Serialize(new
        {
            text = text, voice = voice, speed = rate,
        });
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var resp = await _http.PostAsync($"{Base}/tts",
                    content, budget.Token);
                resp.EnsureSuccessStatusCode();
                var wav = await resp.Content.ReadAsByteArrayAsync(budget.Token);
                Log($"generated #{i} in {elapsed.Elapsed.TotalSeconds:F1}s ({wav.Length}B)");
                return wav;
            }
            catch (Exception ex)
            {
                Log($"fetch #{i} attempt {attempt} failed: {ex.Message}");
                if (attempt == 2 || budget.IsCancellationRequested) throw;
                await Task.Delay(600, budget.Token);
            }
        }
        throw new InvalidOperationException("unreachable");
    }

    private void StartPlayback(byte[] wav)
    {
        if (!_playing || _paused) return;
        try
        {
            ReleaseClip();
            var stream = new InMemoryRandomAccessStream();
            var dw = new DataWriter(stream);
            dw.WriteBytes(wav);
            dw.StoreAsync().GetResults();
            dw.DetachStream();
            dw.Dispose();
            stream.Seek(0);
            _keepAlive = stream;
            _sawPlaying = false;
            _clipStartedAt = Environment.TickCount64;
            int generation = _generation;
            int index = _index;
            var source = MediaSource.CreateFromStream(stream, "audio/wav");
            _activeSource = source;
            _endedHandler = (_, _) => Dispatch(() =>
            {
                if (generation == _generation && index == _index && ReferenceEquals(source, _activeSource))
                    Advance();
            });
            _failedHandler = (_, args) => Dispatch(() =>
            {
                if (generation != _generation || index != _index || !ReferenceEquals(source, _activeSource)) return;
                Log($"playback failed #{index}: {args.ErrorMessage}");
                Advance();
            });
            _player.MediaEnded += _endedHandler;
            _player.MediaFailed += _failedHandler;
            _player.Source = source;
            _clipActive = true;
            _player.Play();
            Log($"playback start #{_index}");
            _buffer!.Fill(_index + 1);
            StartPoll();
            SentenceStart?.Invoke(_index);
        }
        catch (Exception ex)
        {
            Log("PlayEx: " + ex.Message);
            ReleaseClip();
            _index++;
            PlayCurrent();
        }
    }

    private void StartPoll()
    {
        _poll?.Dispose();
        _poll = new Timer(_ =>
        {
            if (_disp != null) _disp.TryEnqueue(Tick);
            else Tick();
        }, null, 250, 250);
    }

    private void Tick()
    {
        if (!_playing || !_clipActive || _paused) return;
        MediaPlaybackState st; double pos = 0, dur = 0;
        try
        {
            var s = _player.PlaybackSession;
            st = s.PlaybackState; pos = s.Position.TotalSeconds; dur = s.NaturalDuration.TotalSeconds;
        }
        catch
        {
            if (Environment.TickCount64 - _clipStartedAt > 5000) Advance();
            return;
        }

        if (st == MediaPlaybackState.Playing) _sawPlaying = true;
        bool ended = false;
        // 只有真正拿到 duration 后才检测结束，避免刚加载时 dur=0 误判
        if (_sawPlaying && dur > 0 && (pos >= dur || st == MediaPlaybackState.None)) ended = true;
        // 兜底：请求超时或服务无响应时，8 秒推进避免卡死
        if (!_sawPlaying && Environment.TickCount64 - _clipStartedAt > 8000) ended = true;
        if (_sawPlaying && dur > 0 && st != MediaPlaybackState.Playing && st != MediaPlaybackState.Paused &&
            Environment.TickCount64 - _clipStartedAt > Math.Max(15000, (dur + 8) * 1000)) ended = true;

        if (ended) Advance();
    }

    private void Dispatch(Action action)
    {
        if (_disp != null && !_disp.HasThreadAccess) _disp.TryEnqueue(() => action());
        else action();
    }

    private void Advance()
    {
        if (!_playing || !_clipActive) return;
        Log($"playback end #{_index}");
        ReleaseClip();
        _index++;
        PlayCurrent();
    }

    public void Pause()
    {
        _paused = true;
        try { _player.Pause(); } catch { }
    }

    public void Resume()
    {
        _paused = false;
        if (_pendingWav != null)
        {
            var wav = _pendingWav;
            _pendingWav = null;
            StartPlayback(wav);
        }
        else if (_clipActive) { try { _player.Play(); } catch { } }
    }

    public void Stop()
    {
        _generation++;
        _playing = false;
        _consecutiveFailures = 0;
        StopInternal();
    }

    private void StopInternal()
    {
        _playing = false;
        _paused = false;
        _pendingWav = null;
        ReleaseClip();
        _buffer?.Dispose();
        _buffer = null;
    }

    private void ReleaseClip()
    {
        _clipActive = false;
        _poll?.Dispose();
        _poll = null;
        if (_endedHandler != null) _player.MediaEnded -= _endedHandler;
        if (_failedHandler != null) _player.MediaFailed -= _failedHandler;
        _endedHandler = null;
        _failedHandler = null;
        try { _player.Pause(); _player.Source = null; } catch { }
        _activeSource?.Dispose();
        _activeSource = null;
        _keepAlive?.Dispose();
        _keepAlive = null;
    }
}
