using System.Collections.Generic;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public class ModelCatalogService : IModelCatalogService
{
    private readonly GeminiModelCatalogService _gemini;
    private readonly OpenRouterModelCatalogService _openRouter;
    private readonly GroqModelCatalogService _groq;
    private readonly DeepseekProxyModelCatalogService _deepseek;

    public ModelCatalogService(
        GeminiModelCatalogService gemini,
        OpenRouterModelCatalogService openRouter,
        GroqModelCatalogService groq,
        DeepseekProxyModelCatalogService deepseek)
    {
        _gemini = gemini;
        _openRouter = openRouter;
        _groq = groq;
        _deepseek = deepseek;
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(ApiProvider provider, string? apiKey, bool forceRefresh)
    {
        return provider switch
        {
            ApiProvider.OpenRouter => _openRouter.GetAvailableModelsAsync(apiKey, forceRefresh),
            ApiProvider.Groq => _groq.GetAvailableModelsAsync(apiKey, forceRefresh),
            ApiProvider.DeepseekProxy => _deepseek.GetAvailableModelsAsync(apiKey, forceRefresh),
            _ => _gemini.GetAvailableModelsAsync(apiKey, forceRefresh)
        };
    }
}
