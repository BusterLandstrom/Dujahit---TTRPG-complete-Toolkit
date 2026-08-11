using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Threading;
using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Database;

namespace Dujahit.ViewModels
{
    public class DmCommandsViewModel : ViewModelBase
    {
        public ObservableCollection<CharacterRow> Characters { get; } = new();
        public ObservableCollection<GiftCurrency> Currencies { get; } = new();
        public ObservableCollection<AssigneeOption> AssignableOptions { get; } = new();

        private decimal _itemQuantity = 1;
        public decimal ItemQuantity
        {
            get => _itemQuantity;
            set => this.RaiseAndSetIfChanged(ref _itemQuantity, value < 1 ? 1 : value);
        }

        private string _status = "";
        public string Status
        {
            get => _status;
            set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        public ReactiveCommand<Unit, Unit> GiftMoneyCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearMoneyCommand { get; }
        public ReactiveCommand<Unit, Unit> GiftItemCommand { get; }

        public event Action GiftItemRequested;

        public DmCommandsViewModel()
        {
            GiftMoneyCommand = ReactiveCommand.CreateFromTask(GiftMoneyAsync);
            ClearMoneyCommand = ReactiveCommand.Create(() => { foreach (var c in Currencies) c.Amount = 0; });
            GiftItemCommand = ReactiveCommand.Create(() => GiftItemRequested?.Invoke());
            _ = LoadAsync();
        }

        public async Task LoadAsync()
        {
            if (App.PM?.GameDataRepo == null || App.PM?.DbManager == null) return;

            try
            {
                var currencies = await App.PM.GameDataRepo.LoadCurrenciesAsync();
                var userNames = await LoadUserNamesAsync();
                var players = await LoadAssignablePlayersAsync();
                var chars = await LoadCharactersAsync();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Currencies.Clear();
                    foreach (var c in currencies.OrderByDescending(x => x.EqualToBase))
                        Currencies.Add(new GiftCurrency(c.Id, c.Name, InventoryEngine.FallbackGlyph(c)));

                    AssignableOptions.Clear();
                    AssignableOptions.Add(new AssigneeOption(null, "Unassigned"));
                    foreach (var p in players) AssignableOptions.Add(new AssigneeOption(p.Id, p.Name));

                    Characters.Clear();
                    foreach (var ch in chars)
                    {
                        var ownerName = ch.OwnerUserId != null && userNames.TryGetValue(ch.OwnerUserId, out var nm) ? nm : ch.OwnerUserId;
                        var row = new CharacterRow(ch.Id, ch.Name, AssignableOptions)
                        {
                            VisibleToAll = ch.VisibleToAll,
                            OwnerLabel = ch.OwnerUserId == null ? "Unassigned" : "Assigned to " + ownerName
                        };
                        row.SetInitialAssignee(AssignableOptions.FirstOrDefault(o => string.Equals(o.UserId, ch.OwnerUserId, StringComparison.Ordinal))
                                              ?? AssignableOptions[0]);
                        row.Apply = () => _ = ApplyCharacterSettingsAsync(row);
                        Characters.Add(row);
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() => Status = "Could not load DM commands: " + ex.Message);
            }
        }

        private async Task<Dictionary<string, string>> LoadUserNamesAsync()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (App.PM?.DbManager == null) return map;
            await using var conn = await App.PM.DbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Username FROM Users";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) map[r.GetString(0)] = r.GetString(1);
            return map;
        }

