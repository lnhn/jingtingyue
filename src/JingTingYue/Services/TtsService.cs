using System;
using System.Collections.Generic;
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
/// 只在当前句播完后才请求下一句，避免队列堆积。
/// </summary>
public sealed class TtsService
{
    private const string Base = "http://127.0.0.1:8123";
    private static readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(30) };

    private readonly MediaPlayer _player = new();
    private IReadOnlyList<SpokenSentence> _sentences = new List<SpokenSentence>();
    private int _index;
    private bool _playing;
    private string _voice = "zf_xiaoxiao";
    private double _rate = 1.0;
    private Timer? _poll;
    private bool _sawPlaying;
    private int _clipStartedAt;
    private InMemoryRandomAccessStream? _keepAlive;
    private Microsoft.UI.Dispatching.DispatcherQueue? _disp;

    public event Action<int>? SentenceStart;
    public event Action? Finished;
    public event Action<string>? Error;

    internal static void Log(string msg)
    {
        try { File.AppendAllText(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JingTingYue", "tts.log"),
            $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
    }

    public bool IsPlaying => _playing;

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

    public void SelectVoice(string voiceId) => _voice = voiceId;
    public void SetRate(double rate) => _rate = Math.Clamp(rate, 0.6, 1.8);

    public void Start(IReadOnlyList<SpokenSentence> sentences, int startIndex)
    {
        _disp = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Stop();
        _sentences = sentences;
        _index = Math.Clamp(startIndex, 0, sentences.Count - 1);
        _playing = true;
        PlayCurrent();
    }

    private async void PlayCurrent()
    {
        if (!_playing || _index >= _sentences.Count)
        {
            StopInternal(); Finished?.Invoke(); return;
        }
        try
        {
            byte[] wav;
            if (_cache.TryGetValue(_index, out var cached) && cached != null)
                wav = cached;
            else
                wav = await FetchAsync(_index);
            if (!_playing) return;
            Log($"got wav {wav.Length}B for #{_index}");
            // 预取后面两句，消除句间空档
            Prefetch(_index + 1);
            Prefetch(_index + 2);

            if (_disp != null) _disp.TryEnqueue(() => StartPlayback(wav));
            else StartPlayback(wav);
        }
        catch (Exception ex)
        {
            if (!_playing) return;
            Log("EX: " + ex.Message);
            Error?.Invoke(ex.Message);
            StopInternal(); Finished?.Invoke();
        }
    }

    private readonly Dictionary<int, byte[]> _cache = new();

    private async Task<byte[]> FetchAsync(int i)
    {
        var payload = JsonSerializer.Serialize(new
        {
            text = _sentences[i].Text, voice = _voice, speed = _rate,
        });
        var resp = await _http.PostAsync($"{Base}/tts",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }

    private async void Prefetch(int i)
    {
        if (i < 0 || i >= _sentences.Count || _cache.ContainsKey(i)) return;
        try { _cache[i] = await FetchAsync(i); }
        catch { /* 预取失败，播放时再拉 */ }
    }

    private void StartPlayback(byte[] wav)
    {
        if (!_playing) return;
        try
        {
            var stream = new InMemoryRandomAccessStream();
            var dw = new DataWriter(stream);
            dw.WriteBytes(wav);
            dw.StoreAsync().GetResults();
            dw.DetachStream();
            dw.Dispose();
            stream.Seek(0);
            _keepAlive = stream;
            SentenceStart?.Invoke(_index);
            _sawPlaying = false;
            _clipStartedAt = Environment.TickCount;
            _player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
            _player.Play();
            StartPoll();
        }
        catch (Exception ex) { Log("PlayEx: " + ex.Message); }
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
        if (!_playing) return;
        MediaPlaybackState st; double pos = 0, dur = 0;
        try
        {
            var s = _player.PlaybackSession;
            st = s.PlaybackState; pos = s.Position.TotalSeconds; dur = s.NaturalDuration.TotalSeconds;
        }
        catch { return; }

        if (st == MediaPlaybackState.Playing) _sawPlaying = true;
        bool ended = false;
        if (_sawPlaying && (pos >= dur - 0.15 || st == MediaPlaybackState.None)) ended = true;
        // 兜底：请求超时或服务无响应时，8 秒推进避免卡死
        if (!_sawPlaying && Environment.TickCount - _clipStartedAt > 8000) ended = true;

        if (ended) { _poll?.Dispose(); _poll = null; _index++; PlayCurrent(); }
    }

    public void Pause() { try { if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) _player.Pause(); } catch { } }
    public void Resume() { try { if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Paused) _player.Play(); } catch { } }

    public void Stop() { _playing = false; StopInternal(); }

    private void StopInternal()
    {
        try { _poll?.Dispose(); _poll = null; _player.Pause(); _player.Source = null; } catch { }
        _cache.Clear();
    }
}
