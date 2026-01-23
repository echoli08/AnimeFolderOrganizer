using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// 使用第三方轉發的 DeepSeek API 分析資料夾名稱
/// </summary>
public partial class DeepseekProxyMetadataProvider : IMetadataProvider
{
    public string ProviderName => "DeepSeek Proxy";

    private const int MaxRetryCount = 2;
    private const int CooldownMilliseconds = 1200;
    private const int BaseBackoffMilliseconds = 800;

    private readonly HttpClient _httpClient;
    private readonly ISettingsService _settingsService;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public DeepseekProxyMetadataProvider(ISettingsService settingsService, HttpClient httpClient)
    {
        _settingsService = settingsService;
        _httpClient = httpClient;
    }

    public async Task<AnimeMetadata?> AnalyzeAsync(string folderName)
    {
        var results = await AnalyzeBatchAsync(new[] { folderName });
        return results.Count > 0 ? results[0] : null;
    }

    public async Task<IReadOnlyList<AnimeMetadata?>> AnalyzeBatchAsync(IReadOnlyList<string> folderNames)
    {
        var apiKey = _settingsService.DeepseekProxyApiKey;
        var modelName = NormalizeModelName(_settingsService.ModelName);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return BuildErrorList(folderNames.Count, "no-key", "請先設定 API Key");
        }

        if (folderNames.Count == 0)
        {
            return Array.Empty<AnimeMetadata?>();
        }

        var prompt = BuildUserPrompt(folderNames);
        var requestBody = new
        {
            model = modelName,
            messages = new[]
            {
                new
                {
                    role = "system",
                    content = "You are a strict JSON generator. Return ONLY a JSON object that matches the schema."
                },
                new
                {
                    role = "user",
                    content = prompt
                }
            },
            temperature = 0.2
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var (text, error) = await PostForTextAsync(apiKey, jsonContent);
        if (error != null)
        {
            return BuildErrorList(folderNames.Count, error.Id, error.TitleTW);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return BuildEmptyList(folderNames.Count);
        }

        var cleanText = CleanText(text);
        var parsed = TryParseBatch(cleanText);
        if (parsed == null)
        {
            return BuildEmptyList(folderNames.Count);
        }

        return MapBatchResult(folderNames.Count, parsed);
    }

