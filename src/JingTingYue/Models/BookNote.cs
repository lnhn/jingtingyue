namespace JingTingYue.Models;

/// <summary>一条笔记：选中原文 + 个人想法，定位到某书某章某字符区间。</summary>
public sealed class BookNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string BookId { get; set; } = "";
    public int ChapterIndex { get; set; }
    public int CharStart { get; set; }
    public int CharEnd { get; set; }
    public string SelectedText { get; set; } = "";
    public string NoteText { get; set; } = "";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
