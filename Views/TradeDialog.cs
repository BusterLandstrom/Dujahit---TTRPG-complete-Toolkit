using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dujahit.Models;
using Dujahit.Models.Communication;
using Avalonia.Media;

namespace Dujahit.Views
{
    public class TradeDialog : DialogWindow
    {
        public class TradeTarget
        {
            public string UserId { get; }
            public string CharacterId { get; }
            public string Name { get; }
            public TradeTarget(string userId, string characterId, string name) { UserId = userId; CharacterId = characterId; Name = name; }
            public override string ToString() => Name;
        }

        private readonly bool _iAmFrom;
        private readonly string _tradeId;
        private readonly string _myUserId;
        private readonly string _myCharId;
        private readonly string _myCharName;
        private readonly List<Currency> _currencies;

        private bool _opened;
        private bool _completed;
        private TradeOfferMessage? _lastOffer;

        private string _targetUserId = "";
        private string _targetCharId = "";
        private string _targetName = "";

        private readonly ComboBox? _targetCombo;
        private readonly List<(Currency Cur, NumericUpDown Up)> _currencyInputs = new();
        private readonly List<(TradeItemLine Line, CheckBox Box)> _itemChecks = new();
        private readonly TextBlock _theirText;
        private readonly TextBlock _statusText;
        private readonly TextBlock _youAccepted;
        private readonly TextBlock _themAccepted;

        public static async Task OpenInitiatorAsync(Window owner, string myCharId, string myCharName, string? myOwnerUserId)
        {
            if (App.PM?.GameDataRepo == null) return;
            var myUserId = string.IsNullOrEmpty(myOwnerUserId) ? App.PM.GetUID() : myOwnerUserId!;
            var campaignId = App.PM.GetCampaignId();
            var currencies = await App.PM.GameDataRepo.LoadCurrenciesAsync();
            var myWallet = await App.PM.GameDataRepo.LoadWalletAsync(myCharId);
            var myItems = await LoadMyItemsAsync(myCharId);
            var targets = await LoadTargetsAsync(campaignId, myCharId);
            var dlg = new TradeDialog(true, campaignId, myUserId, myCharId, myCharName, currencies, myItems, myWallet, targets, null);
            await dlg.ShowDialog(owner);
        }

        public static async Task OpenRecipientAsync(Window owner, TradeOfferMessage offer)
        {
            if (App.PM?.GameDataRepo == null) return;
            var myUserId = App.PM.GetUID();
            var myCharId = offer.To.CharacterId;
            var myCharName = offer.To.CharacterName;
            var campaignId = App.PM.GetCampaignId();
            var currencies = await App.PM.GameDataRepo.LoadCurrenciesAsync();
            var myWallet = await App.PM.GameDataRepo.LoadWalletAsync(myCharId);
            var myItems = await LoadMyItemsAsync(myCharId);
            var dlg = new TradeDialog(false, campaignId, myUserId, myCharId, myCharName, currencies, myItems, myWallet, null, offer);
            await dlg.ShowDialog(owner);
        }

