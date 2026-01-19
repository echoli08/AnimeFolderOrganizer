using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeFolderOrganizer.Services;

public class GeminiModelCatalogService
{
    private const string EndpointBase = "https://generativelanguage.googleapis.com/v1beta/models";
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<string> _cached = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheTimeUtc = DateTime.MinValue;

    public GeminiModelCatalogService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(string? apiKey, bool forceRefresh)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return Array.Empty<string>();

        await _gate.WaitAsync();
        try
        {
            if (!forceRefresh && DateTime.UtcNow - _cacheTimeUtc < TimeSpan.FromMinutes(10) && _cached.Count > 0)
            {
                return _cached.OrderBy(x => x).ToList();
            }

            var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? pageToken = null;

            do
            {
                var url = string.IsNullOrWhiteSpace(pageToken)
                    ? $"{EndpointBase}?key={apiKey}"
                    : $"{EndpointBase}?key={apiKey}&pageToken={pageToken}";

                try
                {
                    using var response = await _httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        // 例外情境：API Key 無效或模型列表查詢失敗
                        return Array.Empty<string>();
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("models", out var modelsElement))
                    {
                        foreach (var model in modelsElement.EnumerateArray())
                        {
                            if (!model.TryGetProperty("supportedGenerationMethods", out var methods)) continue;
                            if (!methods.EnumerateArray().Any(m => string.Equals(m.GetString(), "generateContent", StringComparison.OrdinalIgnoreCase)))
                                continue;

                            if (!model.TryGetProperty("name", out var nameProp)) continue;
                            var name = nameProp.GetString();
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            if (name.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
                            {
                                name = name.Substring("models/".Length);
                            }

                            models.Add(name);
                        }
                    }

                    pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var tokenProp)
                        ? tokenProp.GetString()
                        : null;
                }
                catch
                {
                    // 例外處理：單次查詢失敗不阻斷流程，回傳目前結果
                    break;
                }
            } while (!string.IsNullOrWhiteSpace(pageToken));

            var filtered = models
                .Where(IsPrimaryGeminiModel)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _cached = filtered;
            _cacheTimeUtc = DateTime.UtcNow;

            return filtered.OrderBy(x => x).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsPrimaryGeminiModel(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (!name.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase)) return false;
        return name.Contains("-flash", StringComparison.OrdinalIgnoreCase)
               || name.Contains("-pro", StringComparison.OrdinalIgnoreCase)
               || name.Contains("-lite", StringComparison.OrdinalIgnoreCase);
    }
}
