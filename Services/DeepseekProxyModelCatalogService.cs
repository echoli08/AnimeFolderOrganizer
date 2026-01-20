using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeFolderOrganizer.Services;

public class DeepseekProxyModelCatalogService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<string> _cached = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheTimeUtc = DateTime.MinValue;

    public DeepseekProxyModelCatalogService(HttpClient httpClient, ISettingsService settingsService)
    {
        _httpClient = httpClient;
        _settingsService = settingsService;
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(string? apiKey, bool forceRefresh)
    {
        await _gate.WaitAsync();
        try
        {
            if (!forceRefresh && DateTime.UtcNow - _cacheTimeUtc < TimeSpan.FromMinutes(10) && _cached.Count > 0)
            {
                return _cached.OrderBy(x => x).ToList();
            }

            var baseUrl = string.IsNullOrWhiteSpace(_settingsService.DeepseekProxyBaseUrl)
                ? "https://api.chatanywhere.tech/v1"
                : _settingsService.DeepseekProxyBaseUrl!.Trim();
            baseUrl = baseUrl.TrimEnd('/');
            var endpoint = $"{baseUrl}/models";

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return Array.Empty<string>();
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                foreach (var item in dataElement.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var idProp)) continue;
                    var id = idProp.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        models.Add(id);
                    }
                }
            }

            _cached = models;
            _cacheTimeUtc = DateTime.UtcNow;

            return models.OrderBy(x => x).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
        finally
        {
            _gate.Release();
        }
    }
}
