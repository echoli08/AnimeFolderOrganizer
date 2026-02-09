namespace AnimeFolderOrganizer.Models;

/// <summary>
/// sub_share 搜尋載入狀態
/// </summary>
public sealed record SubShareSearchDiagnostics(
    string DbPath,
    int RecordCount,
    int SubsElementCount,
    int ParsedCount,
    long FileSize,
    int RawSubsTagCount,
    string Fingerprint,
    string LastError);
