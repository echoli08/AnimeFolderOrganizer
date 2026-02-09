using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// Google Gemini API 模型目錄服務實作。
/// </summary>
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
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ModelCatalogException(
                ApiProvider.Gemini,
                "驗證失敗",
                "API 金鑰為空或格式無效",
                null);
        }

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
                        var statusCode = (int)response.StatusCode;
                        var reason = GetStatusCodeMessage(response.StatusCode);
                        // 例外情境：API Key 無效或模型列表查詢失敗
                        throw new ModelCatalogException(
                            ApiProvider.Gemini,
                            "模型列表取得失敗",
                            $"{reason} (HTTP {statusCode})",
                            statusCode);
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
                catch (ModelCatalogException)
                {
                    // 重新拋出 ModelCatalogException，不阻斷
                    throw;
                }
                catch
                {
                    // 其他例外處理：單次查詢失敗不阻斷流程，回傳目前結果
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

    /// <summary>
    /// 根據 HTTP 狀態碼取得簡短描述訊息。
    /// </summary>
    private static string GetStatusCodeMessage(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "API 金鑰無效或已過期",
            HttpStatusCode.Forbidden => "API 存取被拒，請檢查權限",
            HttpStatusCode.NotFound => "模型端點不存在",
            HttpStatusCode.TooManyRequests => "已超過 API 請求限制",
            HttpStatusCode.InternalServerError => "伺服器內部錯誤",
            HttpStatusCode.ServiceUnavailable => "服務暫時無法使用",
            _ => "發生未預期的 HTTP 錯誤"
        };
    }
}
