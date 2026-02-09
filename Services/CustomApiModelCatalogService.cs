using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// 自訂 API 模型目錄服務實作 (適用於 OpenRouter、Groq、DeepSeek 等相容 OpenAI API 的服務)。
/// </summary>
public class CustomApiModelCatalogService
{
    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<string> _cached = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheTimeUtc = DateTime.MinValue;

    public CustomApiModelCatalogService(HttpClient httpClient, ISettingsService settingsService)
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

            var baseUrl = string.IsNullOrWhiteSpace(_settingsService.CustomApiBaseUrl)
                ? "https://api.openai.com/v1"
                : _settingsService.CustomApiBaseUrl!.Trim();
            baseUrl = baseUrl.TrimEnd('/');

            // 如果使用者輸入的 URL 已經包含 /models，就直接使用
            string endpoint;
            if (baseUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = baseUrl;
            }
            else
            {
                endpoint = $"{baseUrl}/models";
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                var reason = GetStatusCodeMessage(response.StatusCode);
                throw new ModelCatalogException(
                    ApiProvider.CustomApi,
                    "模型列表取得失敗",
                    $"{reason} (HTTP {statusCode})",
                    statusCode);
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
        catch (ModelCatalogException)
        {
            throw;
        }
        catch
        {
            // 其他未知錯誤，回傳空清單以保持向後相容
            return Array.Empty<string>();
        }
        finally
        {
            _gate.Release();
        }
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
