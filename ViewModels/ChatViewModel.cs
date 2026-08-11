using Avalonia.Threading;
using Dujahit.Models;
using Dujahit.Models.Communication;
using Dujahit.Models.UI;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class ChatViewModel : ViewModelBase
    {
        private string _currentChannelId = ""; // I'll make this reactive maybe?
        private readonly Dictionary<string, string> _macros = new(StringComparer.OrdinalIgnoreCase);
        private ObservableCollection<ChatChannel> _channels = new();
        private ObservableCollection<ChatMessage> _messages = new();

        public ObservableCollection<ChatChannel> Channels
        {
            get => _channels;
            private set => this.RaiseAndSetIfChanged(ref _channels, value);
        }

        public ObservableCollection<ChatMessage> Messages
        {
            get => _messages;
            private set => this.RaiseAndSetIfChanged(ref _messages, value);
        }

        private string _currentMessage = "";
        public string CurrentMessage
        {
            get => _currentMessage;
            set
            {
                Debug.WriteLine($"[VM] CurrentMessage set to '{value}'");
                this.RaiseAndSetIfChanged(ref _currentMessage, value);
            }
        }


        private string _currentChannelName = "general";
        public string CurrentChannelName
        {
            get => _currentChannelName;
            private set => this.RaiseAndSetIfChanged(ref _currentChannelName, value);
        }


        private string _newChannelName = "";
        public string NewChannelName
        {
            get => _newChannelName;
            set => this.RaiseAndSetIfChanged(ref _newChannelName, value);
        }

        public double WidgetHeight => IsMinimized ? 30d : 440d;
        public double WidgetWidth => IsMinimized ? 150d : 500d;

        private bool _showDiceBar;
        public bool ShowDiceBar
        {
            get => _showDiceBar;
            set => this.RaiseAndSetIfChanged(ref _showDiceBar, value);
        }
        private bool _isMinimized = true;
        public bool IsMinimized
        {
            get => _isMinimized;
            set
            {
                this.RaiseAndSetIfChanged(ref _isMinimized, value);
                this.RaisePropertyChanged(nameof(IsExpanded));
                this.RaisePropertyChanged(nameof(WidgetHeight));
                this.RaisePropertyChanged(nameof(WidgetWidth));
            }
        }

        public bool IsExpanded => !_isMinimized;

        private int _diceModifier;
        public int DiceModifier
        {
            get => _diceModifier;
            set => this.RaiseAndSetIfChanged(ref _diceModifier, value);
        }

        private string _diceMode = "";
        public string DiceMode
        {
            get => _diceMode;
            set
            {
                this.RaiseAndSetIfChanged(ref _diceMode, value);
                this.RaisePropertyChanged(nameof(DiceModeLabel));
            }
        }
        public string DiceModeLabel => _diceMode == "adv" ? "Advantage" : _diceMode == "dis" ? "Disadvantage" : "Normal";

        public ReactiveCommand<Unit, Unit> SendMessageCommand { get; }
        public ReactiveCommand<Unit, Unit> CreateChannelCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleMinimizeCommand { get; }
        public ReactiveCommand<ChatChannel, Unit> SwitchChannelCommand { get; }
        public ReactiveCommand<string, Unit> RollDieCommand { get; }
        public ReactiveCommand<string, Unit> SetDiceModeCommand { get; }

        public ChatViewModel()
        {
            var controller = App.PM.ComController;

            var canSend = this.WhenAnyValue(
                x => x.CurrentMessage,
                x => x.CurrentChannelName,
                (msg, ch) => !string.IsNullOrWhiteSpace(msg) && !string.IsNullOrWhiteSpace(ch));

            SendMessageCommand = ReactiveCommand.CreateFromTask(SendMessageAsync, canSend);

            SetDiceModeCommand = ReactiveCommand.Create<string>(m => DiceMode = (m == "adv" || m == "dis") ? m : "");
            RollDieCommand = ReactiveCommand.CreateFromTask<string>(async sides =>
            {
                if (!int.TryParse(sides, out var s) || s < 1) return;
                var mod = DiceModifier;
                var modText = mod > 0 ? "+" + mod : mod < 0 ? mod.ToString() : "";
                var mode = s == 20 ? DiceMode : "";
                await QuickRollAsync($"1d{s}{modText}", mode);
            });

            var canCreate = this.WhenAnyValue(
                x => x.NewChannelName,
                name => !string.IsNullOrWhiteSpace(name));

            CreateChannelCommand = ReactiveCommand.CreateFromTask(CreateChannelAsync, canCreate);
            ToggleMinimizeCommand = ReactiveCommand.Create(() => { IsMinimized = !IsMinimized; });
            SwitchChannelCommand = ReactiveCommand.CreateFromTask<ChatChannel>(SwitchChannelAsync);

            controller.OnChannelCreated += channel =>
            {
                if (!Channels.Any(c => c.Id == channel.Id))
                    Channels.Add(channel);
            };

            controller.OnWhisperReceived += (fromUser, text) =>
                Dispatcher.UIThread.Post(() => ShowLocalSystemMessage($"[whisper from {fromUser}] {text}"));

            controller.OnReconnected += () =>
            {
                if (!string.IsNullOrEmpty(_currentChannelId)) _ = App.PM.ComController.JoinChatChannelAsync(_currentChannelId);
            };

            _ = LoadChannelsFromDbAsync();
            _ = LoadMacrosAsync();
        }

        private async Task LoadMacrosAsync()
        {
            var campaignId = App.PM.GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return;
            var loaded = await App.PM.GameDataRepo.LoadMacrosAsync(campaignId, App.PM.GetUID());
            Dispatcher.UIThread.Post(() =>
            {
                _macros.Clear();
                foreach (var kv in loaded) _macros[kv.Key] = kv.Value;
            });
        }

        public async Task SwitchChannelAsync(ChatChannel channel)
        {
            if (channel == null) return;
            _currentChannelId = channel.Id;
            CurrentChannelName = channel.Name;
            Messages = App.PM.ComController.GetChannelMessages(channel.Id);
            Debug.WriteLine($"[VM] Switched to {channel.Id}, collection hash={Messages.GetHashCode()}, count={Messages.Count}");

            if (Messages.Count == 0)
                await LoadMessageHistoryFromDbAsync(channel.Id);

            await App.PM.ComController.JoinChatChannelAsync(channel.Id);
        }

        private async Task SendMessageAsync()
        {
            Debug.WriteLine($"[VM] SendMessageAsync called. channelId='{_currentChannelId}', msg='{CurrentMessage}'");

            if (string.IsNullOrWhiteSpace(_currentChannelId)) return;

            var message = CurrentMessage.Trim();
            CurrentMessage = "";

            if (message.StartsWith("/"))
            {
                await HandleSlashCommandAsync(message);
                return;
            }

            Debug.WriteLine($"[VM] About to call SendChatMessageAsync");
            await App.PM.ComController.SendChatMessageAsync(_currentChannelId, message);
            Debug.WriteLine($"[VM] SendChatMessageAsync returned");
        }

        // This one is shit.. It does work tho
        private async Task HandleSlashCommandAsync(string raw)
        {
            var parts = raw.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var args = parts.Length > 1 ? parts[1] : "";

            switch (cmd)
            {
                case "/roll":
                case "/r":
                    await HandleRollAsync(args, secret: false);
                    break;

                case "/gmroll":
                case "/gr":
                    await HandleRollAsync(args, secret: true);
                    break;

                case "/invite":
                    await HandleInviteAsync(args);
                    break;

                case "/whisper":
                case "/w":
                    await HandleWhisperAsync(args);
                    break;

                case "/macro":
                case "/m":
                    await HandleMacroAsync(args);
                    break;

                case "/table":
                case "/t":
                    await HandleTableAsync(args);
                    break;

                case "/help":
                    ShowLocalSystemMessage(
                        "Commands: /roll NdM[+K] (e.g. 2d6+3), /roll adv|dis 1d20+K, /roll crit <dmg>, /roll <macro>, /gmroll ... (whispered to the DM), /whisper <username> <message> (or /w, private to that player), /macro add <name> <expr>, /macro list, /macro del <name>, /table <name> (or /t, roll on a random table), /invite <username>, /help");
                    break;

                default:
                    ShowLocalSystemMessage($"Unknown command: {cmd}");
                    break;
            }
        }

        private async Task HandleRollAsync(string args, bool secret)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                ShowLocalSystemMessage("Usage: /roll NdM[+K]  |  /roll adv 1d20+5  |  /roll crit 2d6+3");
                return;
            }

            var tokens = args.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var head = tokens[0].ToLowerInvariant();
            var crit = false;
            var expr = args.Trim();

            if (head is "adv" or "dis" or "crit")
            {
                var rest = tokens.Length > 1 ? tokens[1].Trim() : "";
                if (head == "crit") { crit = true; expr = rest; }
                else expr = AdvExpr(rest, head == "adv" ? "2d20kh1" : "2d20kl1");
            }

            if (string.IsNullOrWhiteSpace(expr))
            {
                ShowLocalSystemMessage("Nothing to roll.");
                return;
            }

            if (!DiceManager.TryRoll(expr, crit, out var result) || result == null)
            {
                var key = expr.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? expr;
                if (_macros.TryGetValue(key, out var macroExpr) && DiceManager.TryRoll(macroExpr, crit, out var macroResult) && macroResult != null)
                {
                    var mCritText = crit ? " (crit, dice doubled)" : "";
                    var mText = $"{key} ({macroExpr}){mCritText} = {macroResult.Total}   ({macroResult.Breakdown})";
                    await PostRollAsync(mText, secret);
                    return;
                }

                ShowLocalSystemMessage($"Invalid dice notation: {expr}");
                return;
            }

            var critText = crit ? " (crit, dice doubled)" : "";
            var text = $"rolled {expr}{critText} = {result.Total}   ({result.Breakdown})";
            await PostRollAsync(text, secret);
        }

        private static string AdvExpr(string rest, string keep)
        {
            rest = (rest ?? "").Replace(" ", "");
            if (string.IsNullOrEmpty(rest)) return keep;
            foreach (var p in new[] { "1d20", "d20" })
                if (rest.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { rest = rest[p.Length..]; break; }
            if (rest.Length == 0) return keep;
            if (rest[0] != '+' && rest[0] != '-') rest = "+" + rest;
            return keep + rest;
        }

        public Task QuickRollAsync(string expression, string mode)
        {
            var args = string.IsNullOrEmpty(mode) ? expression : $"{mode} {expression}";
            return HandleRollAsync(args, false);
        }

        public async Task PostRollAsync(string text, bool secret)
        {
            if (secret)
            {
                ShowLocalSystemMessage(text);
                await App.PM.ComController.WhisperToDmAsync(App.PM.GetUsername(), text);
                return;
            }
            if (string.IsNullOrWhiteSpace(_currentChannelId))
            {
                ShowLocalSystemMessage(text);
                return;
            }
            await App.PM.ComController.SendChatMessageAsync(_currentChannelId, text);
        }

        private async Task HandleTableAsync(string args)
        {
            var name = (args ?? "").Trim();
            if (name.Length == 0)
            {
                ShowLocalSystemMessage("Usage: /table <name>");
                return;
            }
            var tables = await App.PM.LoadRandomTablesAsync();
            var table = tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? tables.FirstOrDefault(t => t.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (table == null)
            {
                ShowLocalSystemMessage($"No table called '{name}'.");
                return;
            }
            var rolled = ProgramManager.RollOnTable(table);
            if (rolled == null)
            {
                ShowLocalSystemMessage($"{table.Name} has nothing to roll on.");
                return;
            }
            await PostRollAsync($"rolled on {table.Name}: [{rolled.Value.Roll}] {rolled.Value.Text}", false);
        }

        private async Task HandleMacroAsync(string args)
        {
            var parts = (args ?? "").Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var sub = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
            var campaignId = App.PM.GetCampaignId();
            var userId = App.PM.GetUID();

            switch (sub)
            {
                case "add" when parts.Length == 3:
                    {
                        var name = parts[1];
                        var expr = parts[2].Trim();
                        if (!DiceManager.TryRoll(expr, out _))
                        {
                            ShowLocalSystemMessage($"'{expr}' isn't a valid dice expression.");
                            return;
                        }
                        await App.PM.GameDataRepo.SaveMacroAsync(campaignId, userId, name, expr);
                        _macros[name] = expr;
                        ShowLocalSystemMessage($"Saved macro '{name}' = {expr}. Roll it with /roll {name}");
                        break;
                    }
                case "del" when parts.Length >= 2:
                case "delete" when parts.Length >= 2:
                    {
                        var name = parts[1];
                        await App.PM.GameDataRepo.DeleteMacroAsync(campaignId, userId, name);
                        _macros.Remove(name);
                        ShowLocalSystemMessage($"Deleted macro '{name}'.");
                        break;
                    }
                case "list":
                    {
                        if (_macros.Count == 0) { ShowLocalSystemMessage("No macros yet. Add one with /macro add <name> <expr>"); break; }
                        var lines = string.Join("\n", _macros.Select(m => $"  {m.Key} = {m.Value}"));
                        ShowLocalSystemMessage("Your macros:\n" + lines);
                        break;
                    }
                default:
                    ShowLocalSystemMessage("Usage: /macro add <name> <expr>  |  /macro list  |  /macro del <name>");
                    break;
            }
        }

        private async Task HandleInviteAsync(string args)
        {
            if (string.IsNullOrWhiteSpace(args))
            {
                ShowLocalSystemMessage("Usage: /invite <username>");
                return;
            }

            var username = args.Trim();
            var targetUserId = App.PM.LookupUserIdByName(username);

            if (string.IsNullOrEmpty(targetUserId))
            {
                ShowLocalSystemMessage($"No user named '{username}' in this campaign.");
                return;
            }

            await App.PM.ComController.InviteToChannelAsync(_currentChannelId, targetUserId);

            await App.PM.ComController.SendChatMessageAsync(
                _currentChannelId,
                $"{App.PM.GetUsername()} invited {username} to #{CurrentChannelName}");
        }

        private async Task HandleWhisperAsync(string args)
        {
            var parts = (args ?? "").Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
            {
                ShowLocalSystemMessage("Usage: /whisper <username> <message>");
                return;
            }

            var username = parts[0];
            var text = parts[1].Trim();
            var targetUserId = App.PM.LookupUserIdByName(username);

            if (string.IsNullOrEmpty(targetUserId))
            {
                ShowLocalSystemMessage($"No user named '{username}' in this campaign.");
                return;
            }

            if (string.Equals(targetUserId, App.PM.GetUID(), StringComparison.OrdinalIgnoreCase))
            {
                ShowLocalSystemMessage("Whispering yourself, bold move but no.");
                return;
            }

            await App.PM.ComController.WhisperToUserAsync(targetUserId, App.PM.GetUsername(), text);
            ShowLocalSystemMessage($"[whisper to {username}] {text}");
        }

        private void ShowLocalSystemMessage(string text)
        {
            Messages.Add(new ChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                ChannelId = _currentChannelId,
                Sender = "system",
                Message = text,
                Timestamp = DateTime.Now
            });
        }

        private async Task CreateChannelAsync()
        {
            if (string.IsNullOrWhiteSpace(NewChannelName)) return;

            var name = NewChannelName.Trim();
            NewChannelName = "";

            await App.PM.ComController.CreateChannelAsync(name);
        }

        private async Task LoadChannelsFromDbAsync()
        {

            var campaignId = App.PM.GetCampaignId();
            Debug.WriteLine($"[LoadChannelsFromDb] called. campaignId = '{campaignId}'");

            if (string.IsNullOrEmpty(campaignId))
            {
                Debug.WriteLine("[LoadChannelsFromDb] no campaignId, returning early.");
                return;
            }

            var repo = App.PM.GameDataRepo;
            var channels = await repo.LoadChannelsAsync(campaignId);
            Debug.WriteLine($"[LoadChannelsFromDb] loaded {channels.Count} channels for '{campaignId}'");
            foreach (var ch in channels)
                Debug.WriteLine($"  - channel {ch.Id} '{ch.Name}' (campaign={ch.CampaignId})");


            Dispatcher.UIThread.Post(() =>
            {
                Channels.Clear();
                foreach (var ch in channels)
                    Channels.Add(ch);

                var general = Channels.FirstOrDefault(c =>
                    c.Name.Equals("general", StringComparison.OrdinalIgnoreCase));
                if (general != null)
                    _ = SwitchChannelAsync(general);
                else if (string.IsNullOrWhiteSpace(_currentChannelId) && Channels.Count > 0)
                    _ = SwitchChannelAsync(Channels[0]);
            });
        }

        private async Task LoadMessageHistoryFromDbAsync(string channelId)
        {
            var repo = App.PM.GameDataRepo;
            var history = await repo.LoadMessagesAsync(channelId);

            if (history.Count == 0)
            {
                var remote = await App.PM.ComController.FetchChannelHistoryAsync(channelId);
                if (remote.Count > 0) history = remote;
            }

            Dispatcher.UIThread.Post(() =>
            {
                if (channelId != _currentChannelId) return;

                var collection = App.PM.ComController.GetChannelMessages(channelId);
                collection.Clear();
                foreach (var msg in history)
                    collection.Add(msg);
            });
        }
    }
}