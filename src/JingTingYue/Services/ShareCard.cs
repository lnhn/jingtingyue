using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace JingTingYue.Services;

/// <summary>
/// 把一条笔记渲染成竖版分享卡片 PNG（至少 1920x2560，长内容自动增高）。
/// 用隐藏 WebView2 渲染 HTML，再 CapturePreview 截图。
/// </summary>
public static class ShareCard
{
    /// <summary>把渲染好的 WebView2 内容捕获为 PNG，返回保存路径。</summary>
    public static async Task<string> CaptureToPngAsync(WebView2 web)
    {
        await Task.Delay(200);
        var outPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            $"静听阅_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        using (var stream = new InMemoryRandomAccessStream())
        {
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            stream.Seek(0);
            using var outStream = new FileStream(outPath, FileMode.Create, FileAccess.Write);
            await stream.AsStreamForRead().CopyToAsync(outStream);
        }
        return outPath;
    }

    public static async Task LoadCardAsync(WebView2 web, string quote, string thought, string bookTitle, string author)
    {
        await web.EnsureCoreWebView2Async();
        web.NavigateToString(BuildHtml(quote, thought, bookTitle, author));
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(100);
            var h = await web.CoreWebView2.ExecuteScriptAsync("document.body.scrollHeight");
            if (!string.IsNullOrWhiteSpace(h) && h.Trim('"') != "0") break;
        }
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\n", "<br/>");

    private static string BuildHtml(string quote, string thought, string book, string author)
    {
        return $@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
  * {{ margin:0; box-sizing:border-box; }}
  body {{ width:1080px; font-family:'KaiTi','STKaiti',serif;
          background:linear-gradient(160deg,#F7F4EC 0%,#EFE9D9 100%); padding:90px 80px; }}
  .card {{ min-height:1400px; display:flex; flex-direction:column; }}
  .qmark {{ font-size:120px; color:#B56A4E; line-height:0.6; font-family:Georgia; }}
  .quote {{ font-size:46px; line-height:1.9; color:#2A2825; margin:30px 0 50px; }}
  .thought {{ font-size:34px; line-height:1.8; color:#4a5d52;
              border-left:6px solid #2F5D4B; padding-left:28px; margin-bottom:auto; }}
  .foot {{ margin-top:60px; padding-top:34px; border-top:1px solid #d8d0bd;
           display:flex; justify-content:space-between; align-items:flex-end; }}
  .book {{ font-size:30px; color:#2A2825; }}
  .author {{ font-size:24px; color:#8a8274; margin-top:6px; }}
  .brand {{ font-size:24px; color:#2F5D4B; letter-spacing:4px; }}
</style></head><body><div class='card'>
  <div class='qmark'>&ldquo;</div>
  <div class='quote'>{Esc(quote)}</div>
  {(string.IsNullOrWhiteSpace(thought) ? "" : $"<div class='thought'>{Esc(thought)}</div>")}
  <div class='foot'>
    <div><div class='book'>《{Esc(book)}》</div><div class='author'>{Esc(author)}</div></div>
    <div class='brand'>静听阅</div>
  </div>
</div></body></html>";
    }
}