    private string BuildEndpoint()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settingsService.DeepseekProxyBaseUrl)
            ? "https://api.chatanywhere.org/v1"
            : _settingsService.DeepseekProxyBaseUrl!.Trim();
        baseUrl = baseUrl.TrimEnd('/');
        return $"{baseUrl}/chat/completions";
    }

    private static string NormalizeModelName(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return ModelDefaults.DeepseekProxyDefaultModel;
        return modelName.Trim();
    }

    private async Task<(string? Text, AnimeMetadata? Error)> PostForTextAsync(string apiKey, string jsonContent)
    {
        for (var attempt = 0; attempt <= MaxRetryCount; attempt++)
        {
            try
            {
                await WaitForCooldownAsync();
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpoint());
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"DeepSeek Proxy API Error: {response.StatusCode} - {errorContent}");

                    if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    {
                        return (null, BuildErrorMetadata("no-key", "請先設定 API Key"));
                    }

                    if (response.StatusCode == (HttpStatusCode)429)
                    {
                        var retrySeconds = GetRetryAfterSeconds(response, errorContent);
                        var backoffSeconds = GetBackoffSeconds(attempt);
                        var waitSeconds = retrySeconds.HasValue
                            ? Math.Max(retrySeconds.Value, backoffSeconds)
                            : backoffSeconds;

                        if (attempt < MaxRetryCount)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
                            continue;
                        }

                        return (null, BuildErrorMetadata("rate-limit", "請稍後重試 (已達速率限制)"));
                    }

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return (null, BuildErrorMetadata("model-not-found", "模型不存在或無法存取"));
                    }

                    if (response.StatusCode == (HttpStatusCode)402)
                    {
                        return (null, BuildErrorMetadata("quota-exceeded", "配額不足或模型不在方案內"));
                    }

                    return (null, null);
                }

                var responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                var text = doc.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                return (text, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DeepSeek Proxy Provider Exception: {ex}");
                return (null, null);
            }
        }

        return (null, null);
    }

    private async Task WaitForCooldownAsync()
    {
        await _requestGate.WaitAsync();
        try
        {
            var now = DateTime.UtcNow;
            var elapsed = now - _lastRequestUtc;
            if (elapsed.TotalMilliseconds < CooldownMilliseconds)
            {
                var delay = TimeSpan.FromMilliseconds(CooldownMilliseconds - elapsed.TotalMilliseconds);
                await Task.Delay(delay);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private static double GetBackoffSeconds(int attempt)
    {
        var jitter = Random.Shared.NextDouble() * 0.5;
        var baseSeconds = BaseBackoffMilliseconds / 1000.0;
        return baseSeconds * Math.Pow(2, attempt) + jitter;
    }

    // 在 AnimeFolderOrganizer.Services.GeminiMetadataProvider 中

    private static string BuildUserPrompt(IReadOnlyList<string> folderNames)
    {
        var lines = folderNames
            .Select((name, index) => $"[{index}] \"{name}\"");

        var folderInput = string.Join("\n", lines);
        return ApiPrompt.BuildUserPrompt(folderInput);
    }

    private static string CleanText(string text)
    {
        if (text.StartsWith("```json"))
        {
            text = text.Substring(7);
        }
        if (text.StartsWith("```"))
        {
            text = text.Substring(3);
        }
        if (text.EndsWith("```"))
        {
            text = text.Substring(0, text.Length - 3);
        }

        return text.Trim();
    }

    private static BatchResponse? TryParseBatch(string text)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<BatchResponse>(text, options);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<AnimeMetadata?> MapBatchResult(int count, BatchResponse batch)
    {
        var list = new List<AnimeMetadata?>(BuildEmptyList(count));

        var items = batch.Items ?? new List<BatchItem>();
        foreach (var item in items)
        {
            if (item.Index < 0 || item.Index >= list.Count) continue;
            list[item.Index] = new AnimeMetadata(
                Id: item.Id ?? string.Empty,
                TitleJP: item.TitleJP ?? string.Empty,
                TitleCN: item.TitleCN ?? string.Empty,
                TitleTW: item.TitleTW ?? string.Empty,
                TitleEN: item.TitleEN ?? string.Empty,
                Type: item.Type ?? string.Empty,
                Year: item.Year,
                Confidence: item.Confidence
            );
        }

        return list;
    }

    private static IReadOnlyList<AnimeMetadata?> BuildEmptyList(int count)
    {
        return Enumerable.Repeat<AnimeMetadata?>(null, count).ToList();
    }

    private static IReadOnlyList<AnimeMetadata?> BuildErrorList(int count, string id, string message)
    {
        var list = new List<AnimeMetadata?>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(BuildErrorMetadata(id, message));
        }

        return list;
    }

    private static double? GetRetryAfterSeconds(HttpResponseMessage response, string errorContent)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var headerValue = values.FirstOrDefault();
            if (double.TryParse(headerValue, out var secondsFromHeader))
            {
                return secondsFromHeader;
            }
        }

        var match = RetryAfterRegex().Match(errorContent);
        if (match.Success && double.TryParse(match.Groups["s"].Value, out var secondsFromBody))
        {
            return secondsFromBody;
        }

        return null;
    }

    private static AnimeMetadata BuildErrorMetadata(string id, string message)
    {
        return new AnimeMetadata(
            Id: id,
            TitleJP: message,
            TitleCN: message,
            TitleTW: message,
            TitleEN: message,
            Type: string.Empty,
            Year: null,
            Confidence: 0
        );
    }

    [GeneratedRegex(@"retry in\s+(?<s>\d+(\.\d+)?)s", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex RetryAfterRegex();

    private record BatchResponse(List<BatchItem>? Items);
    private record BatchItem(int Index, string? Id, string? TitleJP, string? TitleCN, string? TitleTW, string? TitleEN, string? Type, int? Year, double Confidence);
}