        public TradeDialog(bool iAmFrom, string campaignId, string myUserId, string myCharId, string myCharName,
            List<Currency> currencies, List<TradeItemLine> myItems, Dictionary<string, long> myWallet,
            List<TradeTarget>? targets, TradeOfferMessage? incoming)
        {
            _iAmFrom = iAmFrom;
            _myUserId = myUserId;
            _myCharId = myCharId;
            _myCharName = myCharName;
            _currencies = currencies;

            if (iAmFrom)
            {
                _tradeId = Guid.NewGuid().ToString("N");
                _opened = false;
            }
            else
            {
                _tradeId = incoming!.TradeId;
                _opened = true;
                _lastOffer = incoming;
                _targetUserId = incoming.From.UserId;
                _targetCharId = incoming.From.CharacterId;
                _targetName = incoming.From.CharacterName;
            }

            Title = "Trade";
            Width = 560;
            Height = 660;
            CanResize = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = new StackPanel { Margin = new Thickness(20), Spacing = 12 };

            root.Children.Add(new TextBlock { Text = "Set what you give, send the offer, both sides accept to seal it.", FontSize = 12, Opacity = 0.7 });

            if (iAmFrom)
            {
                _targetCombo = new ComboBox { PlaceholderText = "Choose a player", HorizontalAlignment = HorizontalAlignment.Stretch };
                _targetCombo.ItemsSource = targets;
                root.Children.Add(new TextBlock { Text = "Trade with", FontWeight = FontWeight.SemiBold });
                root.Children.Add(_targetCombo);
                if (targets != null && targets.Count == 0)
                    root.Children.Add(new TextBlock { Text = "No other players with a character to trade with yet.", FontSize = 12, Opacity = 0.6 });
            }
            else
            {
                root.Children.Add(new TextBlock { Text = "Trading with " + _targetName, FontWeight = FontWeight.SemiBold });
            }

            root.Children.Add(new TextBlock { Text = "You give", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });

            var giveBox = new StackPanel { Spacing = 6 };
            foreach (var cur in currencies)
            {
                var have = myWallet.TryGetValue(cur.Id, out var v) ? v : 0;
                var up = new NumericUpDown
                {
                    Minimum = 0,
                    Maximum = have,
                    Increment = 1,
                    Value = 0,
                    Width = 120,
                    FormatString = "0"
                };
                var label = new TextBlock { Text = CurrencyLabel(cur.Id) + "  (have " + have + ")", VerticalAlignment = VerticalAlignment.Center, Width = 200 };
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { label, up } };
                giveBox.Children.Add(row);
                _currencyInputs.Add((cur, up));
            }

            if (myItems.Count == 0)
            {
                giveBox.Children.Add(new TextBlock { Text = "No tradeable item instances on this character yet.", FontSize = 12, Opacity = 0.6, Margin = new Thickness(0, 4, 0, 0) });
            }
            else
            {
                foreach (var line in myItems)
                {
                    var box = new CheckBox { Content = line.Quantity > 1 ? line.Name + "  x" + line.Quantity : line.Name };
                    giveBox.Children.Add(box);
                    _itemChecks.Add((line, box));
                }
            }
            root.Children.Add(giveBox);

            root.Children.Add(new TextBlock { Text = "They give", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
            _theirText = new TextBlock { Text = "(nothing yet)", TextWrapping = TextWrapping.Wrap, Opacity = 0.85 };
            root.Children.Add(_theirText);

            _youAccepted = new TextBlock { Text = "You: pending", FontSize = 12 };
            _themAccepted = new TextBlock { Text = "Them: pending", FontSize = 12 };
            root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(0, 6, 0, 0), Children = { _youAccepted, _themAccepted } });

            _statusText = new TextBlock { Text = "", FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
            root.Children.Add(_statusText);

            var send = new Button { Content = "Send / Update Offer", Width = 160, Classes = { "primary" } };
            var accept = new Button { Content = "Accept", Width = 110, Classes = { "accent" } };
            var cancel = new Button { Content = "Close", Width = 90, IsCancel = true, Classes = { "ghost" } };
            send.Click += (_, _) => { _ = OnSendClicked(); };
            accept.Click += (_, _) => { _ = OnAcceptClicked(); };
            cancel.Click += (_, _) => Close();
            root.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(0, 6, 0, 0),
                Children = { cancel, accept, send }
            });

            Content = new ScrollViewer { Content = root };

            if (incoming != null) RenderTheirSide(incoming);
            if (incoming != null) UpdateAcceptedIndicators(incoming);

