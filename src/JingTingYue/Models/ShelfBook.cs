using JingTingYue.Services;

namespace JingTingYue.Models;

/// <summary>书架中一本书（含解析出的内容，真实 EPUB 导入后填充）。</summary>
public record ShelfBook(string Title, string Author, string Type, string CoverHex, int Progress, EpubBook? Book);
