using System.Diagnostics;
using System.Text.Json;
using JingTingYue.Models;
using JingTingYue.Services;

// Standalone regression harness: dotnet run --project tests/BookStore.Regression
var reload = args.Length == 2 && args[0] == "--reload";
var root = reload ? args[1] : Path.Combine(Path.GetTempPath(), "JingTingYue-regression-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("JINGTINGYUE_DATA_DIR", root);
Directory.CreateDirectory(root);

void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine("PASS: " + message);
}

if (reload)
{
    await BookStore.LoadAsync();
    Check(BookStore.LastRead?.Id == "low", "Most recently opened book survives a process restart");
    Check(BookStore.LastRead?.LastReadAt.HasValue == true, "Reading timestamp survives a process restart");
    Check(BookStore.Books.Single(b => b.Id == "low").LastPage == 3, "Opening preserves the saved reading position");
    return;
}

try
{
    // Deliberately use old JSON with no timestamp, including a book saved in chapter 0.
    await File.WriteAllTextAsync(Path.Combine(root, "books.json"), """
        [
          {"id":"high","title":"Higher progress","progress":90,"lastChapter":9,"lastPage":4},
          {"id":"low","title":"Lower progress","progress":10,"lastChapter":1,"lastPage":3},
          {"id":"unread","title":"Unread","progress":0},
          {"id":"sample-demo","title":"Sample","progress":99}
        ]
        """);
    await BookStore.LoadAsync();
    Check(BookStore.LastRead?.Id == "high", "Old reading records remain available without timestamp migration");
    var books = BookStore.Books.ToList();
    BookStore.Books.Clear();
    BookStore.Books.Add(books.Single(b => b.Id == "unread"));
    Check(BookStore.LastRead == null, "Never-read books do not create a continue-reading card");
    BookStore.Books.Clear();
    BookStore.Books.AddRange(books);

    await BookStore.MarkReadAsync("low");
    Check(BookStore.LastRead?.Id == "low", "Recent reading beats higher progress");
    await BookStore.MarkReadAsync("unread");
    Check(BookStore.LastRead?.Id == "unread", "Opening a book at 0% counts without turning a page");
    var openedAt = BookStore.LastRead!.LastReadAt;
    await BookStore.SaveSettingsAsync("high", fontSize: 20);
    await BookStore.MoveToCategoryAsync("high", "Test");
    await BookStore.MarkReadAsync("sample-demo");
    await BookStore.MarkReadAsync("missing");
    Check(BookStore.LastRead?.Id == "unread" && BookStore.LastRead.LastReadAt == openedAt,
        "Settings, categories, samples and unknown IDs do not replace the recent book");
    await BookStore.UpdateProgressAsync("low", 1, 3, 10);
    Check(BookStore.LastRead?.Id == "low", "Reading activity updates recency even when the percentage is unchanged");

    await Task.WhenAll(Enumerable.Range(0, 20).Select(i => BookStore.SaveSettingsAsync("high", fontSize: i + 20)));
    var saved = JsonSerializer.Deserialize<List<StoredBook>>(await File.ReadAllTextAsync(Path.Combine(root, "books.json")))!;
    Check(saved.Single(b => b.Id == "high").FontSize == 39, "Overlapping saves preserve the latest state in valid JSON");
    await BookStore.DeleteAsync("low");
    Check(BookStore.LastRead?.Id == "unread", "Deleting the recent book falls back to the previous read book");
    BookStore.Books.Add(books.Single(b => b.Id == "low"));
    await BookStore.MarkReadAsync("low");

    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
    start.ArgumentList.Add("--reload");
    start.ArgumentList.Add(root);
    using var child = Process.Start(start)!;
    await child.WaitForExitAsync();
    Check(child.ExitCode == 0, "Reload checks passed in a fresh process");
}
finally
{
    Directory.Delete(root, recursive: true);
}
