using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public class FileSettingsService : ISettingsService
{
    private const string FileName = "settings.json";
    private readonly string _filePath;

    public string? ApiKey { get; set; }
    public string ModelName { get; set; } = "gemini-2.5-flash-lite";
    public string NamingFormat { get; set; } = "{Title} ({Year})";
    public NamingLanguage PreferredLanguage { get; set; } = NamingLanguage.TraditionalChinese;

    public FileSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "AnimeFolderOrganizer");
        Directory.CreateDirectory(appFolder);
        _filePath = Path.Combine(appFolder, FileName);
    }

    public async Task SaveAsync()
    {
        try
        {
            var data = new SettingsData(ApiKey, ModelName, NamingFormat, PreferredLanguage);
            var json = JsonSerializer.Serialize(data);
            await File.WriteAllTextAsync(_filePath, json);
        }
        catch
        {
            // Ignore save errors for now
        }
    }

    public async Task LoadAsync()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = await File.ReadAllTextAsync(_filePath);
                var data = JsonSerializer.Deserialize<SettingsData>(json);
                if (data != null)
                {
                    ApiKey = data.ApiKey;
                    ModelName = string.IsNullOrWhiteSpace(data.ModelName) ? "gemini-2.5-flash-lite" : data.ModelName;
                    NamingFormat = string.IsNullOrWhiteSpace(data.NamingFormat) ? "{Title} ({Year})" : data.NamingFormat;
                    PreferredLanguage = data.PreferredLanguage;
                }
            }
        }
        catch
        {
            // Ignore load errors, use defaults
        }
    }

    private record SettingsData(string? ApiKey, string ModelName, string NamingFormat, NamingLanguage PreferredLanguage);
}
