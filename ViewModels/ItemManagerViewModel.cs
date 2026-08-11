using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.Json;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class ItemListItemViewModel : ViewModelBase
    {
        public string Id { get; init; } = "";
        public string Slug { get; init; } = "";
        public string Name { get; init; } = "";
        public string ItemType { get; init; } = "";
        public List<string> Tags { get; init; } = new();
        public string TagsDisplay => Tags.Count == 0 ? "" : "#" + string.Join("  #", Tags);
        public string SlugDisplay => string.IsNullOrEmpty(Slug) ? "" : $"item:{Slug}";
    }

    public class ItemManagerViewModel : ViewModelBase
    {
        private readonly DatabaseManager _db;
        private readonly string _campaignId;
        private readonly string _userId;

        private readonly List<ItemListItemViewModel> _all = new();
        public ObservableCollection<ItemListItemViewModel> Visible { get; } = new();

        public ObservableCollection<string> AvailableTypes { get; } = new();
        public ObservableCollection<string> AvailableTags { get; } = new();

        private string _search = "";
        public string Search { get => _search; set { this.RaiseAndSetIfChanged(ref _search, value ?? ""); ApplyFilters(); } }

        private string _typeFilter = "";
        public string TypeFilter { get => _typeFilter; set { this.RaiseAndSetIfChanged(ref _typeFilter, value ?? ""); ApplyFilters(); } }

        private string _tagFilter = "";
        public string TagFilter { get => _tagFilter; set { this.RaiseAndSetIfChanged(ref _tagFilter, value ?? ""); ApplyFilters(); } }

        private ItemListItemViewModel? _selected;
        public ItemListItemViewModel? Selected
        {
            get => _selected;
            set => this.RaiseAndSetIfChanged(ref _selected, value);
        }

        public ReactiveCommand<Unit, Unit> CreateItemCommand { get; }
        public ReactiveCommand<Unit, Unit> EditSelectedCommand { get; }
        public ReactiveCommand<string, Unit> EditItemCommand { get; }
        public ReactiveCommand<string, Unit> ViewItemCommand { get; }
        public ReactiveCommand<string, Unit> DeleteSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearFiltersCommand { get; }

        public event Action<string>? OpenItemRequested;
        public event Action<string>? ViewItemRequested;
        public event Action<ItemDraft>? OpenCreateItemRequested;
        public event Func<string, string, Task<bool>>? ConfirmAsync;

        public ItemManagerViewModel(DatabaseManager db, string campaignId, string userId)
        {
            _db = db;
            _campaignId = campaignId;
            _userId = userId;

            CreateItemCommand = ReactiveCommand.Create(() =>
                OpenCreateItemRequested?.Invoke(new ItemDraft()));

            EditSelectedCommand = ReactiveCommand.Create(() =>
            {
                if (_selected != null) OpenItemRequested?.Invoke(_selected.Id);
            });

            EditItemCommand = ReactiveCommand.Create<string>(id =>
            {
                if (!string.IsNullOrEmpty(id)) OpenItemRequested?.Invoke(id);
            });

            ViewItemCommand = ReactiveCommand.Create<string>(id =>
            {
                if (!string.IsNullOrEmpty(id)) ViewItemRequested?.Invoke(id);
            });

            DeleteSelectedCommand = ReactiveCommand.CreateFromTask<string>(DeleteSelectedAsync);

            ClearFiltersCommand = ReactiveCommand.Create(() =>
            {
                Search = ""; TypeFilter = ""; TagFilter = "";
            });
        }

        public async Task LoadAsync()
        {
            _all.Clear();
            AvailableTypes.Clear();
            AvailableTags.Clear();
            Visible.Clear();

            await using var conn = await _db.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT i.Id, i.Slug, i.Name, i.ItemType, i.Tags
                FROM Items i
                INNER JOIN CampaignItems ci ON ci.ItemId = i.Id
                WHERE ci.CampaignId = $cid
                  AND i.Source      = 'custom'
                ORDER BY i.Name COLLATE NOCASE;
            """;
            cmd.Parameters.AddWithValue("$cid", _campaignId);

            var typeSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Weapon", "Armor", "Consumable", "Generic"
            };
            var tagSet = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var type = r.GetString(3);
                var tags = TagsJson.Parse(r.IsDBNull(4) ? null : r.GetString(4));
                typeSet.Add(type);
                foreach (var t in tags) tagSet.Add(t);

                _all.Add(new ItemListItemViewModel
                {
                    Id = r.GetString(0),
                    Slug = r.IsDBNull(1) ? "" : r.GetString(1),
                    Name = r.GetString(2),
                    ItemType = type,
                    Tags = tags
                });
            }

            foreach (var x in typeSet) AvailableTypes.Add(x);
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

            foreach (var it in _all)
            {
                if (!string.IsNullOrEmpty(search))
                {
                    var hay = $"{it.Name} {it.Slug}";
                    if (hay.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                if (!string.IsNullOrEmpty(_typeFilter) &&
                    !string.Equals(it.ItemType, _typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (requiredTags.Count > 0 &&
                    !requiredTags.All(req => it.Tags.Contains(req, StringComparer.OrdinalIgnoreCase)))
                    continue;

                Visible.Add(it);
            }
        }

        private async Task DeleteSelectedAsync(string id)
        {
            var row = _all.FirstOrDefault(x => x.Id == id);
            if (row == null) return;
            if (ConfirmAsync != null)
            {
                var ok = await ConfirmAsync("Delete item",
                    $"Delete custom item \"{row.Name}\"?\n\nAny <ref> tags pointing to it will become broken links, and existing ItemInstances will lose their base.");
                if (!ok) return;
            }

            await using var conn = await _db.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Items WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", row.Id);
            await cmd.ExecuteNonQueryAsync();

            _all.RemoveAll(x => x.Id == row.Id);
            Visible.Remove(row);
            if (Selected == row) Selected = null;
        }

        public async Task<bool> SlugAvailableAsync(string slug, string? excludeItemId = null)
        {
            if (!SlugHelper.IsValid(slug)) return false;
            await using var conn = await _db.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1 FROM Items
                WHERE Slug = $slug
                  AND ($excl IS NULL OR Id != $excl)
                LIMIT 1;
            """;
            cmd.Parameters.AddWithValue("$slug", slug);
            cmd.Parameters.AddWithValue("$excl", (object?)excludeItemId ?? DBNull.Value);
            return (await cmd.ExecuteScalarAsync()) == null;
        }

        public async Task<ItemListItemViewModel?> CreateItemAsync(ItemDraft draft)
        {
            var baseSlug = SlugHelper.IsValid(draft.Slug)
                ? draft.Slug
                : SlugHelper.Suggest(draft.Name);
            if (!SlugHelper.IsValid(baseSlug)) return null;

            var existing = new HashSet<string>(
                _all.Select(x => x.Slug).Where(s => !string.IsNullOrEmpty(s)));
            var slug = SlugHelper.EnsureUnique(baseSlug, existing);

            var id = Guid.NewGuid().ToString("N");
            var now = DateTime.UtcNow.ToString("o");
            var tagsJson = TagsJson.Serialise(draft.Tags);
            var dataJson = JsonSerializer.Serialize(draft.Data ?? new Dictionary<string, object?>());

            await using var conn = await _db.OpenAsync();

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO Items
                        (Id, Name, ItemType, Source, OwnerUserId, TemplateId,
                         RevisionNumber, UpdatedAt, DataJson, Slug, Tags)
                    VALUES
                        ($id, $name, $type, 'custom', $owner, NULL,
                         1, $now, $data, $slug, $tags);
                """;
                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$name", string.IsNullOrWhiteSpace(draft.Name) ? "Unnamed Item" : draft.Name.Trim());
                cmd.Parameters.AddWithValue("$type", draft.ItemType);
                cmd.Parameters.AddWithValue("$owner", _userId);
                cmd.Parameters.AddWithValue("$now", now);
                cmd.Parameters.AddWithValue("$data", dataJson);
                cmd.Parameters.AddWithValue("$slug", slug);
                cmd.Parameters.AddWithValue("$tags", tagsJson);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO CampaignItems (CampaignId, ItemId, AddedAt, IsEnabled)
                    VALUES ($cid, $iid, $now, 1);
                """;
                cmd.Parameters.AddWithValue("$cid", _campaignId);
                cmd.Parameters.AddWithValue("$iid", id);
                cmd.Parameters.AddWithValue("$now", now);
                await cmd.ExecuteNonQueryAsync();
            }

            var row = new ItemListItemViewModel
            {
                Id = id,
                Slug = slug,
                Name = draft.Name,
                ItemType = draft.ItemType,
                Tags = TagsJson.Parse(tagsJson)
            };
            _all.Add(row);
            ApplyFilters();
            return row;
        }
    }

    public class ItemDraft
    {
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public string ItemType { get; set; } = "Generic";
        public List<string> Tags { get; set; } = new();
        public Dictionary<string, object?>? Data { get; set; } = new();
    }
}