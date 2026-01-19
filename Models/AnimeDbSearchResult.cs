namespace AnimeFolderOrganizer.Models;

public sealed class AnimeDbSearchResult
{
    public int Id { get; init; }
    public string? Title { get; init; }
    public string? Url { get; init; }
    public string? Type { get; init; }
    public string? Subtype { get; init; }
}
