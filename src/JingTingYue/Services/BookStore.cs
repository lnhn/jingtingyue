using System.Text.Json;
using JingTingYue.Models;

namespace JingTingYue.Services;

/// <summary>
/// 应用级书单。真实导入的书持久化到本地：
///   %LOCALAPPDATA%\JingTingYue\books.json          —— 书单与阅读位置
///   %LOCALAPPDATA%\JingTingYue\epubs\{id}.epub     —— 书籍本体副本
/// 三本示例书仅内存展示，不落盘。
/// </summary>
public static class BookStore
{
    private static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JingTingYue");
    private static readonly string BooksFile = Path.Combine(Root, "books.json");
    private static readonly string EpubDir = Path.Combine(Root, "epubs");

    private static readonly string[] Palette = { "#3A4A63", "#2F5D4B", "#9E4F35" };

    /// <summary>书架显示用的完整列表（示例书 + 持久化的真实书，真实书在前）。</summary>
    public static List<StoredBook> Books { get; } = new();

    public static bool Loaded { get; private set; }

    public static async Task LoadAsync()
    {
        if (Loaded) return;
        Directory.CreateDirectory(EpubDir);

        List<StoredBook> stored = new();
        if (File.Exists(BooksFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(BooksFile);
                stored = JsonSerializer.Deserialize<List<StoredBook>>(json) ?? new();
            }
            catch { stored = new(); } // 损坏文件不崩，下面会重新生成
        }

        // 重新解析每本真实书（0.9MB 量级很快）
        foreach (var b in stored)
        {
            var path = Path.Combine(EpubDir, b.EpubFile);
            if (!File.Exists(path)) continue;
            try { b.Book = EpubParser.Parse(path); } catch { b.Book = null; }
        }

        Books.Clear();
        Books.AddRange(stored);      // 只保留真实导入的书
        Loaded = true;
    }

    private static async Task SaveAsync()
    {
        var real = Books.Where(b => !b.Id.StartsWith("sample-")).ToList();
        var json = JsonSerializer.Serialize(real, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(BooksFile, json);
    }

    public static async Task<StoredBook> AddImportedAsync(string sourceEpubPath, EpubBook parsed)
    {
        var id = Guid.NewGuid().ToString("N");
        var fileName = id + ".epub";
        Directory.CreateDirectory(EpubDir);
        File.Copy(sourceEpubPath, Path.Combine(EpubDir, fileName), overwrite: true);

        var hex = Palette[Books.Count % Palette.Length];
        var book = new StoredBook
        {
            Id = id,
            Title = parsed.Title,
            Author = parsed.Author,
            CoverHex = hex,
            EpubFile = fileName,
            Book = parsed,
        };
        Books.Insert(0, book);
        await SaveAsync();
        return book;
    }

    /// <summary>更新某本书的阅读位置与进度（节流由调用方控制）。</summary>
    public static Task UpdateProgressAsync(string id, int lastChapter, int lastPage, int progress)
    {
        var b = Books.FirstOrDefault(x => x.Id == id);
        if (b == null || b.Id.StartsWith("sample-")) return Task.CompletedTask;
        b.LastChapter = lastChapter;
        b.LastPage = lastPage;
        b.Progress = progress;
        return SaveAsync();
    }

    /// <summary>保存某本书的阅读设置（字体/字号/主题/音色/语速）。</summary>
    public static Task SaveSettingsAsync(string id, string? fontFamily = null, int? fontSize = null,
        string? theme = null, string? voiceId = null, double? rate = null)
    {
        var b = Books.FirstOrDefault(x => x.Id == id);
        if (b == null || b.Id.StartsWith("sample-")) return Task.CompletedTask;
        if (fontFamily != null) b.FontFamily = fontFamily;
        if (fontSize != null) b.FontSize = fontSize.Value;
        if (theme != null) b.Theme = theme;
        if (voiceId != null) b.VoiceId = voiceId;
        if (rate != null) b.Rate = rate.Value;
        return SaveAsync();
    }

    /// <summary>最近在读的真实书（用于“继续阅读”）。</summary>
    public static StoredBook? LastRead =>
        Books.Where(b => !b.Id.StartsWith("sample-")).OrderByDescending(b => b.Progress).FirstOrDefault();

    /// <summary>所有已使用的分类名（去重、非空）。</summary>
    public static List<string> Categories =>
        Books.Where(b => !b.Id.StartsWith("sample-") && !string.IsNullOrWhiteSpace(b.Category))
             .Select(b => b.Category).Distinct().OrderBy(c => c).ToList();

    /// <summary>删除一本真实书及其 epub 文件（示例书不可删）。</summary>
    public static async Task DeleteAsync(string id)
    {
        var b = Books.FirstOrDefault(x => x.Id == id);
        if (b == null || b.Id.StartsWith("sample-")) return;
        try { var ep = Path.Combine(EpubDir, b.EpubFile); if (File.Exists(ep)) File.Delete(ep); } catch { }
        Books.Remove(b);
        await SaveAsync();
    }

    /// <summary>把一本书移动到指定分类（空串表示未分类）。</summary>
    public static async Task MoveToCategoryAsync(string id, string category)
    {
        var b = Books.FirstOrDefault(x => x.Id == id);
        if (b == null || b.Id.StartsWith("sample-")) return;
        b.Category = category ?? "";
        await SaveAsync();
    }
}
