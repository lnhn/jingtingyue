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

        int idx = 0;
        foreach (var idref in spineRefs)
        {
            if (!manifest.TryGetValue(idref, out var item)) continue;
            if (!item.mt.Contains("xhtml") && !item.mt.Contains("html")) continue;

            var href = (opfDir + item.href).Replace("//", "/");
            var entry = zip.GetEntry(href) ?? zip.GetEntry(Uri.UnescapeDataString(href));
            if (entry == null) continue;

            var bodyHtml = ExtractBodyHtml(ReadEntryText(zip, entry));
            if (string.IsNullOrWhiteSpace(bodyHtml)) continue;

            idx++;
            var label = tocLabels.TryGetValue(idx - 1, out var t) && !string.IsNullOrWhiteSpace(t) ? t : $"第 {idx} 章";
            book.Chapters.Add(new EpubChapter { Title = label, HtmlBody = bodyHtml });
        }

        return book;
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
            return string.Concat(body.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
        }
        catch
        {
            return xhtml; // 解析失败时原样返回
        }
    }

    private static string ReadEntryText(ZipArchive zip, ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var r = new StreamReader(s, System.Text.Encoding.UTF8);
        return r.ReadToEnd();
    }
}
