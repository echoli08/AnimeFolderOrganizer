using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public class MetadataProviderRouter : IMetadataProvider
{
    private readonly ISettingsService _settingsService;
    private readonly GeminiMetadataProvider _gemini;
    private readonly OpenRouterMetadataProvider _openRouter;
    private readonly GroqMetadataProvider _groq;
    private readonly DeepseekProxyMetadataProvider _deepseek;

    public MetadataProviderRouter(
        ISettingsService settingsService,
        GeminiMetadataProvider gemini,
        OpenRouterMetadataProvider openRouter,
        GroqMetadataProvider groq,
        DeepseekProxyMetadataProvider deepseek)
    {
        _settingsService = settingsService;
        _gemini = gemini;
        _openRouter = openRouter;
        _groq = groq;
        _deepseek = deepseek;
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
        return _settingsService.ApiProvider switch
        {
            ApiProvider.OpenRouter => _openRouter,
            ApiProvider.Groq => _groq,
            ApiProvider.DeepseekProxy => _deepseek,
            _ => _gemini
        };
    }
}
