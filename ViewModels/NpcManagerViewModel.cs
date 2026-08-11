using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using Microsoft.Data.Sqlite;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class NpcListItemViewModel : ViewModelBase
    {
        public string Id { get; init; } = "";
        public string Slug { get; init; } = "";
        public string Name { get; init; } = "";
        public string Race { get; init; } = "";
        public string Class { get; init; } = "";
        public int Level { get; init; }
        public List<string> Tags { get; init; } = new();

        public string TagsDisplay => Tags.Count == 0 ? "" : "#" + string.Join("  #", Tags);
        public string SlugDisplay => string.IsNullOrEmpty(Slug) ? "" : $"npc:{Slug}";
    }

    public class NpcManagerViewModel : ViewModelBase
    {
        private readonly DatabaseManager _db;
        private readonly string _campaignId;
        private readonly string _userId;

        private readonly List<NpcListItemViewModel> _all = new();
        public ObservableCollection<NpcListItemViewModel> Visible { get; } = new();

        public ObservableCollection<string> AvailableRaces { get; } = new();
        public ObservableCollection<string> AvailableClasses { get; } = new();
        public ObservableCollection<string> AvailableTags { get; } = new();

        private string _search = "";
        public string Search
        {
            get => _search;
            set { this.RaiseAndSetIfChanged(ref _search, value ?? ""); ApplyFilters(); }
        }

        private string _raceFilter = "";
        public string RaceFilter
        {
            get => _raceFilter;
            set { this.RaiseAndSetIfChanged(ref _raceFilter, value ?? ""); ApplyFilters(); }
        }

        private string _classFilter = "";
        public string ClassFilter
        {
            get => _classFilter;
            set { this.RaiseAndSetIfChanged(ref _classFilter, value ?? ""); ApplyFilters(); }
        }

        private string _tagFilter = "";
        public string TagFilter
        {
            get => _tagFilter;
            set { this.RaiseAndSetIfChanged(ref _tagFilter, value ?? ""); ApplyFilters(); }
        }

        private NpcListItemViewModel? _selected;
        public NpcListItemViewModel? Selected
        {
            get => _selected;
            set => this.RaiseAndSetIfChanged(ref _selected, value);
        }

        public ReactiveCommand<Unit, Unit> CreateNpcCommand { get; }
        public ReactiveCommand<Unit, Unit> CreateMonsterCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportStatblockCommand { get; }
        public ReactiveCommand<string, Unit> EditSelectedCommand { get; }
        public ReactiveCommand<string, Unit> ViewSelectedCommand { get; }
        public ReactiveCommand<string, Unit> DeleteSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearFiltersCommand { get; }

        public event Action<string>? OpenNpcRequested;
        public event Action<string>? ViewNpcRequested;
        public event Action? OpenNpcCreatorRequested;
        public event Action<bool>? OpenMonsterEditorRequested;
        public event Func<string, string, Task<bool>>? ConfirmAsync;

        public NpcManagerViewModel(DatabaseManager db, string campaignId, string userId)
        {
            _db = db;
            _campaignId = campaignId;
            _userId = userId;

            CreateNpcCommand = ReactiveCommand.Create(() => OpenNpcCreatorRequested?.Invoke());
            CreateMonsterCommand = ReactiveCommand.Create(() => OpenMonsterEditorRequested?.Invoke(false));
            ImportStatblockCommand = ReactiveCommand.Create(() => OpenMonsterEditorRequested?.Invoke(true));

            EditSelectedCommand = ReactiveCommand.Create<string>(id =>
            {
                if (!string.IsNullOrEmpty(id)) OpenNpcRequested?.Invoke(id);
            });

            ViewSelectedCommand = ReactiveCommand.Create<string>(id =>
            {
                if (!string.IsNullOrEmpty(id)) ViewNpcRequested?.Invoke(id);
            });

            DeleteSelectedCommand = ReactiveCommand.CreateFromTask<string>(DeleteSelectedAsync);

            ClearFiltersCommand = ReactiveCommand.Create(() =>
            {
                Search = ""; RaceFilter = ""; ClassFilter = ""; TagFilter = "";
            });
        }

        public async Task LoadAsync()
        {
            _all.Clear();
            AvailableRaces.Clear();
            AvailableClasses.Clear();
            AvailableTags.Clear();
            Visible.Clear();

            await using var conn = await _db.OpenAsync();
            await using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                SELECT c.Id, c.Slug, c.Name, c.Level, c.Tags,
                       r.Name AS RaceName,
                       cl.Name AS ClassName
                FROM Characters c
                LEFT JOIN Races   r  ON r.Id  = c.RaceId
                LEFT JOIN Classes cl ON cl.Id = c.ClassId
                WHERE c.CampaignId    = $cid
                  AND c.CharacterKind = 'npc'
                ORDER BY c.Name COLLATE NOCASE;
            """;
            cmd.Parameters.AddWithValue("$cid", _campaignId);

            var raceSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var classSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var tagSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var race = r.IsDBNull(5) ? "" : r.GetString(5);
                var className = r.IsDBNull(6) ? "" : r.GetString(6);
                var tags = TagsJson.Parse(r.IsDBNull(4) ? null : r.GetString(4));

                if (!string.IsNullOrEmpty(race)) raceSet.Add(race);
                if (!string.IsNullOrEmpty(className)) classSet.Add(className);
                foreach (var t in tags) tagSet.Add(t);

                _all.Add(new NpcListItemViewModel
                {
                    Id = r.GetString(0),
                    Slug = r.IsDBNull(1) ? "" : r.GetString(1),
                    Name = r.GetString(2),
                    Level = r.GetInt32(3),
                    Tags = tags,
                    Race = race,
                    Class = className
                });
            }

            foreach (var x in raceSet) AvailableRaces.Add(x);
            foreach (var x in classSet) AvailableClasses.Add(x);
            foreach (var x in tagSet) AvailableTags.Add(x);

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            Visible.Clear();

            var search = _search.Trim();
            var requiredTags = _tagFilter
                .Split(new[] { ',', ' ', '#' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().ToLowerInvariant())
                .Where(s => s.Length > 0)
                .ToList();

            foreach (var npc in _all)
            {
                if (!string.IsNullOrEmpty(search))
                {
                    var hay = $"{npc.Name} {npc.Slug}";
                    if (hay.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                if (!string.IsNullOrEmpty(_raceFilter) &&
                    !string.Equals(npc.Race, _raceFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrEmpty(_classFilter) &&
                    !string.Equals(npc.Class, _classFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (requiredTags.Count > 0 &&
                    !requiredTags.All(req => npc.Tags.Contains(req, StringComparer.OrdinalIgnoreCase)))
                    continue;

                Visible.Add(npc);
            }
        }

        private async Task DeleteSelectedAsync(string id)
        {
            var row = _all.FirstOrDefault(x => x.Id == id);
            if (row == null) return;

            if (ConfirmAsync != null)
            {
                var ok = await ConfirmAsync("Delete NPC",
                    $"Delete NPC \"{row.Name}\"?\n\nAny <ref> tags pointing to this NPC will become broken links.");
                if (!ok) return;
            }

            await using var conn = await _db.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Characters WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", row.Id);
            await cmd.ExecuteNonQueryAsync();

            _all.RemoveAll(x => x.Id == row.Id);
            Visible.Remove(row);
            if (Selected == row) Selected = null;
        }

        public async Task<bool> SlugAvailableAsync(string slug, string? excludeCharacterId = null)
        {
            if (!SlugHelper.IsValid(slug)) return false;
            await using var conn = await _db.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1 FROM Characters
                WHERE CampaignId = $cid AND Slug = $slug
                  AND ($excl IS NULL OR Id != $excl)
                LIMIT 1;
            """;
            cmd.Parameters.AddWithValue("$cid", _campaignId);
            cmd.Parameters.AddWithValue("$slug", slug);
            cmd.Parameters.AddWithValue("$excl", (object?)excludeCharacterId ?? DBNull.Value);
            return (await cmd.ExecuteScalarAsync()) == null;
        }

    }

    public record RefOption(string Id, string Name)
    {
        public override string ToString() => Name;
    }
}