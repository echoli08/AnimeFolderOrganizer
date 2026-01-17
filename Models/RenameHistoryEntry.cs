using System;

namespace AnimeFolderOrganizer.Models;

public record RenameHistoryEntry(
    DateTime TimestampUtc,
    string OriginalPath,
    string NewPath,
    string Status,
    string Message
);
