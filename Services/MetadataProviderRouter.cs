using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public class MetadataProviderRouter : IMetadataProvider
{
    private readonly ISettingsService _settingsService;
    private readonly GeminiMetadataProvider _gemini;
    private readonly OpenRouterMetadataProvider _openRouter;

    public MetadataProviderRouter(
        ISettingsService settingsService,
        GeminiMetadataProvider gemini,
        OpenRouterMetadataProvider openRouter)
    {
        _settingsService = settingsService;
        _gemini = gemini;
        _openRouter = openRouter;
    }

    public string ProviderName => GetProvider().ProviderName;

    public Task<AnimeMetadata?> AnalyzeAsync(string folderName)
    {
        return GetProvider().AnalyzeAsync(folderName);
    }

    public Task<IReadOnlyList<AnimeMetadata?>> AnalyzeBatchAsync(IReadOnlyList<string> folderNames)
    {
        return GetProvider().AnalyzeBatchAsync(folderNames);
    }

    private IMetadataProvider GetProvider()
    {
        return _settingsService.ApiProvider == ApiProvider.OpenRouter
            ? _openRouter
            : _gemini;
    }
}
