using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.IO;
using Dujahit.Models;

namespace Dujahit.ViewModels
{
    public class TemplateCategoryRow
    {
        public string Key { get; set; } = "";
        public int Count { get; set; }
        public string Display => $"{Key}  ({Count})";
    }

    public class TemplateEntryRow
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Display => string.IsNullOrWhiteSpace(Name) ? Id : Name;
        public string Sub => string.IsNullOrWhiteSpace(Name) ? "" : Id;
    }

    public class TemplateEditorViewModel : ViewModelBase
    {
        private JsonObject? _root;
        private readonly List<TemplateEntryRow> _allEntries = new();

        public ObservableCollection<TemplateCategoryRow> Categories { get; } = new();
        public ObservableCollection<TemplateEntryRow> Entries { get; } = new();
        public ObservableCollection<TemplateIssue> Issues { get; } = new();

        private TemplateCategoryRow? _selectedCategory;
        public TemplateCategoryRow? SelectedCategory
        {
            get => _selectedCategory;
            set { this.RaiseAndSetIfChanged(ref _selectedCategory, value); PopulateEntries(); }
        }

        private TemplateEntryRow? _selectedEntry;
        public TemplateEntryRow? SelectedEntry
        {
            get => _selectedEntry;
            set { this.RaiseAndSetIfChanged(ref _selectedEntry, value); LoadEntryJson(); }
        }

        private string _entryJson = "";
        public string EntryJson
        {
            get => _entryJson;
            set => this.RaiseAndSetIfChanged(ref _entryJson, value);
        }

        private string _entrySearch = "";
        public string EntrySearch
        {
            get => _entrySearch;
            set { this.RaiseAndSetIfChanged(ref _entrySearch, value); FilterEntries(); }
        }

        private string _status = "";
        public string Status
        {
            get => _status;
            set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        private bool _importOpen;
        public bool ImportOpen
        {
            get => _importOpen;
            set => this.RaiseAndSetIfChanged(ref _importOpen, value);
        }

        private string _statblockText = "";
        public string StatblockText
        {
            get => _statblockText;
            set => this.RaiseAndSetIfChanged(ref _statblockText, value);
        }

        public ReactiveCommand<Unit, Unit> SaveEntryCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteEntryCommand { get; }
        public ReactiveCommand<Unit, Unit> NewEntryCommand { get; }
        public ReactiveCommand<Unit, Unit> ValidateCommand { get; }
        public ReactiveCommand<Unit, Unit> ReloadCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCatalogCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleImportCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportStatblockCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportTemplateCommand { get; }

        public TemplateEditorViewModel()
        {
            SaveEntryCommand = ReactiveCommand.CreateFromTask(SaveEntryAsync);
            DeleteEntryCommand = ReactiveCommand.CreateFromTask(DeleteEntryAsync);
            NewEntryCommand = ReactiveCommand.Create(NewEntry);
            ValidateCommand = ReactiveCommand.Create(RunValidation);
            ReloadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
            RefreshCatalogCommand = ReactiveCommand.CreateFromTask(RefreshCatalogAsync);
            ToggleImportCommand = ReactiveCommand.Create(() => { ImportOpen = !ImportOpen; });
            ImportStatblockCommand = ReactiveCommand.CreateFromTask(ImportStatblockAsync);
            ExportTemplateCommand = ReactiveCommand.CreateFromTask(ExportTemplateAsync);
        }

        // Writes the stored blob, never the half edited json sitting in the box.
        private async Task ExportTemplateAsync()
        {
            if (App.PM == null) { Status = "No campaign loaded."; return; }
            var blob = await App.PM.GetActiveTemplateJsonAsync();
            if (string.IsNullOrEmpty(blob)) { Status = "No template to export."; return; }
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is not { } w) return;

            var file = await w.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export template",
                SuggestedFileName = App.PM.GetActiveTemplateId() + ".json",
                DefaultExtension = "json",
                FileTypeChoices = new[] { new FilePickerFileType("Template JSON") { Patterns = new[] { "*.json" } } }
            });
            if (file is null) return;

            try
            {
                await File.WriteAllTextAsync(file.Path.LocalPath, blob);
                Status = "Template written to " + file.Path.LocalPath + ".";
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[TemplateEditor] export failed", ex);
                Status = "The export did not write, check the log.";
            }
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;
            var blob = await App.PM.GetActiveTemplateJsonAsync();
            JsonObject? root = null;
            if (!string.IsNullOrEmpty(blob))
            {
                try { root = JsonNode.Parse(blob) as JsonObject; }
                catch (JsonException) { }
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _root = root;
                var cats = new List<TemplateCategoryRow>();
                if (_root != null)
                    foreach (var p in _root)
                        if (p.Value is JsonArray a && a.Count > 0 && a[0] is JsonObject)
                            cats.Add(new TemplateCategoryRow { Key = p.Key, Count = a.Count });

                Categories.Clear();
                foreach (var c in cats.OrderBy(c => c.Key)) Categories.Add(c);
                Entries.Clear();
                _allEntries.Clear();
                Status = _root == null ? "Could not read the active template." : $"{Categories.Count} catalog sections loaded.";
            });
        }

        private string IdKeyFor(string? array)
        {
            if (string.Equals(array, "ClassResources", StringComparison.OrdinalIgnoreCase)) return "Id";
            if (string.Equals(array, "Level", StringComparison.OrdinalIgnoreCase)) return "Level";
            return "TemplateId";
        }

        private void PopulateEntries()
        {
            _allEntries.Clear();
            SelectedEntry = null;
            EntryJson = "";
            if (_root != null && SelectedCategory != null && _root[SelectedCategory.Key] is JsonArray arr)
            {
                var idKey = IdKeyFor(SelectedCategory.Key);
                foreach (var el in arr)
                {
                    if (el is not JsonObject o) continue;
                    _allEntries.Add(new TemplateEntryRow
                    {
                        Id = o[idKey]?.ToString() ?? "",
                        Name = o["Name"]?.ToString() ?? ""
                    });
                }
            }
            FilterEntries();
        }

        private void FilterEntries()
        {
            var q = (EntrySearch ?? "").Trim();
            IEnumerable<TemplateEntryRow> rows = _allEntries;
            if (q.Length > 0)
                rows = rows.Where(r => r.Id.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
            Entries.Clear();
            foreach (var r in rows.OrderBy(r => r.Display, StringComparer.OrdinalIgnoreCase)) Entries.Add(r);
        }

        private void LoadEntryJson()
        {
            if (_root == null || SelectedCategory == null || SelectedEntry == null) return;
            if (_root[SelectedCategory.Key] is not JsonArray arr) return;
            var idKey = IdKeyFor(SelectedCategory.Key);
            foreach (var el in arr)
                if (el is JsonObject o && string.Equals(o[idKey]?.ToString() ?? "", SelectedEntry.Id, StringComparison.Ordinal))
                {
                    EntryJson = o.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                    return;
                }
        }

        private async Task SaveEntryAsync()
        {
            if (App.PM == null || SelectedCategory == null) { Status = "Pick a section first."; return; }
            if (string.IsNullOrWhiteSpace(EntryJson)) { Status = "Nothing to save."; return; }

            var key = SelectedCategory.Key;
            var err = await App.PM.SaveTemplateEntryAsync(key, EntryJson);
            if (err != null) { Status = err; return; }

            await LoadAsync();
            SelectedCategory = Categories.FirstOrDefault(c => c.Key == key);
            Status = "Saved to the campaign template.";
        }

        private async Task DeleteEntryAsync()
        {
            if (App.PM == null || SelectedCategory == null || SelectedEntry == null) { Status = "Pick an entry to delete."; return; }

            var key = SelectedCategory.Key;
            var err = await App.PM.DeleteTemplateEntryAsync(key, SelectedEntry.Id);
            if (err != null) { Status = err; return; }

            await LoadAsync();
            SelectedCategory = Categories.FirstOrDefault(c => c.Key == key);
            Status = "Deleted.";
        }

        private void NewEntry()
        {
            if (SelectedCategory == null) { Status = "Pick a section first."; return; }
            var idKey = IdKeyFor(SelectedCategory.Key);
            EntryJson = "{\n  \"" + idKey + "\": \"new-id\",\n  \"Name\": \"New entry\"\n}";
            SelectedEntry = null;
            Status = "New entry, set a unique id and the fields, then Save.";
        }

        private void RunValidation()
        {
            Issues.Clear();
            var list = TemplateValidator.Validate(_root?.ToJsonString());
            foreach (var i in list) Issues.Add(i);
            var errors = list.Count(i => i.Severity == "error");
            Status = list.Count == 0 ? "Validated, no problems found." : $"Validated: {errors} errors, {list.Count - errors} warnings.";
        }

        public async Task OpenMonstersAsync(bool withImport)
        {
            await LoadAsync();
            SelectedCategory = Categories.FirstOrDefault(c => c.Key == "Monsters");
            ImportOpen = withImport;
            if (withImport) Status = "Paste a statblock and import it, or pick a monster on the left to edit it.";
        }

        private async Task ImportStatblockAsync()
        {
            if (App.PM == null) { Status = "No campaign loaded."; return; }
            var parsed = StatblockParser.Parse(StatblockText, out var error);
            if (parsed == null) { Status = error; return; }

            var baseId = parsed["TemplateId"]!.GetValue<string>();
            var id = baseId;
            var taken = (_root?["Monsters"] as JsonArray)?.Select(m => (m as JsonObject)?["TemplateId"]?.ToString() ?? "").ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var n = 2; taken.Contains(id); n++) id = baseId + "-" + n;
            parsed["TemplateId"] = id;

            var err = await App.PM.SaveTemplateEntryAsync("Monsters", parsed.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            if (err != null) { Status = err; return; }

            var attackCount = (parsed["Attacks"] as JsonArray)?.Count ?? 0;
            StatblockText = "";
            await LoadAsync();
            SelectedCategory = Categories.FirstOrDefault(c => c.Key == "Monsters");
            Status = $"Imported {parsed["Name"]} as {id}, {attackCount} attacks. Check the entry, a pasted block never parses perfectly.";
        }

        private async Task RefreshCatalogAsync()
        {
            if (App.PM == null) { Status = "No campaign loaded."; return; }
            Status = "Refreshing catalog from the template...";
            var (rows, sections) = await App.PM.RefreshCatalogFromTemplateAsync();
            Status = rows == 0
                ? "Nothing to refresh."
                : $"Refreshed {rows} catalog rows across {sections} sections, custom entries left alone. Reopen the compendium to see it.";
        }
    }
}
