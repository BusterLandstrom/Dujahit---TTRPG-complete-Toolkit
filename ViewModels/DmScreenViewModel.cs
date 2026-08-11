using Dujahit.Models.Database;
using Dujahit.Models.DmScreen;
using Dujahit.Models.UI;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;
using Dujahit.Models;

namespace Dujahit.ViewModels
{
    public class DmScreenViewModel : ViewModelBase
    {
        private readonly DmScreenRepository _repo;
        private readonly string _campaignId;
        private readonly string? _userId;
        private readonly HashSet<string> _hidden = new(StringComparer.OrdinalIgnoreCase);
        private bool _suppressShownPersist;

        public ObservableCollection<DmScreenPanel> Panels { get; } = new();
        public ObservableCollection<DmScreenPanel> AllPanels { get; } = new();

        public Action<string, bool>? RollToChat { get; set; }

        private bool _isBusy;
        public bool IsBusy { get => _isBusy; set => this.RaiseAndSetIfChanged(ref _isBusy, value); }

        private bool _chooserOpen;
        public bool ChooserOpen { get => _chooserOpen; set => this.RaiseAndSetIfChanged(ref _chooserOpen, value); }

        public ReactiveCommand<Unit, Unit> AddPanelCommand { get; }
        public ReactiveCommand<Unit, Unit> ReloadCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleChooserCommand { get; }

        public DmScreenViewModel()
        {
            _repo = App.PM.DmScreenRepo;
            _campaignId = App.PM.GetCampaignId();
            _userId = App.PM.GetUID();

            AddPanelCommand = ReactiveCommand.CreateFromTask(AddPanelAsync);
            ReloadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
            ToggleChooserCommand = ReactiveCommand.Create(() => { ChooserOpen = !ChooserOpen; });

            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            try
            {
                IsBusy = true;

                _hidden.Clear();
                var stored = await App.PM.GetSettingAsync("dmscreen_hidden_" + _campaignId);
                if (!string.IsNullOrEmpty(stored))
                    foreach (var id in stored.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        _hidden.Add(id);

                _suppressShownPersist = true;
                foreach (var old in AllPanels) old.PropertyChanged -= OnPanelShownChanged;
                AllPanels.Clear();

                foreach (var p in await BuildTemplatePanelsAsync())
                    TrackPanel(p);

                foreach (var row in await _repo.GetPanelsAsync(_campaignId))
                    TrackPanel(MakeCustomPanel(row.Id, row.Title, row.Content, row.SortOrder));

                _suppressShownPersist = false;
                RebuildVisible();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[DmScreen] Load failed", ex);
            }
            finally { IsBusy = false; }
        }

        private void TrackPanel(DmScreenPanel p)
        {
            p.IsShown = !_hidden.Contains(p.Id);
            p.PropertyChanged += OnPanelShownChanged;
            AllPanels.Add(p);
        }

        private async void OnPanelShownChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                if (_suppressShownPersist || e.PropertyName != nameof(DmScreenPanel.IsShown)) return;
                if (sender is not DmScreenPanel p) return;
                if (p.IsShown) _hidden.Remove(p.Id);
                else _hidden.Add(p.Id);
                RebuildVisible();
                try
                {
                    await App.PM.SetSettingAsync("dmscreen_hidden_" + _campaignId, string.Join(",", _hidden));
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("[DmScreen] hidden set save failed", ex);
                }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnPanelShownChanged", ex); }
        }

        private void RebuildVisible()
        {
            Panels.Clear();
            foreach (var p in AllPanels)
                if (p.IsShown) Panels.Add(p);
        }

        private DmScreenPanel MakeCustomPanel(string id, string title, string content, int sort)
            => new(id, title, content, sort, isTemplate: false,
                   onSave: SavePanelAsync, onDelete: DeletePanelAsync, onRoll: RollChip);

        private void RollChip(DmScreenPanel panel, string expr)
        {
            if (!DiceManager.TryRoll(expr, out var result) || result == null) return;
            var line = "rolled " + expr + " (" + panel.Title + ") = " + result.Total + "   (" + result.Breakdown + ")";
            RollToChat?.Invoke(line, false);
        }

        private async Task AddPanelAsync()
        {
            var id = Guid.NewGuid().ToString();
            var sort = AllPanels.Count;
            var panel = MakeCustomPanel(id, "New Panel", "", sort);
            await _repo.UpsertPanelAsync(id, _campaignId, _userId, panel.Title, panel.Content, sort);
            TrackPanel(panel);
            RebuildVisible();
            panel.IsEditing = true;
        }

        private Task SavePanelAsync(DmScreenPanel p)
            => _repo.UpsertPanelAsync(p.Id, _campaignId, _userId, p.Title, p.Content, p.SortOrder);

        private async Task DeletePanelAsync(DmScreenPanel p)
        {
            await _repo.DeletePanelAsync(p.Id);
            p.PropertyChanged -= OnPanelShownChanged;
            AllPanels.Remove(p);
            Panels.Remove(p);
        }

        private async Task<List<DmScreenPanel>> BuildTemplatePanelsAsync()
        {
            var result = new List<DmScreenPanel>();
            var json = await _repo.GetTemplateJsonForCampaignAsync(_campaignId);

            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("DmScreenPanels", out var panels)
                        && panels.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in panels.EnumerateArray())
                        {
                            var id = p.TryGetProperty("TemplateId", out var pid) ? pid.GetString() : null;
                            var title = p.TryGetProperty("Title", out var pt) ? pt.GetString() : null;
                            var content = p.TryGetProperty("Content", out var pc) ? pc.GetString() : null;
                            if (string.IsNullOrWhiteSpace(title)) continue;
                            var order = p.TryGetProperty("SortOrder", out var so) && so.TryGetInt32(out var sov) ? sov : 0;
                            result.Add(new DmScreenPanel(id ?? $"tmpl-panel-{title}", title!, content ?? "",
                                                         sortOrder: -2000 + order, isTemplate: true, onRoll: RollChip));
                        }
                    }

                    // Looks for "Conditions": [ { "Name": ..., "Description": ... }, ... ]
                    if (doc.RootElement.TryGetProperty("Conditions", out var conditions)
                        && conditions.ValueKind == JsonValueKind.Array)
                    {
                        int i = 0;
                        foreach (var c in conditions.EnumerateArray())
                        {
                            var name = c.TryGetProperty("Name", out var n) ? n.GetString() : null;
                            var desc = c.TryGetProperty("Description", out var d) ? d.GetString() : null;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            result.Add(new DmScreenPanel($"tmpl-cond-{i}", name!, desc ?? "",
                                                         sortOrder: -1000 + i++, isTemplate: true, onRoll: RollChip));
                        }
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.Log($"[DmScreen] Template parse failed", ex);
                }
            }

            return result;
        }
    }
}
