using JingTingYue.Models;

namespace JingTingYue.Models;

/// <summary>打开阅读页时传入的数据。</summary>
public class ReaderRequest
{
    public StoredBook? Book { get; init; }
    /// <summary>无内容的示例书标题/作者。</summary>
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
}