            Sub();
            Closed += OnClosed;
        }

        private async void OnClosed(object? sender, EventArgs e)
        {
            try
            {
                Unsub();
                var com = App.PM?.ComController;
                if (com == null) return;
                try
                {
                    if (_opened && !_completed && _lastOffer != null)
                        await com.CancelTradeAsync(_lastOffer);
                }
                catch { }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnClosed", ex); }
        }

        private async Task OnSendClicked()
        {
            var com = App.PM?.ComController;
            if (com == null) { SetStatus("Not connected."); return; }
            if (!ResolveTargetFromUi()) { SetStatus("Pick someone to trade with."); return; }

            var mine = BuildMySide(false);
            var theirs = TheirSideFromLast() with { Accepted = false };
            var offer = Compose(mine, theirs);
            _lastOffer = offer;
            try
            {
                if (_iAmFrom && !_opened) { _opened = true; await com.OpenTradeAsync(offer); }
                else await com.UpdateTradeAsync(offer);
                SetStatus("Offer sent.");
            }
            catch (Exception ex) { SetStatus("Send failed: " + ex.Message); }
            RenderTheirSide(offer);
            UpdateAcceptedIndicators(offer);
        }

        private async Task OnAcceptClicked()
        {
            var com = App.PM?.ComController;
            if (com == null) { SetStatus("Not connected."); return; }
            if (!ResolveTargetFromUi()) { SetStatus("Pick someone to trade with."); return; }

            var mine = BuildMySide(true);
            var theirs = TheirSideFromLast();
            var offer = Compose(mine, theirs);
            _lastOffer = offer;
            try
            {
                if (_iAmFrom && !_opened) { _opened = true; await com.OpenTradeAsync(offer); }
                else await com.RespondTradeAsync(offer);
                SetStatus("You accepted, waiting for the other side.");
            }
            catch (Exception ex) { SetStatus("Accept failed: " + ex.Message); }
            UpdateAcceptedIndicators(offer);
        }

        private void OnTradeUpdated(TradeOfferMessage offer)
        {
            if (offer.TradeId != _tradeId) return;
            _lastOffer = offer;
            RenderTheirSide(offer);
            UpdateAcceptedIndicators(offer);
            SetStatus("Offer updated.");
        }

        private void OnTradeCancelled(TradeOfferMessage offer)
        {
            if (offer.TradeId != _tradeId) return;
            _completed = true;
            SetStatus("Trade cancelled by the other side.");
            Close();
        }

        private void OnTradeResult(TradeResultMessage res)
        {
            if (res.TradeId != _tradeId) return;
            if (res.Success)
            {
                _completed = true;
                SetStatus("Trade complete. " + res.Summary);
                Close();
            }
            else
            {
                SetStatus("Trade failed: " + (res.Reason ?? "unknown"));
            }
        }

        private TradeSide BuildMySide(bool accepted)
        {
            var items = new List<TradeItemLine>();
            foreach (var (line, box) in _itemChecks)
                if (box.IsChecked == true) items.Add(line);
            var currency = new List<TradeCurrencyLine>();
            foreach (var (cur, up) in _currencyInputs)
            {
                var amt = (long)(up.Value ?? 0m);
                if (amt > 0) currency.Add(new TradeCurrencyLine(cur.Id, amt));
            }
            return new TradeSide(_myUserId, _myCharId, _myCharName, items, currency, accepted);
        }

        private TradeSide TheirSideFromLast()
        {
            if (_lastOffer == null)
                return new TradeSide(_targetUserId, _targetCharId, _targetName, new(), new(), false);
            return _iAmFrom ? _lastOffer.To : _lastOffer.From;
        }

        private TradeOfferMessage Compose(TradeSide mine, TradeSide theirs)
            => _iAmFrom ? new TradeOfferMessage(_tradeId, mine, theirs) : new TradeOfferMessage(_tradeId, theirs, mine);

        private void RenderTheirSide(TradeOfferMessage offer)
        {
            var their = _iAmFrom ? offer.To : offer.From;
            var parts = new List<string>();
            foreach (var c in their.Currency) parts.Add(c.Amount + " " + CurrencyLabel(c.CurrencyId));
            foreach (var it in their.Items) parts.Add(it.Name);
            _theirText.Text = parts.Count == 0 ? "(nothing yet)" : string.Join(", ", parts);
        }

        private void UpdateAcceptedIndicators(TradeOfferMessage offer)
        {
            var mine = _iAmFrom ? offer.From : offer.To;
            var their = _iAmFrom ? offer.To : offer.From;
            _youAccepted.Text = mine.Accepted ? "You: accepted" : "You: pending";
            _themAccepted.Text = their.Accepted ? "Them: accepted" : "Them: pending";
        }

        private bool ResolveTargetFromUi()
        {
            if (!_iAmFrom) return true;
            if (_targetCombo?.SelectedItem is not TradeTarget t) return false;
            _targetUserId = t.UserId;
            _targetCharId = t.CharacterId;
            _targetName = t.Name;
            return true;
        }

        private string CurrencyLabel(string currencyId)
        {
            var cur = _currencies.FirstOrDefault(c => c.Id == currencyId);
            if (cur == null) return currencyId;
            return string.IsNullOrWhiteSpace(cur.Abbreviation) ? cur.Name : cur.Abbreviation;
        }

        private void SetStatus(string text) => _statusText.Text = text;

        private void Sub()
        {
            var com = App.PM?.ComController;
            if (com == null) return;
            com.OnTradeUpdated += OnTradeUpdated;
            com.OnTradeCancelled += OnTradeCancelled;
            com.OnTradeResult += OnTradeResult;
        }

        private void Unsub()
        {
            var com = App.PM?.ComController;
            if (com == null) return;
            com.OnTradeUpdated -= OnTradeUpdated;
            com.OnTradeCancelled -= OnTradeCancelled;
            com.OnTradeResult -= OnTradeResult;
        }

        private static async Task<List<TradeItemLine>> LoadMyItemsAsync(string charId)
        {
            var lines = new List<TradeItemLine>();
            if (App.PM?.GameDataRepo == null) return lines;
            var instances = await App.PM.GameDataRepo.LoadInstancesForCharacterAsync(charId);
            if (instances.Count == 0) return lines;
            var names = await LoadItemNamesAsync(instances.Select(i => i.BaseItemId));
            foreach (var inst in instances)
            {
                var name = names.TryGetValue(inst.BaseItemId, out var n) ? n : inst.BaseItemId;
                if (!string.IsNullOrEmpty(inst.CustomName)) name = inst.CustomName!;
                lines.Add(new TradeItemLine(inst.Id, inst.BaseItemId, name, inst.Quantity <= 0 ? 1 : inst.Quantity));
            }
            return lines;
        }

        private static async Task<Dictionary<string, string>> LoadItemNamesAsync(IEnumerable<string> ids)
        {
            var map = new Dictionary<string, string>();
            var list = ids.Distinct().ToList();
            if (list.Count == 0 || App.PM?.DbManager == null) return map;
            await using var conn = await App.PM.DbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            var names = new List<string>();
            for (int i = 0; i < list.Count; i++)
            {
                var pn = "$i" + i;
                names.Add(pn);
                cmd.Parameters.AddWithValue(pn, list[i]);
            }
            cmd.CommandText = "SELECT Id, Name FROM Items WHERE Id IN (" + string.Join(", ", names) + ")";
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) map[r.GetString(0)] = r.GetString(1);
            return map;
        }

        private static async Task<List<TradeTarget>> LoadTargetsAsync(string campaignId, string myCharId)
        {
            var list = new List<TradeTarget>();
            if (App.PM?.DbManager == null) return list;
            await using var conn = await App.PM.DbManager.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT cm.UserId, cm.CharacterId, c.Name
                FROM CampaignMembers cm
                JOIN Characters c ON c.Id = cm.CharacterId
                WHERE cm.CampaignId = $cid AND cm.CharacterId IS NOT NULL AND cm.CharacterId <> $me
                ORDER BY c.Name
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$me", myCharId);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
                list.Add(new TradeTarget(r.GetString(0), r.GetString(1), r.GetString(2)));
            return list;
        }
    }
}