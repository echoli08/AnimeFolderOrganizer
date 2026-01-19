using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AnimeFolderOrganizer.Services;

public class OpenRouterModelCatalogService
{
    private const string Endpoint = "https://openrouter.ai/api/v1/models";
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<string> _cached = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheTimeUtc = DateTime.MinValue;

    public OpenRouterModelCatalogService(HttpClient httpClient)
    {
        _httpClient = httpClient;
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

            using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
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
                    if (!string.IsNullOrWhiteSpace(id) && IsFreeModel(item))
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

    private static bool IsFreeModel(JsonElement item)
    {
        if (item.TryGetProperty("is_free", out var isFreeElement) && isFreeElement.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (item.TryGetProperty("free", out var freeElement))
        {
            if (freeElement.ValueKind == JsonValueKind.True) return true;
            if (freeElement.ValueKind == JsonValueKind.False) return false;
        }

        if (!item.TryGetProperty("pricing", out var pricingElement) || pricingElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var hasValue = false;
        foreach (var prop in pricingElement.EnumerateObject())
        {
            if (!TryParsePrice(prop.Value, out var price)) return false;
            hasValue = true;
            if (price > 0m) return false;
        }

        return hasValue;
    }

    private static bool TryParsePrice(JsonElement element, out decimal price)
    {
        price = 0m;
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetDecimal(out price);
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();
            return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out price);
        }

        return false;
    }
}
