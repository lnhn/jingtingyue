using System.Text.Json.Serialization;
using JingTingYue.Services;

namespace JingTingYue.Models;

/// <summary>落盘到 books.json 的一条书记录。EpubBook 仅在内存缓存，不序列化。</summary>
public class StoredBook
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("cover")] public string CoverHex { get; set; } = "#3A4A63";
    /// <summary>epubs 目录下的文件名。</summary>
    [JsonPropertyName("file")] public string EpubFile { get; set; } = "";
    [JsonPropertyName("coverFile")] public string CoverFile { get; set; } = "";
    [JsonPropertyName("lastChapter")] public int LastChapter { get; set; }
    [JsonPropertyName("lastPage")] public int LastPage { get; set; }
    [JsonPropertyName("progress")] public int Progress { get; set; }
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    // 每本书独立的阅读设置
    [JsonPropertyName("fontFamily")] public string FontFamily { get; set; } = "";
    [JsonPropertyName("fontSize")] public int FontSize { get; set; } = 0;
    [JsonPropertyName("theme")] public string Theme { get; set; } = "";
    [JsonPropertyName("voiceId")] public string VoiceId { get; set; } = "";
    [JsonPropertyName("rate")] public double Rate { get; set; } = 0;

    [JsonIgnore] public EpubBook? Book { get; set; }
}
