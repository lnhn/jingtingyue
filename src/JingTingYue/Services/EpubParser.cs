using System.IO.Compression;
using System.Xml.Linq;

namespace JingTingYue.Services;

public class EpubChapter
{
    public string Title { get; set; } = "";
    public string HtmlBody { get; set; } = "";
}

public class EpubBook
{
    public string FilePath { get; set; } = "";
    public string Title { get; set; } = "未命名";
    public string Author { get; set; } = "佚名";
    public List<EpubChapter> Chapters { get; set; } = new();
}

/// <summary>
/// 极简 EPUB 解析器：解包 → container.xml → OPF → spine 顺序 → 提取各章 body HTML。
/// 不依赖第三方库，只取正文文字与结构；图片/样式暂不处理（后续阶段再增强）。
/// </summary>
public static class EpubParser
{
    private static readonly XNamespace OpfNs = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace XhtmlNs = "http://www.w3.org/1999/xhtml";

    public static EpubBook Parse(string epubPath)
    {
        using var zip = ZipFile.OpenRead(epubPath);

        // 1. container.xml -> OPF 路径
        var containerEntry = zip.GetEntry("META-INF/container.xml")
            ?? throw new InvalidDataException("EPUB 缺少 META-INF/container.xml");
        var opfPath = ReadEntryText(zip, containerEntry);
        var doc = XDocument.Parse(opfPath);
        var opfFullPath = doc.Descendants()
            .Elements()
            .First(e => e.Name.LocalName == "rootfile")
            .Attribute("full-path")?.Value
            ?? throw new InvalidDataException("找不到 OPF 文件路径");

        int slash = opfFullPath.LastIndexOf('/');
        var opfDir = slash >= 0 ? opfFullPath.Substring(0, slash + 1) : "";

        // 2. OPF
        var opfEntry = zip.GetEntry(opfFullPath) ?? throw new InvalidDataException("OPF 文件缺失");
        var opf = XDocument.Parse(ReadEntryText(zip, opfEntry));

        var title = opf.Descendants(DcNs + "title").FirstOrDefault()?.Value.Trim() ?? "未命名";
        var author = opf.Descendants(DcNs + "creator").FirstOrDefault()?.Value.Trim() ?? "佚名";

        // manifest: id -> (href, media-type)
        var manifest = opf.Descendants(OpfNs + "item").ToDictionary(
            i => i.Attribute("id")?.Value ?? "",
            i => (href: i.Attribute("href")?.Value ?? "",
                  mt: i.Attribute("media-type")?.Value ?? ""));

        // spine: 有序 idref
        var spineRefs = opf.Descendants(OpfNs + "itemref").Select(r => r.Attribute("idref")?.Value ?? "").ToList();

        // 尝试解析 NCX 目录标签
        var tocLabels = ReadNcxLabels(zip, manifest, opfDir);

        var book = new EpubBook { FilePath = epubPath, Title = title, Author = author };

        // 预读所有图片资源，用于内联 base64
        var images = ReadImages(zip);

        int idx = 0;
        foreach (var idref in spineRefs)
        {
            if (!manifest.TryGetValue(idref, out var item)) continue;
            if (!item.mt.Contains("xhtml") && !item.mt.Contains("html")) continue;

            var href = (opfDir + item.href).Replace("//", "/");
            var entry = zip.GetEntry(href) ?? zip.GetEntry(Uri.UnescapeDataString(href));
            if (entry == null) continue;

            var bodyHtml = ExtractBodyHtml(ReadEntryText(zip, entry));
            bodyHtml = InlineImages(bodyHtml, images, opfDir);
            if (string.IsNullOrWhiteSpace(bodyHtml)) continue;

            idx++;
            var label = tocLabels.TryGetValue(idx - 1, out var t) && !string.IsNullOrWhiteSpace(t) ? t : $"第 {idx} 章";
            book.Chapters.Add(new EpubChapter { Title = label, HtmlBody = bodyHtml });
        }

        return book;
    }

