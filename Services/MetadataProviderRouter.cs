using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public class MetadataProviderRouter : IMetadataProvider
{
    private readonly ISettingsService _settingsService;
    private readonly GeminiMetadataProvider _gemini;
    private readonly CustomApiMetadataProvider _customApi;

    public MetadataProviderRouter(
        ISettingsService settingsService,
        GeminiMetadataProvider gemini,
        CustomApiMetadataProvider customApi)
    {
        _settingsService = settingsService;
        _gemini = gemini;
        _customApi = customApi;
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
            ApiProvider.CustomApi => _customApi,
            _ => _gemini
        };
    }
}