        private async Task<List<(string Id, string Name)>> LoadAssignablePlayersAsync()
        {
            var list = new List<(string, string)>();
            if (App.PM?.DbManager == null) return list;
            var cid = App.PM.GetCampaignId();
            await using var conn = await App.PM.DbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT u.Id, u.Username
                FROM CampaignMembers cm
                JOIN Users u ON u.Id = cm.UserId
                WHERE cm.CampaignId = $cid AND cm.Role = 'player'
                ORDER BY u.Username COLLATE NOCASE
                """;
            cmd.Parameters.AddWithValue("$cid", cid);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add((r.GetString(0), r.GetString(1)));
            return list;
        }

        private async Task<List<(string Id, string Name, string? OwnerUserId, bool VisibleToAll)>> LoadCharactersAsync()
        {
            var list = new List<(string, string, string?, bool)>();
            if (App.PM?.DbManager == null) return list;
            var cid = App.PM.GetCampaignId();
            await using var conn = await App.PM.DbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Id, Name, OwnerUserId, VisibleToAll FROM Characters WHERE CampaignId = $cid AND CharacterKind = 'pc' ORDER BY Name COLLATE NOCASE";
            cmd.Parameters.AddWithValue("$cid", cid);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), !r.IsDBNull(3) && r.GetInt32(3) != 0));
            return list;
        }

        private async Task ApplyCharacterSettingsAsync(CharacterRow row)
        {
            if (App.PM?.DbManager == null) { Status = "Not connected."; return; }
            var oid = row.SelectedAssignee?.UserId;

            await using (var conn = await App.PM.DbManager.OpenAsync())
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "UPDATE Characters SET OwnerUserId = $oid, VisibleToAll = $vis WHERE Id = $cid";
                cmd.Parameters.AddWithValue("$oid", (object?)oid ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$vis", row.VisibleToAll ? 1 : 0);
                cmd.Parameters.AddWithValue("$cid", row.CharacterId);
                await cmd.ExecuteNonQueryAsync();
            }

            await App.PM.BroadcastCharacterAsync(row.CharacterId);
            row.OwnerLabel = oid == null ? "Unassigned" : "Assigned to " + (row.SelectedAssignee?.Name ?? oid);
            Status = "Updated " + row.Name + ".";
        }

        private async Task GiftMoneyAsync()
        {
            var targets = Characters.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0) { Status = "Tick at least one character first."; return; }
            if (App.PM?.GameDataRepo == null) { Status = "Not connected."; return; }

            var coins = Currencies.Where(c => (long)c.Amount != 0).ToList();
            if (coins.Count == 0) { Status = "Set an amount on at least one currency."; return; }

            foreach (var t in targets)
            {
                foreach (var c in coins)
                    await App.PM.GameDataRepo.AdjustWalletAsync(t.CharacterId, c.CurrencyId, (long)c.Amount);
                await App.PM.BroadcastCharacterAsync(t.CharacterId);
            }

            var who = string.Join(", ", targets.Select(t => t.Name));
            var what = string.Join(", ", coins.Select(c => (long)c.Amount + " " + c.Name));
            Status = "Gave " + what + " to " + who + ".";
            foreach (var c in Currencies) c.Amount = 0;
        }

        public async Task GiftItemToSelectedAsync(string baseItemId, string itemName)
        {
            var targets = Characters.Where(c => c.IsSelected).ToList();
            if (targets.Count == 0) { Status = "Tick at least one character first."; return; }
            if (App.PM?.GameDataRepo == null) { Status = "Not connected."; return; }

            var qty = (int)ItemQuantity;
            if (qty < 1) qty = 1;
            var cid = App.PM.GetCampaignId();

            foreach (var t in targets)
            {
                var inst = new ItemInstance
                {
                    Id = Guid.NewGuid().ToString("N"),
                    CampaignId = cid,
                    BaseItemId = baseItemId,
                    OwnerCharacterId = t.CharacterId,
                    Quantity = qty
                };
                await App.PM.GameDataRepo.SaveInstanceAsync(inst);
                await App.PM.BroadcastInstanceAsync(inst);
            }

            Status = "Gave " + qty + "x " + itemName + " to " + string.Join(", ", targets.Select(t => t.Name)) + ".";
        }

        public async Task<List<(string Id, string Name, string ItemType, string DataJson)>> LoadCatalogItemsAsync()
        {
            var result = new List<(string, string, string, string)>();
            if (App.PM?.DbManager == null) return result;
            var cid = App.PM.GetCampaignId();

            await using var conn = await App.PM.DbManager.OpenAsync();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT i.Id, i.Name, i.ItemType, i.DataJson
                    FROM Items i
                    INNER JOIN CampaignItems ci ON ci.ItemId = i.Id
                    WHERE ci.CampaignId = $cid AND ci.IsEnabled = 1
                    ORDER BY i.Name COLLATE NOCASE
                    """;
                cmd.Parameters.AddWithValue("$cid", cid);
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    result.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "{}" : r.GetString(3)));
            }

            if (result.Count == 0)
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT Id, Name, ItemType, {CatalogResolver.ResolvedJsonSql("Items", "Items")} FROM Items ORDER BY Name COLLATE NOCASE";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync())
                    result.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "{}" : r.GetString(3)));
            }
            return result;
        }
    }

    public class CharacterRow : ReactiveObject
    {
        public string CharacterId { get; }
        public string Name { get; }
        public ObservableCollection<AssigneeOption> Assignees { get; }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

        private bool _visibleToAll;
        public bool VisibleToAll { get => _visibleToAll; set => this.RaiseAndSetIfChanged(ref _visibleToAll, value); }

        private AssigneeOption? _selectedAssignee;
        public AssigneeOption? SelectedAssignee { get => _selectedAssignee; set => this.RaiseAndSetIfChanged(ref _selectedAssignee, value); }

        private string _ownerLabel = "Unassigned";
        public string OwnerLabel { get => _ownerLabel; set => this.RaiseAndSetIfChanged(ref _ownerLabel, value); }

        public Action? Apply;
        public ReactiveCommand<Unit, Unit> ApplyCommand { get; }

        public CharacterRow(string characterId, string name, ObservableCollection<AssigneeOption> assignees)
        {
            CharacterId = characterId;
            Name = name;
            Assignees = assignees;
            ApplyCommand = ReactiveCommand.Create(() => Apply?.Invoke());
        }

        public void SetInitialAssignee(AssigneeOption option) => _selectedAssignee = option;
    }

    public class AssigneeOption
    {
        public string? UserId { get; }
        public string Name { get; }
        public AssigneeOption(string? userId, string name) { UserId = userId; Name = name; }
        public override string ToString() => Name;
    }

    public class GiftCurrency : ReactiveObject
    {
        public string CurrencyId { get; }
        public string Name { get; }
        public string Glyph { get; }
        private decimal _amount;
        public decimal Amount { get => _amount; set => this.RaiseAndSetIfChanged(ref _amount, value); }
        public GiftCurrency(string currencyId, string name, string glyph) { CurrencyId = currencyId; Name = name; Glyph = glyph; }
    }
}