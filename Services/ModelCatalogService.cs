using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public class ModelCatalogService : IModelCatalogService
{
    private readonly GeminiModelCatalogService _gemini;
    private readonly CustomApiModelCatalogService _customApi;

    public ModelCatalogService(
        GeminiModelCatalogService gemini,
        CustomApiModelCatalogService customApi)
    {
        _gemini = gemini;
        _customApi = customApi;
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(ApiProvider provider, string? apiKey, bool forceRefresh)
    {
        return provider switch
        {
            ApiProvider.CustomApi => _customApi.GetAvailableModelsAsync(apiKey, forceRefresh),
            _ => _gemini.GetAvailableModelsAsync(apiKey, forceRefresh)
        };
    }
}