    /// <summary>读取 zip 里所有图片条目，返回 相对路径 -> (mime, base64)</summary>
    private static Dictionary<string, (string mime, string b64)> ReadImages(ZipArchive zip)
    {
        var dict = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            var lower = e.FullName.ToLowerInvariant();
            if (!(lower.EndsWith(".jpg") || lower.EndsWith(".jpeg") || lower.EndsWith(".png")
                  || lower.EndsWith(".gif") || lower.EndsWith(".webp") || lower.EndsWith(".svg")))
                continue;
            try
            {
                using var s = e.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                var mime = lower.EndsWith(".png") ? "image/png"
                    : lower.EndsWith(".gif") ? "image/gif"
                    : lower.EndsWith(".webp") ? "image/webp"
                    : lower.EndsWith(".svg") ? "image/svg+xml"
                    : "image/jpeg";
                dict[e.FullName] = (mime, Convert.ToBase64String(ms.ToArray()));
            }
            catch { }
        }
        return dict;
    }

    /// <summary>把 &lt;img src="..."/&gt; 改成 data: URI。</summary>
    private static string InlineImages(string html, Dictionary<string, (string mime, string b64)> images, string opfDir)
    {
        if (images.Count == 0) return html;
        try
        {
            return System.Text.RegularExpressions.Regex.Replace(html,
                @"<img[^>]+src\s*=\s*[""']([^""']+)[""'][^>]*>",
                m =>
                {
                    var src = m.Groups[1].Value;
                    if (src.StartsWith("data:")) return m.Value;
                    var clean = src.Split('#')[0].Split('?')[0];
                    var full = (opfDir + clean).Replace("//", "/");
                    // 用栈方式规范化 ../
                    var parts = full.Split('/');
                    var stack = new List<string>();
                    foreach (var part in parts)
                    {
                        if (part == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); }
                        else if (part != "." && part != "") stack.Add(part);
                    }
                    full = string.Join("/", stack);
                    if (!images.TryGetValue(full, out var imgData)) return m.Value;
                    return $"<img src=\"data:{imgData.mime};base64,{imgData.b64}\" style=\"max-width:100%;height:auto;display:block;margin:1em auto;\"/>";
                },
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        catch
        {
            return html;
        }
    }

    private static Dictionary<int, string> ReadNcxLabels(ZipArchive zip, Dictionary<string, (string href, string mt)> manifest, string opfDir)
    {
        var result = new Dictionary<int, string>();
        var ncx = manifest.Values.FirstOrDefault(v => v.mt.Contains("ncx"));
        if (ncx.href == null!) return result;
        try
        {
            var path = (opfDir + ncx.href).Replace("//", "/");
            var entry = zip.GetEntry(path);
            if (entry == null) return result;
            var ncxDoc = XDocument.Parse(ReadEntryText(zip, entry));
            int i = 0;
            foreach (var np in ncxDoc.Descendants().Where(e => e.Name.LocalName == "navPoint"))
            {
                var label = np.Descendants().FirstOrDefault(e => e.Name.LocalName == "navLabel")?
                    .Descendants().FirstOrDefault(e => e.Name.LocalName == "text")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(label)) result[i] = label!;
                i++;
            }
        }
        catch { /* NCX 解析失败不影响主流程 */ }
        return result;
    }

    private static string ExtractBodyHtml(string xhtml)
    {
        try
        {
            var d = XDocument.Parse(xhtml);
            var body = d.Descendants(XhtmlNs + "body").FirstOrDefault();
            if (body == null) body = d.Descendants().FirstOrDefault(e => e.Name.LocalName == "body");
            if (body == null) return "";
            // 去掉脚本与样式
            foreach (var bad in body.Descendants().Where(e => e.Name.LocalName is "script" or "style").ToList())
                bad.Remove();
            // 剥掉所有元素的内联 style / class / width / height 属性，强制走我们的排版
            foreach (var el in body.Descendants().ToList())
            {
                foreach (var attr in el.Attributes().Where(a => a.Name.LocalName is "style" or "class" or "width" or "height" or "align" or "valign" or "bgcolor" or "color").ToList())
                    attr.Remove();
            }
            return string.Concat(body.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
        }
        catch
        {
            return xhtml; // 解析失败时原样返回
        }
    }

    /// <summary>读取 zip 里指定条目内容。</summary>
    private static byte[] ReadEntryBytes(ZipArchive zip, string path)
    {
        var e = zip.GetEntry(path) ?? zip.GetEntry(Uri.UnescapeDataString(path));
        if (e == null) return Array.Empty<byte>();
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>尝试从 EPUB 中提取封面图片（字节 + 扩展名）。失败返回 null。</summary>
    public static (byte[] data, string ext)? ExtractCover(string epubPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(epubPath);
            var containerEntry = zip.GetEntry("META-INF/container.xml");
            if (containerEntry == null) return null;
            var opfPath = ReadEntryText(zip, containerEntry);
            var doc = XDocument.Parse(opfPath);
            var opfFullPath = doc.Descendants().Elements()
                .First(e => e.Name.LocalName == "rootfile")
                .Attribute("full-path")?.Value;
            if (opfFullPath == null) return null;

            int slash = opfFullPath.LastIndexOf('/');
            var opfDir = slash >= 0 ? opfFullPath.Substring(0, slash + 1) : "";

            var opfEntry = zip.GetEntry(opfFullPath);
            if (opfEntry == null) return null;
            var opf = XDocument.Parse(ReadEntryText(zip, opfEntry));

            // 方法1: meta name="cover" content="cover-id"
            string? coverId = opf.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "meta" &&
                    (string?)e.Attribute("name") == "cover")?
                .Attribute("content")?.Value;

            // 方法2: manifest item properties="cover-image"
            if (coverId == null)
            {
                var coverItem = opf.Descendants(OpfNs + "item")
                    .FirstOrDefault(i => (string?)i.Attribute("properties") == "cover-image");
                coverId = coverItem?.Attribute("id")?.Value;
            }

            string? coverHref = null;
            if (coverId != null)
            {
                coverHref = opf.Descendants(OpfNs + "item")
                    .FirstOrDefault(i => (string?)i.Attribute("id") == coverId)?
                    .Attribute("href")?.Value;
            }

            // 方法3: 找第一张 jpg/png
            if (coverHref == null)
            {
                foreach (var it in opf.Descendants(OpfNs + "item"))
                {
                    var mt = (string?)it.Attribute("media-type") ?? "";
                    if (mt.StartsWith("image/"))
                    {
                        coverHref = it.Attribute("href")?.Value;
                        break;
                    }
                }
            }

            if (coverHref == null) return null;
            var full = (opfDir + coverHref).Replace("//", "/");
            var data = ReadEntryBytes(zip, full);
            if (data.Length == 0) return null;

            var ext = coverHref.ToLowerInvariant();
            var dot = ext.LastIndexOf('.');
            ext = dot >= 0 ? ext.Substring(dot) : ".jpg";
            if (ext == ".jpeg") ext = ".jpg";
            return (data, ext);
        }
        catch { return null; }
    }

    private static string ReadEntryText(ZipArchive zip, ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var r = new StreamReader(s, System.Text.Encoding.UTF8);
        return r.ReadToEnd();
    }
}
