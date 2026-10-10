using System.Collections.Concurrent;
using JingTingYue.Services;

await StartupAndContinuousRefill();
await StopCancelsQueuedWork();
await FailuresAndShortChapters();
Console.WriteLine("PASS: startup barrier, refill beyond 10 sentences, deduplication, two-request limit, cancellation, failure isolation and chapter end.");

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static async Task StartupAndContinuousRefill()
{
    var synth = new ControlledSynth();
    using var buffer = new SentenceAudioBuffer(30, synth.FetchAsync);
    var progress = new ConcurrentQueue<(int, int)>();
    var prepared = buffer.PrepareAsync(0, (done, total) => progress.Enqueue((done, total)));
    Check(synth.Calls.Count == 2, "Exactly two syntheses should start concurrently.");
    Check(ReferenceEquals(buffer.GetAsync(2), buffer.GetAsync(2)), "Pending requests must be deduplicated.");
    synth.Complete(0);
    await buffer.GetAsync(0).WaitAsync(TimeSpan.FromSeconds(2));
    Check(!prepared.IsCompleted, "Playback must not start after only one sentence is ready.");
    for (int i = 1; i < 5; i++) synth.Complete(i);
    await prepared.WaitAsync(TimeSpan.FromSeconds(2));
    Check(progress.Last() == (5, 5), "Five prepared sentences should open the startup barrier.");
    Check(!synth.Calls.ContainsKey(10), "The initial window must not submit an entire chapter.");
    for (int i = 5; i < 15; i++) synth.Complete(i);
    for (int i = 0; i < 15; i++)
    {
        buffer.Fill(i);
        var audio = await buffer.GetAsync(i).WaitAsync(TimeSpan.FromSeconds(2));
        Check(audio[0] == i, "Audio must stay in sentence order despite concurrent synthesis.");
    }
    Check(synth.Calls.ContainsKey(14), "The producer must refill past both sentence 5 and sentence 10.");
    Check(synth.Calls.Values.All(n => n == 1), "A sentence was synthesized more than once.");
    Check(synth.MaxActive == 2, "The concurrency limit was exceeded.");
}

static async Task StopCancelsQueuedWork()
{
    var oldSynth = new ControlledSynth();
    var oldBuffer = new SentenceAudioBuffer(20, oldSynth.FetchAsync);
    var preparing = oldBuffer.PrepareAsync(0);
    oldBuffer.Dispose();
    try
    {
        await preparing.WaitAsync(TimeSpan.FromSeconds(2));
        throw new Exception("Stopping must cancel preparation.");
    }
    catch (OperationCanceledException) { }
    Check(!oldSynth.Calls.ContainsKey(2), "Canceled queued sentences must not reach synthesis.");
    using var newBuffer = new SentenceAudioBuffer(1, (_, _) => Task.FromResult(new byte[] { 99 }));
    await newBuffer.PrepareAsync(0);
    Check((await newBuffer.GetAsync(0))[0] == 99, "A stopped session must not supply the next session's audio.");
}

static async Task FailuresAndShortChapters()
{
    using var buffer = new SentenceAudioBuffer(6, (i, _) => i == 0
        ? Task.FromException<byte[]>(new IOException("one synthesis failed"))
        : Task.FromResult(new byte[] { (byte)i }));
    await buffer.PrepareAsync(0);
    try
    {
        await buffer.GetAsync(0);
        throw new Exception("Failed synthesis should remain visible to playback.");
    }
    catch (IOException) { }
    Check((await buffer.GetAsync(1))[0] == 1, "One failure must not stop subsequent sentences.");
    using var shortChapter = new SentenceAudioBuffer(3, (i, _) => Task.FromResult(new byte[] { (byte)i }));
    var reported = (0, 0);
    await shortChapter.PrepareAsync(2, (done, total) => reported = (done, total));
    Check(reported == (1, 1), "Near the chapter end, preparation must not wait for nonexistent sentences.");
}

sealed class ControlledSynth
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _gates = new();
    internal readonly ConcurrentDictionary<int, int> Calls = new();
    private int _active;
    internal int MaxActive;

    internal void Complete(int index) => Gate(index).TrySetResult();
    private TaskCompletionSource Gate(int index) => _gates.GetOrAdd(index,
        _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

    internal async Task<byte[]> FetchAsync(int index, CancellationToken token)
    {
        Calls.AddOrUpdate(index, 1, (_, count) => count + 1);
        int active = Interlocked.Increment(ref _active);
        int max;
        do { max = MaxActive; }
        while (active > max && Interlocked.CompareExchange(ref MaxActive, active, max) != max);
        try
        {
            await Gate(index).Task.WaitAsync(token);
            return new byte[] { (byte)index };
        }
        finally { Interlocked.Decrement(ref _active); }
    }
}
