using Avalonia.Threading;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;
using Dujahit.Models.Application;

namespace Dujahit.ViewModels
{
    public class CompendiumEntry
    {
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Detail { get; set; } = "";
        public ItemDisplay? Item { get; set; }
        public bool IsItem => Item != null;
        public string Version { get; set; } = "";
        public bool HasVersion => Version.Length > 0;
        public string DataJson { get; set; } = "";
        public string ItemType { get; set; } = "";
    }

    public class CompendiumViewModel : ViewModelBase
    {
        private readonly DatabaseManager _db;
        private readonly string _campaignId;
        private readonly bool _isDm;

        private readonly List<CompendiumEntry> _all = new();

        public ObservableCollection<CompendiumEntry> Entries { get; } = new();
        public bool ShowMonsters => _isDm;

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set => this.RaiseAndSetIfChanged(ref _searchText, value);
        }

        private string _selectedCategory = "All";
        public string SelectedCategory
        {
            get => _selectedCategory;
            set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
        }

        private string _versionFilter = "both";
        public string VersionFilter
        {
            get => _versionFilter;
            set => this.RaiseAndSetIfChanged(ref _versionFilter, value);
        }

        public ReactiveCommand<string, Unit> SetCategoryCommand { get; }
        public ReactiveCommand<string, Unit> SetVersionCommand { get; }

        public CompendiumViewModel(DatabaseManager db, string campaignId, bool isDm)
        {
            _db = db;
            _campaignId = campaignId;
            _isDm = isDm;
            _versionFilter = App.PM?.GetRulesVersionFilter() ?? "both";

            SetCategoryCommand = ReactiveCommand.Create<string>(c => SelectedCategory = c ?? "All");
            SetVersionCommand = ReactiveCommand.CreateFromTask<string>(async v =>
            {
                VersionFilter = (v == "2014" || v == "2024") ? v : "both";
                if (App.PM != null) await App.PM.SetRulesVersionFilterAsync(VersionFilter);
            });

            this.WhenAnyValue(x => x.SearchText, x => x.SelectedCategory, x => x.VersionFilter)
                .Subscribe(_ => ApplyFilter());
        }

