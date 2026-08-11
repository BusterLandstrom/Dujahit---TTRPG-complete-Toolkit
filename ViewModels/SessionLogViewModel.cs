using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace Dujahit.ViewModels
{
    public class SessionLogViewModel : ViewModelBase
    {
        private List<SessionLogRowViewModel> _all = new();

        public ObservableCollection<SessionLogRowViewModel> Visible { get; } = new();
        public ObservableCollection<string> EventTypes { get; } = new() { "All" };
        public ObservableCollection<string> Actors { get; } = new() { "All" };

        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportCommand { get; }

        private string _exportStatus = "";
        public string ExportStatus
        {
            get => _exportStatus;
            set => this.RaiseAndSetIfChanged(ref _exportStatus, value);
        }

        private string _selectedEventType = "All";
        public string SelectedEventType
        {
            get => _selectedEventType;
            set { this.RaiseAndSetIfChanged(ref _selectedEventType, value); ApplyFilter(); }
        }

        private string _selectedActor = "All";
        public string SelectedActor
        {
            get => _selectedActor;
            set { this.RaiseAndSetIfChanged(ref _selectedActor, value); ApplyFilter(); }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set { this.RaiseAndSetIfChanged(ref _searchText, value); ApplyFilter(); }
        }

        public bool HasRows => Visible.Count > 0;
        public string CountLabel => Visible.Count + (Visible.Count == 1 ? " event" : " events");

        public SessionLogViewModel()
        {
            RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);
            ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync);
        }

        // Exports what the filters show, not the whole table, since narrowing first is the whole point of exporting
        internal static string FormatRows(IEnumerable<SessionLogRowViewModel> rows)
        {
            var sb = new StringBuilder();
            foreach (var r in rows)
                sb.AppendLine(r.TimeText + "  " + r.ActorName + "  [" + r.EventType + "]  " + r.Summary);
            return sb.ToString();
        }

        private async Task ExportAsync()
        {
            if (Visible.Count == 0) { ExportStatus = "Nothing to export with these filters."; return; }
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is not { } w) return;

            var file = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export session log",
                SuggestedFileName = "session-log.txt",
                DefaultExtension = "txt",
                FileTypeChoices = new[] { new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } } }
            });
            if (file is null) return;

            try
            {
                await File.WriteAllTextAsync(file.Path.LocalPath, FormatRows(Visible));
                ExportStatus = "Exported " + Visible.Count + (Visible.Count == 1 ? " event." : " events.");
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[SessionLog] export failed", ex);
                ExportStatus = "The export did not write, check the log.";
            }
        }

        public async Task LoadAsync()
        {
            var entries = await App.PM.LoadSessionLogAsync();
            _all = entries.Select(e => new SessionLogRowViewModel(e)).ToList();

            var types = new List<string> { "All" };
            types.AddRange(_all.Select(r => r.EventType).Where(t => !string.IsNullOrEmpty(t)).Distinct().OrderBy(t => t));
            EventTypes.Clear();
            foreach (var t in types) EventTypes.Add(t);

            var actors = new List<string> { "All" };
            actors.AddRange(_all.Select(r => r.ActorName).Where(a => !string.IsNullOrEmpty(a)).Distinct().OrderBy(a => a));
            Actors.Clear();
            foreach (var a in actors) Actors.Add(a);

            if (!EventTypes.Contains(SelectedEventType)) SelectedEventType = "All";
            if (!Actors.Contains(SelectedActor)) SelectedActor = "All";

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            IEnumerable<SessionLogRowViewModel> q = _all;

            if (SelectedEventType != "All")
                q = q.Where(r => r.EventType == SelectedEventType);
            if (SelectedActor != "All")
                q = q.Where(r => r.ActorName == SelectedActor);

            var s = SearchText?.Trim();
            if (!string.IsNullOrEmpty(s))
                q = q.Where(r =>
                    (r.Summary?.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0)
                    || (r.ActorName?.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0));

            Visible.Clear();
            foreach (var r in q) Visible.Add(r);

            this.RaisePropertyChanged(nameof(HasRows));
            this.RaisePropertyChanged(nameof(CountLabel));
        }
    }

    public class SessionLogRowViewModel
    {
        public string TimeText { get; }
        public string ActorName { get; }
        public string EventType { get; }
        public string Summary { get; }

        public SessionLogRowViewModel(SessionLogEntry entry)
        {
            TimeText = entry.Timestamp.ToLocalTime().ToString("MMM d, HH:mm:ss");
            ActorName = entry.ActorName ?? "";
            EventType = entry.EventType ?? "";
            Summary = entry.Summary ?? "";
        }
    }
}
