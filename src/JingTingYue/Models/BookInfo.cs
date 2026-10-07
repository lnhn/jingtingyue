namespace JingTingYue.Models;

/// <summary>书架中一本书的基本信息（后续阶段会扩展为持久化记录）。</summary>
public record BookInfo(string Title, string Author, string CoverHex, int Progress);
