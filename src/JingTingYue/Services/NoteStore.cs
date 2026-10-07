using System.Text.Json;
using JingTingYue.Models;

namespace JingTingYue.Services;

/// <summary>
/// 笔记持久化：%LOCALAPPDATA%\JingTingYue\notes.json。
/// 损坏文件不崩，回退为空列表并重新生成（不静默覆盖已有数据到别处——第六阶段再加备份）。
/// </summary>
public static class NoteStore
{
    private static readonly string Root =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JingTingYue");
    private static readonly string NotesFile = Path.Combine(Root, "notes.json");

    public static List<BookNote> Notes { get; } = new();
    private static bool _loaded;

    public static async Task LoadAsync()
    {
        if (_loaded) return;
        if (File.Exists(NotesFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(NotesFile);
                var list = JsonSerializer.Deserialize<List<BookNote>>(json);
                if (list != null) Notes.AddRange(list);
            }
            catch { /* 损坏则空库，下面会重建 */ }
        }
        _loaded = true;
    }

    private static async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(Notes, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(NotesFile, json);
    }

    public static List<BookNote> ForBook(string bookId) =>
        Notes.Where(n => n.BookId == bookId).OrderBy(n => n.ChapterIndex).ThenBy(n => n.CharStart).ToList();

    public static async Task<BookNote> AddAsync(BookNote note)
    {
        Notes.Add(note);
        await SaveAsync();
        return note;
    }

    public static async Task UpdateAsync(BookNote note)
    {
        note.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await SaveAsync();
    }

    public static async Task DeleteAsync(string id)
    {
        Notes.RemoveAll(n => n.Id == id);
        await SaveAsync();
    }
}
