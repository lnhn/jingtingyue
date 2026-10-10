using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace JingTingYue.Services;

/// <summary>A bounded window of audio, with one request per sentence and two synthesis slots.</summary>
internal sealed class SentenceAudioBuffer : IDisposable
{
    internal const int StartupCount = 5;
    internal const int Capacity = 10;
    private readonly int _sentenceCount;
    private readonly Func<int, CancellationToken, Task<byte[]>> _fetch;
    private readonly Dictionary<int, Task<byte[]>> _requests = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _slots = new(2, 2);
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationToken _token;
    private bool _disposed;

    internal SentenceAudioBuffer(int sentenceCount, Func<int, CancellationToken, Task<byte[]>> fetch)
    {
        _sentenceCount = sentenceCount;
        _fetch = fetch;
        _token = _stop.Token;
    }

    internal void Fill(int from)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (int i in _requests.Keys.Where(i => i < from).ToArray())
                _requests.Remove(i);
            for (int i = from; i < Math.Min(_sentenceCount, from + Capacity); i++)
                GetOrCreate(i);
        }
    }

    internal bool IsReady(int index)
    {
        lock (_gate)
            return _requests.TryGetValue(index, out var request) && request.IsCompleted;
    }

    internal Task<byte[]> GetAsync(int index)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetOrCreate(index);
        }
    }

    internal async Task PrepareAsync(int from, Action<int, int>? progress = null)
    {
        Fill(from);
        int count = Math.Min(StartupCount, _sentenceCount - from);
        var pending = Enumerable.Range(from, count).Select(GetAsync).ToList();
        progress?.Invoke(0, count);
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            try { await completed; }
            catch (OperationCanceledException) when (_token.IsCancellationRequested) { throw; }
            catch { /* Playback skips individual synthesis failures. */ }
            _token.ThrowIfCancellationRequested();
            progress?.Invoke(count - pending.Count, count);
        }
    }

    private Task<byte[]> GetOrCreate(int index)
    {
        if (index < 0 || index >= _sentenceCount) throw new ArgumentOutOfRangeException(nameof(index));
        if (!_requests.TryGetValue(index, out var request))
        {
            _requests[index] = request = GenerateAsync(index, _token);
            // Observe failures even if the user stops before playback reaches this sentence.
            _ = request.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        return request;
    }

    private async Task<byte[]> GenerateAsync(int index, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _fetch(index, cancellationToken);
        }
        finally { _slots.Release(); }
    }

    public void Dispose()
    {
        Task<byte[]>[] outstanding;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            outstanding = _requests.Values.ToArray();
            _requests.Clear();
        }
        // Wait for in-flight requests to release their slots before disposing the semaphore.
        _ = Task.WhenAll(outstanding).ContinueWith(t =>
        {
            _ = t.Exception;
            _slots.Dispose();
            _stop.Dispose();
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }
}
