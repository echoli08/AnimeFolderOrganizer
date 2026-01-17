namespace AnimeFolderOrganizer.Services;

public interface ITextConverter
{
    string? ToTraditional(string? text);
    string? ToSimplified(string? text);
}
