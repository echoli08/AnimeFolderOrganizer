using System;
using AnimeFolderOrganizer.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnimeFolderOrganizer.ViewModels;

public partial class HistoryItemViewModel : ObservableObject
{
    public RenameHistoryEntry Entry { get; }

    [ObservableProperty]
    private bool _isSelected;

    public DateTime TimestampUtc => Entry.TimestampUtc;
    public string OriginalPath => Entry.OriginalPath;
    public string NewPath => Entry.NewPath;
    public string Status => Entry.Status;
    public string Message => Entry.Message;

    public HistoryItemViewModel(RenameHistoryEntry entry)
    {
        Entry = entry;
    }
}
