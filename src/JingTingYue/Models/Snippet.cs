namespace JingTingYue.Models;

/// <summary>
/// 一段粘贴进来的文字：独立于书库保存，可直接朗读。
/// </summary>
public sealed class Snippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string VoiceId { get; set; } = "zf_xiaoxiao";
    public double Rate { get; set; } = 1.0;
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
