using System.Threading.Tasks;
using AnimeFolderOrganizer.Models;

namespace AnimeFolderOrganizer.Services;

public interface ISettingsService
{
    string? ApiKey { get; set; }
    string ModelName { get; set; }
    string NamingFormat { get; set; }
    NamingLanguage PreferredLanguage { get; set; }
    Task SaveAsync();
    Task LoadAsync();
}
