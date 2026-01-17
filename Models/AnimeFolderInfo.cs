using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace AnimeFolderOrganizer.Models;

/// <summary>
/// 代表一個動畫資料夾的資訊
/// </summary>
public partial class AnimeFolderInfo : ObservableObject
{
    public string OriginalPath { get; private set; }
    public string OriginalFolderName { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedName))]
    private string? _analyzedTitle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedName))]
    private int? _year;

    [ObservableProperty]
    private string? _metadataId;

    [ObservableProperty]
    private bool _isIdentified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedName))]
    private string _namingFormat = "{Title} ({Year})";

    // 多語言標題支援
    public string? TitleJP { get; set; }
    public string? TitleCN { get; set; } // Simplified
    public string? TitleTW { get; set; } // Traditional
    public string? TitleEN { get; set; } // English

    /// <summary>
    /// 使用者目前選擇使用的標題
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SuggestedName))]
    private string? _selectedTitle;

    /// <summary>
    /// 供 UI 選擇的可用標題清單
    /// </summary>
    public ObservableCollection<string> AvailableTitles { get; } = new();

    public AnimeFolderInfo(string originalPath, string originalFolderName)
    {
        OriginalPath = originalPath;
        OriginalFolderName = originalFolderName;
    }

    /// <summary>
    /// 改名後同步更新路徑與顯示名稱
    /// </summary>
    public void UpdateOriginalPath(string newPath)
    {
        OriginalPath = newPath;
        OriginalFolderName = Path.GetFileName(newPath);
        OnPropertyChanged(nameof(OriginalFolderName));
    }

    /// <summary>
    /// 取得建議的目標資料夾名稱 (Binding 用)
    /// </summary>
    public string SuggestedName => GetSuggestedName(NamingFormat);

    /// <summary>
    /// 取得建議的目標資料夾名稱
    /// </summary>
    /// <param name="format">格式字串，例如 "{Title} ({Year})"</param>
    /// <returns>格式化後的名稱</returns>
    public string GetSuggestedName(string format = "{Title} ({Year})")
    {
        // 優先使用 SelectedTitle，若無則退回 AnalyzedTitle
        var titleToUse = !string.IsNullOrWhiteSpace(SelectedTitle) ? SelectedTitle : AnalyzedTitle;

        if (string.IsNullOrWhiteSpace(titleToUse))
            return OriginalFolderName;

        var safeFormat = string.IsNullOrWhiteSpace(format) ? "{Title} ({Year})" : format;

        if (!Year.HasValue)
        {
            safeFormat = safeFormat.Replace("({Year})", string.Empty);
        }

        var name = safeFormat
            .Replace("{Title}", titleToUse)
            .Replace("{TitleTW}", TitleTW ?? titleToUse)
            .Replace("{TitleCN}", TitleCN ?? titleToUse)
            .Replace("{TitleJP}", TitleJP ?? titleToUse)
            .Replace("{TitleEN}", TitleEN ?? titleToUse)
            .Replace("{Year}", Year?.ToString() ?? string.Empty)
            .Replace("{Original}", OriginalFolderName);

        return name.Trim();
    }

    /// <summary>
    /// 更新可用標題清單通知 (當標題資料更新時呼叫)
    /// </summary>
    public void UpdateAvailableTitles()
    {
        var ordered = new List<string>();
        var exists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTitle(string? title)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            if (exists.Add(title))
            {
                ordered.Add(title);
            }
        }

        AddTitle(TitleTW);
        AddTitle(TitleCN);
        AddTitle(TitleJP);
        AddTitle(TitleEN);
        AddTitle(AnalyzedTitle);
        AddTitle(SelectedTitle);

        AvailableTitles.Clear();
        foreach (var title in ordered)
        {
            AvailableTitles.Add(title);
        }

        OnPropertyChanged(nameof(AvailableTitles));
    }

    partial void OnSelectedTitleChanged(string? value)
    {
        UpdateAvailableTitles();
    }
}