        public async Task LoadAsync()
        {
            var loaded = new List<CompendiumEntry>();

            await using var conn = await _db.OpenAsync();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT Name, Level, School, Description, Version, {CatalogResolver.ResolvedJsonSql("Spells", "Spells")} FROM Spells ORDER BY Name COLLATE NOCASE";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    loaded.Add(new CompendiumEntry
                    {
                        Category = "Spells",
                        Name = r.GetString(0),
                        Subtitle = $"Level {r.GetInt32(1)} \u00B7 {r.GetString(2)}",
                        Detail = r.IsDBNull(3) ? "" : r.GetString(3),
                        Version = r.IsDBNull(4) ? "" : r.GetString(4),
                        DataJson = r.IsDBNull(5) ? "" : r.GetString(5)
                    });
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT Name, ItemType, {CatalogResolver.ResolvedJsonSql("Items", "Items")}, Version FROM Items ORDER BY Name COLLATE NOCASE";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var itemType = r.IsDBNull(1) ? "" : r.GetString(1);
                    var dataJson = r.IsDBNull(2) ? "" : r.GetString(2);
                    loaded.Add(new CompendiumEntry
                    {
                        Category = "Items",
                        Name = r.GetString(0),
                        Subtitle = itemType,
                        Detail = DescriptionFromJson(dataJson),
                        Item = ItemDisplay.FromJson(r.GetString(0), itemType, dataJson),
                        Version = r.IsDBNull(3) ? "" : r.GetString(3),
                        DataJson = dataJson,
                        ItemType = itemType
                    });
                }
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"SELECT Name, Version, {CatalogResolver.ResolvedJsonSql("Races", "Races")} FROM Races ORDER BY Name COLLATE NOCASE";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    var dj = r.IsDBNull(2) ? "" : r.GetString(2);
                    loaded.Add(new CompendiumEntry
                    {
                        Category = "Species",
                        Name = r.GetString(0),
                        Subtitle = "Species",
                        Detail = DescriptionFromJson(dj),
                        Version = r.IsDBNull(1) ? "" : r.GetString(1),
                        DataJson = dj
                    });
                }
            }

            if (_isDm)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT Name, Level, CurrentHp, MaxHp FROM Characters
                    WHERE CampaignId = $cid AND CharacterKind = 'npc'
                    ORDER BY Name COLLATE NOCASE";
                cmd.Parameters.AddWithValue("$cid", _campaignId);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                {
                    loaded.Add(new CompendiumEntry
                    {
                        Category = "Monsters",
                        Name = r.GetString(0),
                        Subtitle = $"NPC · Level {r.GetInt32(1)}",
                        Detail = $"{r.GetInt32(2)}/{r.GetInt32(3)} HP"
                    });
                }
            }

            string templateJson = "";
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT JsonContent FROM CampaignTemplates ORDER BY ImportedAt DESC LIMIT 1";
                templateJson = await cmd.ExecuteScalarAsync() as string ?? "";
            }
            if (templateJson.Length > 0)
            {
                try
                {
                    using var doc = JsonDocument.Parse(templateJson);
                    var root = doc.RootElement;

                    if (_isDm && root.TryGetProperty("Monsters", out var mons) && mons.ValueKind == JsonValueKind.Array)
                        foreach (var m in mons.EnumerateArray())
                            loaded.Add(new CompendiumEntry
                            {
                                Category = "Monsters",
                                Name = Str(m, "Name"),
                                Subtitle = $"CR {Str(m, "ChallengeRating")} · {Str(m, "Type")}",
                                Detail = Str(m, "Description"),
                                Version = Str(m, "Version")
                            });

                    if (root.TryGetProperty("Feats", out var feats) && feats.ValueKind == JsonValueKind.Array)
                        foreach (var f in feats.EnumerateArray())
                            loaded.Add(new CompendiumEntry
                            {
                                Category = "Feats",
                                Name = Str(f, "Name"),
                                Subtitle = Str(f, "Prerequisites"),
                                Detail = Str(f, "Description"),
                                Version = Str(f, "Version")
                            });

                    if (root.TryGetProperty("Backgrounds", out var bgs) && bgs.ValueKind == JsonValueKind.Array)
                        foreach (var b in bgs.EnumerateArray())
                            loaded.Add(new CompendiumEntry
                            {
                                Category = "Backgrounds",
                                Name = Str(b, "Name"),
                                Subtitle = "Background",
                                Detail = Str(b, "Description"),
                                Version = Str(b, "Version")
                            });

                    if (root.TryGetProperty("Deities", out var deis) && deis.ValueKind == JsonValueKind.Array)
                        foreach (var d in deis.EnumerateArray())
                            loaded.Add(new CompendiumEntry
                            {
                                Category = "Deities",
                                Name = Str(d, "Name"),
                                Subtitle = Str(d, "Domain"),
                                Detail = Str(d, "Description")
                            });

                    if (root.TryGetProperty("Alignments", out var als) && als.ValueKind == JsonValueKind.Array)
                        foreach (var a in als.EnumerateArray())
                            loaded.Add(new CompendiumEntry
                            {
                                Category = "Alignments",
                                Name = Str(a, "Name"),
                                Detail = Str(a, "Description")
                            });
                }
                catch (JsonException) { }
            }

            Dispatcher.UIThread.Post(() =>
            {
                _all.Clear();
                _all.AddRange(loaded);
                ApplyFilter();
            });
        }

        private void ApplyFilter()
        {
            var search = (SearchText ?? "").Trim();
            IEnumerable<CompendiumEntry> q = _all;

            if (SelectedCategory != "All")
                q = q.Where(e => e.Category == SelectedCategory);

            if (_versionFilter != "both")
                q = q.Where(e => App.PM?.Rules?.VisibleInEdition(e.Version, _versionFilter) ?? (!e.HasVersion || e.Version == _versionFilter));

            if (search.Length > 0)
                q = q.Where(e => e.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

            Entries.Clear();
            foreach (var e in q) Entries.Add(e);
        }

        private static string Str(JsonElement el, string key) =>
            el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";

        private static string DescriptionFromJson(string dataJson)
        {
            if (string.IsNullOrWhiteSpace(dataJson)) return "";
            try
            {
                using var doc = JsonDocument.Parse(dataJson);
                if (doc.RootElement.TryGetProperty("Description", out var d))
                    return d.GetString() ?? "";
            }
            catch (JsonException) { }
            return "";
        }
    }
}
