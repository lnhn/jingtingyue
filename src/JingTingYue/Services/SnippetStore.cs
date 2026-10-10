using System.IO;
using System.Text.Json;
using JingTingYue.Models;

namespace JingTingYue.Services;

/// <summary>
/// 粘贴文字片段持久化：DataPaths.Root\snippets.json。
/// 独立于书库，损坏文件回退为空列表。
/// </summary>
public static class SnippetStore
{
    private static readonly string Root = DataPaths.Root;
    private static readonly string SnippetsFile = Path.Combine(Root, "snippets.json");

    public static List<Snippet> Snippets { get; } = new();
    private static bool _loaded;

    public static async Task LoadAsync()
    {
        if (_loaded) return;
        if (File.Exists(SnippetsFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(SnippetsFile);
                var list = JsonSerializer.Deserialize<List<Snippet>>(json);
                if (list != null) Snippets.AddRange(list);
            }
            catch { /* 损坏则空库，下面会重建 */ }
        }
        _loaded = true;
    }

    private static async Task SaveAsync()
    {
        Directory.CreateDirectory(Root);
        var json = JsonSerializer.Serialize(Snippets, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(SnippetsFile, json);
    }

    public static List<Snippet> AllOrdered() =>
        Snippets.OrderByDescending(s => s.UpdatedAt).ToList();

    public static async Task<Snippet> AddAsync(Snippet s)
    {
        Snippets.Add(s);
        await SaveAsync();
        return s;
    }

    public static async Task UpdateAsync(Snippet s)
    {
        s.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await SaveAsync();
    }

    public static async Task DeleteAsync(string id)
    {
        Snippets.RemoveAll(s => s.Id == id);
        await SaveAsync();
    }
}
