using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using GDI.Core.Security;
using Microsoft.Win32;

namespace GDI.Desktop.ViewModels;

public sealed record LogEntry(DateTime Timestamp, string Category, string Message, string Formatted);

public sealed class LogsViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly List<LogEntry> _allEntries = [];

    public ObservableCollection<LogEntry> FilteredEntries { get; } = [];

    private string _searchFilter = "";
    public string SearchFilter
    {
        get => _searchFilter;
        set
        {
            if (SetProperty(ref _searchFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    private string _selectedCategory = "ALL";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
            }
        }
    }

    private bool _autoScroll = true;
    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public RelayCommand ClearCommand { get; }
    public RelayCommand ExportCommand { get; }
    public RelayCommand FilterCategoryCommand { get; }

    public LogsViewModel(MainViewModel main)
    {
        _main = main;
        ClearCommand = new RelayCommand(ClearLogs);
        ExportCommand = new RelayCommand(ExportLogs);
        FilterCategoryCommand = new RelayCommand(cat => SelectedCategory = cat?.ToString() ?? "ALL");
    }

    public void Log(string rawMessage, string category = "System")
    {
        var redacted = SecretRedactor.RedactUrl(rawMessage);
        redacted = SecretRedactor.RedactHeaders(redacted);

        var entry = new LogEntry(
            DateTime.Now,
            category.ToUpperInvariant(),
            redacted,
            $"[{DateTime.Now:HH:mm:ss.fff}] [{category.ToUpperInvariant()}] {redacted}"
        );

        _allEntries.Add(entry);
        if (_allEntries.Count > 2000) _allEntries.RemoveAt(0);

        if (MatchesFilter(entry))
        {
            FilteredEntries.Add(entry);
            if (FilteredEntries.Count > 1000) FilteredEntries.RemoveAt(0);
        }
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        foreach (var entry in _allEntries)
        {
            if (MatchesFilter(entry))
            {
                FilteredEntries.Add(entry);
            }
        }
    }

    private bool MatchesFilter(LogEntry entry)
    {
        if (SelectedCategory != "ALL" && !entry.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(SearchFilter) &&
            !entry.Message.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private void ClearLogs()
    {
        _allEntries.Clear();
        FilteredEntries.Clear();
    }

    private void ExportLogs()
    {
        var sfd = new SaveFileDialog
        {
            Filter = "Log File (*.log)|*.log|Text File (*.txt)|*.txt",
            FileName = $"GDI_SRT_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };
        if (sfd.ShowDialog() == true)
        {
            File.WriteAllLines(sfd.FileName, _allEntries.Select(e => e.Formatted));
            MessageBox.Show("Logs exported successfully.", "Export Logs", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
