using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public class ModelCatalogService : IModelCatalogService
{
    private readonly GeminiModelCatalogService _gemini;
    private readonly OpenRouterModelCatalogService _openRouter;

    public ModelCatalogService(GeminiModelCatalogService gemini, OpenRouterModelCatalogService openRouter)
    {
        _gemini = gemini;
        _openRouter = openRouter;
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(ApiProvider provider, string? apiKey, bool forceRefresh)
    {
        return provider == ApiProvider.OpenRouter
            ? _openRouter.GetAvailableModelsAsync(apiKey, forceRefresh)
            : _gemini.GetAvailableModelsAsync(apiKey, forceRefresh);
    }
}
