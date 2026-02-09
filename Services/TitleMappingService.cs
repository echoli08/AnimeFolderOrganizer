using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

/// <summary>
/// 負責從 sub_share 專案資料庫中取得動畫標題對應資訊
/// 資料來源: https://github.com/foxofice/sub_share
/// </summary>
public partial class TitleMappingService
{
    private readonly HttpClient _httpClient;
    // 儲存從日文標題對應到其他語言標題的映射
    // Key: 日文標題 (正規化後), Value: 包含多種語言標題的物件
    private readonly Dictionary<string, AnimeTitleInfo> _titleMap = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInitialized = false;

    // GitHub API 基礎路徑 (使用 Raw 內容)
    // 注意: 由於 GitHub API 有 Rate Limit，且該 repo 結構為資料夾，
    // 為了效能與穩定性，建議未來改為下載特定索引檔或讓使用者手動匯入。
    // 目前此服務僅提供基礎架構與解析邏輯。
    private const string BaseUrl = "https://raw.githubusercontent.com/foxofice/sub_share/master/subs_list/animation";
    
    public TitleMappingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;

        try
        {
            // 這裡預留初始化邏輯，例如從本地快取載入或從網路下載索引
            // 由於目前沒有單一的資料檔，暫時標記為已初始化
            await Task.CompletedTask;
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"TitleMappingService Initialization Failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 嘗試根據日文標題尋找對應的中文/英文標題
    /// </summary>
    public AnimeTitleInfo? FindMapping(string japaneseTitle)
    {
        if (string.IsNullOrWhiteSpace(japaneseTitle)) return null;

        // 嘗試直接比對
        if (_titleMap.TryGetValue(japaneseTitle, out var info))
        {
            return info;
        }

        // 這裡可以加入更複雜的模糊比對邏輯
        
        return null;
    }

    /// <summary>
    /// 解析目錄名稱格式：(YYYY.MM.DD)日文標題 其他語言標題
    /// 例如: (2015.7.4)夏洛特 Charlotte
    /// </summary>
    public static AnimeTitleInfo? ParseDirectoryName(string dirName)
    {
        // 移除日期部分 (YYYY.MM.DD)
        var match = DatePrefixRegex().Match(dirName);
        if (!match.Success) return null;

        var datePart = match.Groups[0].Value;
        var titlePart = dirName.Substring(datePart.Length).Trim();

        // 簡單的分割邏輯：假設日文標題在最前面，後面跟著空格和其他語言標題
        // 這部分可能需要根據實際資料格式微調
        var parts = titlePart.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length == 0) return null;

        var jpTitle = parts[0];
        var otherTitle = parts.Length > 1 ? parts[1] : string.Empty;

        return new AnimeTitleInfo(jpTitle, otherTitle);
    }

    [GeneratedRegex(@"^\(\d{4}(\.\d{1,2}){2}\)")]
    private static partial Regex DatePrefixRegex();
}

public record AnimeTitleInfo(string JapaneseTitle, string OtherTitle);
