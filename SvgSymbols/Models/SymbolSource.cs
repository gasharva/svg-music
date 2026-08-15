namespace SvgSymbols.Models;

public sealed record SymbolSource(
    string Kind,
    string Category,
    string Title,
    string FileName,
    string DescriptionUrl,
    string FileUrl,
    string? License,
    string? LicenseUrl,
    string? Artist);
