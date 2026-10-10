using System.Collections.Concurrent;
using JingTingYue.Services;

// Windows-only integration test: requires the local TTS server and plays sample audio.
var tts = new TtsService();
var started = new ConcurrentQueue<(int Phase, int Index)>();
var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int phase = 1;
int initialBuffers = 0;
tts.Buffering += (done, total) =>
{
    Console.WriteLine($"buffer {done}/{total}");
    if (phase == 1 && done == 0) Interlocked.Increment(ref initialBuffers);
    if (done == total) prepared.TrySetResult();
};
tts.SentenceStart += index =>
{
    started.Enqueue((phase, index));
    Console.WriteLine($"start phase={phase} sentence={index}");
};
tts.Finished += () => finished.TrySetResult();
tts.Error += error => Console.WriteLine("error: " + error);

var text = new[]
{
    "从前有座山，山里有座庙。", "庙里有个老和尚，正在给小和尚讲故事。",
    "窗外的风轻轻吹过，故事又开始了。", "天色渐渐暗下来，远处亮起了灯。",
    "她翻开书，继续读下一页。", "雨停了，阳光照进窗户。",
    "树叶落在安静的小路上。", "我们坐下来，继续听故事。",
    "河水缓缓流过村庄。", "这是今天的第十句话。", "故事还在继续。", "最后一页也读完了。"
};
var sentences = text.Select(s => new SpokenSentence(s, 0, s.Length)).ToArray();
if (args.Contains("--mixed"))
{
    text = new[]
    {
        "这是 Hello world 的意思。", "我们使用 Python 编程。",
        "这项研究使用 AI 技术。", "请阅读 machine learning 这一段。",
        "这台电脑运行 Windows 11 系统。", "这里是 Hello world，请继续。",
        "她正在学习 English。", "这本书介绍 Internet 的发展。",
        "请把文件保存为 PDF 格式。", "我们继续讨论 artificial intelligence。",
        "今天的任务已经 done。", "最后说一句 Thank you。"
    };
    sentences = text.Select(s => new SpokenSentence(s, 0, s.Length)).ToArray();
}
if (args.Contains("--punctuation"))
{
    text = new[]
    {
        "风来疏竹，风过而竹不留声；", "雁度寒潭，雁去而潭不留影。",
        "我们使用 Python，继续学习 AI；", "请稍等，准备好了吗？",
        "她翻开书，继续读下一页。", "她说：“Hello world，请继续。”",
        "雨停了，阳光照进窗户；", "树叶落下来，小路很安静。",
        "听到这里，请休息片刻；", "河水缓缓流过，村庄亮起了灯。",
        "故事还在继续，请接着听；", "最后说一句，Thank you。"
    };
    sentences = text.Select(s => new SpokenSentence(s, 0, s.Length)).ToArray();
}
try
{
    tts.Start(sentences, 0);
    tts.Pause();
    await prepared.Task.WaitAsync(TimeSpan.FromMinutes(3));
    await Task.Delay(500);
    if (!started.IsEmpty) throw new Exception("Playback started while paused during buffering.");
    tts.Resume();
    await finished.Task.WaitAsync(TimeSpan.FromMinutes(3));
    phase = 2;
    tts.Start(sentences, 0);
    tts.Stop();
    phase = 3;
    finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    tts.Start(new[] { new SpokenSentence("这是重新开始后的新一句。", 0, 13) }, 0);
    await finished.Task.WaitAsync(TimeSpan.FromMinutes(2));
    var heard = started.ToArray();
    var first = heard.Where(x => x.Phase == 1).Select(x => x.Index).ToArray();
    if (!first.SequenceEqual(Enumerable.Range(0, sentences.Length)))
        throw new Exception("Playback did not progress in order through all twelve sentences.");
    if (heard.Any(x => x.Phase == 2) || !heard.Where(x => x.Phase == 3).Select(x => x.Index).SequenceEqual(new[] { 0 }))
        throw new Exception("Stopped playback leaked into the restarted session.");
    Console.WriteLine($"PASS: all twelve sentences, startup pause, stop during synthesis/restart and MediaEnded completion. " +
                      $"Mid-playback rebuffer events: {initialBuffers - 1}.");
}
finally { tts.Stop(); }
