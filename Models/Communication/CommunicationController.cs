using Avalonia.Threading;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Dujahit.Models.Communication
{
    public class CommunicationController
    {
        private HubConnection? _gameHub;
        private HubConnection? _chatHub;
        private HubConnection? _dataHub;
        private HubConnection? _mapHub;
        private HubConnection? _noteHub;
        private IWebHost? _webHost;

        private string _currentCampaignId = "";
        private string _currentChatChannelId = "";
        private string _joinSecret = "";
        private string _sessionToken = "";
        private string _mapUserId = "";
        private long _lastChangeId;

        private readonly Dictionary<string, ObservableCollection<ChatMessage>> _channelMessages = new();

        public bool IsConnected { get; private set; }
        public string Username { get; set; } = "User";
        public string UserId { get; set; } = "";
        public string MyColor { get; set; } = "#FFD700";
        private readonly Dictionary<string, string> _memberColors = new(StringComparer.OrdinalIgnoreCase);
        public string GetColorForUser(string userId) => _memberColors.TryGetValue(userId, out var c) ? c : "#FFD700";
        public string ServerIp { get; set; } = "localhost";
        public string ServerPort { get; set; } = "5000";

        public event Action<CampaignBootstrapPayload>? OnBootstrapReceived;

        public event Action<ChangeNotification>? OnChangeReceived;

        public event Action<string, ChatMessage>? OnChatMessageReceived;
        public event Action<string, string>? OnWhisperReceived;

        public event Action<string, string>? OnPlayerJoined;
        public event Action<string>? OnPlayerLeft;
        public event Action<string, string>? OnPlayerColorChanged;

        public event Action? OnConnectionLost;
        public event Action? OnReconnecting;
        public event Action? OnReconnected;

        public event Action<HandoutRevealedMessage>? OnHandoutRevealed;
        public event Action<MindmapSyncPayload>? OnMindmapSynced;
        public event Action<string>? OnMindmapRevoked;
        public event Action<MindmapNodeOp>? OnMindNodeUpserted;
        public event Action<MindmapNodeMoveOp>? OnMindNodeMoved;
        public event Action<MindmapNodeDeleteOp>? OnMindNodeDeleted;
        public event Action<MindmapLinkOp>? OnMindLinkUpserted;
        public event Action<MindmapLinkDeleteOp>? OnMindLinkDeleted;
        public event Action<string>? OnHandoutHidden;


        public event Action<StrokeMessage>? OnStrokeReceived;
        public event Action<TokenAddedMessage>? OnTokenAdded;
        public event Action<TokenMovedMessage>? OnTokenMoved;
        public event Action<TokenRemovedMessage>? OnTokenRemoved;
        public event Action<NotePageChangePayload>? OnNotePageChanged;
        public event Action<NoteUpdatePayload>? OnNoteUpdate;
        public event Action? OnNoteReconnected;
        public event Action<NotePage>? OnNotePageInvited;
        public event Action<string>? OnNotePageRevoked;

        public event Action<PingMessage>? OnPingReceived;
        public event Action<string>? OnStrokeUndone;
        public event Action<PermissionsUpdateMessage>? OnPermissionsUpdated;
        public event Action<TokenResizedMessage>? OnTokenResized;
        public event Action<TokenRotatedMessage>? OnTokenRotated;
        public event Action<FogPaintMessage>? OnFogPainted;
        public event Action<FogStateMessage>? OnFogUpdated;
        public event Action<WallStateMessage>? OnWallsUpdated;
        public event Action<TerrainStateMessage>? OnTerrainUpdated;
        public event Action<MapObjectStateMessage>? OnMapObjectsUpdated;
        public event Action<DoorToggleMessage>? OnDoorToggled;
        public event Action<AoeTemplateMessage>? OnAoeTemplatePlaced;
        public event Action<string>? OnAoeTemplatesCleared;
        public event Action<MapActivatedMessage>? OnMapActivated; 
        public event Action<CombatStateMessage>? OnCombatStateUpdated;
        public event Action<CombatActionMessage>? OnCombatActionReceived;
        public event Action<CombatEconomyMessage>? OnCombatEconomyReceived;
        public event Action<DiceRollMessage>? OnDiceRollReceived;
        public event Action<SoundChunkMessage>? OnSoundChunkShared;
        public event Action<PlaySoundMessage>? OnSoundPlayed;
        public event Action? OnMusicStopped;
        public event Action<TradeOfferMessage>? OnTradeOffered;
        public event Action<TradeOfferMessage>? OnTradeUpdated;
        public event Action<TradeOfferMessage>? OnTradeCancelled;
        public event Action<TradeResultMessage>? OnTradeResult;
        private readonly List<CampaignMember> _members = new();
        public IReadOnlyList<CampaignMember> Members => _members.ToArray();

        private readonly HashSet<string> _onlineUserIds = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyCollection<string> OnlineUserIds => _onlineUserIds.ToArray();

        public event Action? OnMapDeactivated;

        public ObservableCollection<ChatMessage> GetChannelMessages(string channelId)
        {
            if (!_channelMessages.TryGetValue(channelId, out var collection))
            {
                collection = new ObservableCollection<ChatMessage>();
                _channelMessages[channelId] = collection;
            }
            return collection;
        }

        private void UpsertMember(string userId, string username)
        {
            var existing = _members.FirstOrDefault(m => string.Equals(m.UserId, userId, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(username)) existing.Username = username;
                return;
            }
            _members.Add(new CampaignMember
            {
                UserId = userId,
                Username = string.IsNullOrWhiteSpace(username) ? userId : username,
                Role = "player"
            });
        }

        private string _scheme = "http";
        private string _pinnedFingerprint = "";

        private HubConnection BuildHub(string ip, string port, string path)
        {
            var fp = _pinnedFingerprint;
            var scheme = _scheme;
            return new HubConnectionBuilder()
                .WithUrl($"{scheme}://{ip}:{port}/{path}", o =>
                {
                    if (scheme != "https") return;
                    o.HttpMessageHandlerFactory = handler =>
                    {
                        if (handler is HttpClientHandler h)
                            h.ServerCertificateCustomValidationCallback = (msg, cert, chain, errors) => HostCertificate.Matches(cert, fp);
                        else if (handler is SocketsHttpHandler s)
                            s.SslOptions.RemoteCertificateValidationCallback = (snd, cert, chain, errors) => HostCertificate.Matches(cert, fp);
                        return handler;
                    };
                    o.WebSocketConfiguration = ws => ws.RemoteCertificateValidationCallback = (snd, cert, chain, errors) => HostCertificate.Matches(cert, fp);
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30),
                    TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)
                })
                .Build();
        }

        public event Action<ChatChannel>? OnChannelCreated;

        public async Task JoinChatChannelAsync(string channelId)
        {
            if (!string.IsNullOrEmpty(channelId)) _currentChatChannelId = channelId;
            if (_chatHub == null || !IsConnected) return;
            await _chatHub.InvokeAsync("JoinChannel", channelId);
        }

        public async Task<List<ChatMessage>> FetchChannelHistoryAsync(string channelId)
        {
            if (_chatHub == null || !IsConnected) return new List<ChatMessage>();
            try { return await _chatHub.InvokeAsync<List<ChatMessage>>("GetRecentMessages", channelId); }
            catch { return new List<ChatMessage>(); }
        }

        public async Task CreateChannelAsync(string channelName)
        {
            if (_chatHub == null || !IsConnected) return;
            await _chatHub.InvokeAsync("CreateChannel", _currentCampaignId, channelName);
        }

        public async Task<CampaignBootstrapPayload> JoinCampaignAsync(string userId, string username, string serverIp, string serverPort, string joinSecret = "", string fingerprint = "")
        {
            UserId = userId;
            Username = username;
            ServerIp = serverIp;
            ServerPort = serverPort;
            _joinSecret = joinSecret ?? "";
            _pinnedFingerprint = (fingerprint ?? "").Trim();
            _scheme = _pinnedFingerprint.Length > 0 ? "https" : "http";

            try
            {
                return await ConnectAndBootstrapAsync(userId, username, serverIp, serverPort);
            }
            catch when (_pinnedFingerprint.Length == 0 && _scheme == "http")
            {
                _scheme = "https";
                return await ConnectAndBootstrapAsync(userId, username, serverIp, serverPort);
            }
        }

        private async Task<CampaignBootstrapPayload> ConnectAndBootstrapAsync(string userId, string username, string serverIp, string serverPort)
        {
            _gameHub = BuildHub(serverIp, serverPort, "gamehub");
            _chatHub = BuildHub(serverIp, serverPort, "chat");
            _dataHub = BuildHub(serverIp, serverPort, "datexchange");
            _mapHub = BuildHub(serverIp, serverPort, "map");
            _noteHub = BuildHub(serverIp, serverPort, "notepage");

            WireNoteHub();
            WireDataHub();
            WireChatHub();
            WireMapHub();
            WireGameHub();

            try
            {
            await Task.WhenAll(
                _gameHub.StartAsync(),
                _chatHub.StartAsync(),
                _dataHub.StartAsync(),
                _mapHub.StartAsync(),
                _noteHub.StartAsync()
            );

            IsConnected = true;

            var payload = await _dataHub.InvokeAsync<CampaignBootstrapPayload>(
                "JoinCampaign", userId, username, _joinSecret, _sessionToken);
            _sessionToken = payload.SessionToken ?? "";

            await _mapHub!.InvokeAsync("JoinCampaignMap", payload.CampaignId, userId, _sessionToken);

            _currentCampaignId = payload.CampaignId;
            _mapUserId = userId;
            _lastChangeId = payload.LastChangeId;

            _members.Clear();
            if (payload.Members != null) _members.AddRange(payload.Members);

            if (!string.IsNullOrEmpty(payload.AssignedColor)) MyColor = payload.AssignedColor;
            _memberColors.Clear();
            foreach (var m in _members)
                if (!string.IsNullOrEmpty(m.Color)) _memberColors[m.UserId] = m.Color!;
            _memberColors[UserId] = MyColor;

            _onlineUserIds.Clear();
            foreach (var id in payload.OnlineUserIds) _onlineUserIds.Add(id);
            _onlineUserIds.Add(userId);

            await _noteHub!.InvokeAsync("RegisterUser", userId, _sessionToken);
            await _gameHub!.InvokeAsync("RegisterUser", userId, _sessionToken);
            await _chatHub!.InvokeAsync("RegisterUser", userId, _sessionToken);

            OnBootstrapReceived?.Invoke(payload);

            return payload;
            }
            catch
            {
                await DisconnectAsync();
                throw;
            }
        }

        public async Task SendStrokeAsync(StrokeMessage stroke)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("SendStroke", _currentCampaignId, stroke);
        }

        public async Task AddTokenAsync(TokenAddedMessage token)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("AddToken", _currentCampaignId, token);
        }

        public async Task MoveTokenAsync(TokenMovedMessage move)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("MoveToken", _currentCampaignId, move);
        }

        public async Task RemoveTokenAsync(TokenRemovedMessage rm)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("RemoveToken", _currentCampaignId, rm);
        }

        public async Task UndoStrokeAsync(string strokeId)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("UndoStroke", _currentCampaignId, strokeId);
        }

        public async Task ActivateMapAsync(string mapId, string gridKind, double scale, int width = 0, int height = 0)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("ActivateMap", _currentCampaignId, new MapActivatedMessage(mapId, gridKind, scale, width, height));
        }

        public async Task DeactivateMapAsync()
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("DeactivateMap", _currentCampaignId);
        }

        public async Task SendPingAsync(PingMessage ping)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("SendPing", _currentCampaignId, ping);
        }

        public async Task<bool> RequestColorAsync(string hex)
        {
            if (_dataHub == null || !IsConnected) return false;
            return await _dataHub.InvokeAsync<bool>("RequestColor", _currentCampaignId, UserId, hex);
        }

        public async Task<byte[]?> FetchMapImageAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return null;
            return await _mapHub.InvokeAsync<byte[]?>("FetchMapImage", _currentCampaignId, mapId);
        }

        public async Task<List<PlayerMapSummary>> FetchPlayerMapsAsync()
        {
            if (_mapHub == null || !IsConnected) return new List<PlayerMapSummary>();
            return await _mapHub.InvokeAsync<List<PlayerMapSummary>>("FetchPlayerMaps", _currentCampaignId);
        }

        public HandoutRevealedMessage? ActiveHandout { get; private set; }

        public async Task RevealHandoutAsync(HandoutRevealedMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            ActiveHandout = msg;
            await _mapHub.InvokeAsync("RevealHandout", _currentCampaignId, msg);
        }

        public async Task HideHandoutAsync(string handoutId)
        {
            if (_mapHub == null || !IsConnected) return;
            if (ActiveHandout?.HandoutId == handoutId) ActiveHandout = null;
            await _mapHub.InvokeAsync("HideHandout", _currentCampaignId, handoutId);
        }

        public async Task<byte[]?> FetchHandoutAsync(string handoutId)
        {
            if (_mapHub == null || !IsConnected) return null;
            return await _mapHub.InvokeAsync<byte[]?>("FetchHandout", _currentCampaignId, handoutId);
        }

        public async Task SendChatMessageAsync(string channelId, string message)
        {
            Debug.WriteLine($"[ComCtrl] SendChatMessageAsync: connected={IsConnected}, hub={_chatHub != null}, channelId={channelId}");

            if (!IsConnected || _chatHub == null) return;

            var msg = new ChatMessage
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = _currentCampaignId,
                ChannelId = channelId,
                UserId = UserId,
                Sender = Username,
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            Debug.WriteLine($"[ComCtrl] Invoking SendChatMessage on hub");
            await _chatHub.InvokeAsync("SendChatMessage", msg);
            Debug.WriteLine($"[ComCtrl] Invoke returned");
        }

        public async Task WhisperToDmAsync(string fromUsername, string text)
        {
            if (!IsConnected || _chatHub == null) return;
            await _chatHub.InvokeAsync("WhisperToDm", _currentCampaignId, fromUsername, text);
        }

        public async Task WhisperToUserAsync(string targetUserId, string fromUsername, string text)
        {
            if (!IsConnected || _chatHub == null) return;
            await _chatHub.InvokeAsync("WhisperToUser", targetUserId, fromUsername, text);
        }

        private void WireDataHub()
        {
            _dataHub!.On<ChangeNotification>("ReceiveChange", change =>
            {
                if (change.ChangeId > _lastChangeId) _lastChangeId = change.ChangeId;
                Dispatcher.UIThread.Post(() => OnChangeReceived?.Invoke(change));
            });

            _dataHub!.On<string, string>("PlayerJoined", (uid, uname) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _onlineUserIds.Add(uid);
                    UpsertMember(uid, uname);
                    OnPlayerJoined?.Invoke(uid, uname);
                });
            });

            Action<string> onPlayerLeft = uid =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _onlineUserIds.Remove(uid);
                    OnPlayerLeft?.Invoke(uid);
                });
            };
            _dataHub!.On<string>("PlayerLeft", onPlayerLeft);
            _gameHub!.On<string>("PlayerLeft", onPlayerLeft);
            _chatHub!.On<string>("PlayerLeft", onPlayerLeft);
            _mapHub!.On<string>("PlayerLeft", onPlayerLeft);
            _noteHub!.On<string>("PlayerLeft", onPlayerLeft);

            _dataHub!.On<string, string>("PlayerColorChanged", (uid, hex) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _memberColors[uid] = hex;
                    if (string.Equals(uid, UserId, StringComparison.OrdinalIgnoreCase)) MyColor = hex;
                    OnPlayerColorChanged?.Invoke(uid, hex);
                });
            });

            _dataHub!.On<TradeOfferMessage>("TradeOffered", offer =>
                Dispatcher.UIThread.Post(() => OnTradeOffered?.Invoke(offer)));

            _dataHub!.On<TradeOfferMessage>("TradeUpdated", offer =>
                Dispatcher.UIThread.Post(() => OnTradeUpdated?.Invoke(offer)));

            _dataHub!.On<TradeOfferMessage>("TradeCancelled", offer =>
                Dispatcher.UIThread.Post(() => OnTradeCancelled?.Invoke(offer)));

            _dataHub!.On<TradeResultMessage>("TradeResult", result =>
                Dispatcher.UIThread.Post(() => OnTradeResult?.Invoke(result)));

            _dataHub!.On<string>("SessionTokenIssued", t => _sessionToken = t ?? "");

            _dataHub!.Closed += _ =>
            {
                IsConnected = false;
                Dispatcher.UIThread.Post(() => OnConnectionLost?.Invoke());
                return Task.CompletedTask;
            };

            _dataHub!.Reconnecting += _ =>
            {
                Dispatcher.UIThread.Post(() => OnReconnecting?.Invoke());
                return Task.CompletedTask;
            };

            _dataHub!.Reconnected += async _ =>
            {
                IsConnected = true;
                try { await _dataHub.InvokeAsync("RejoinCampaign", _currentCampaignId, UserId, Username, _joinSecret, _lastChangeId, _sessionToken); }
                catch (Exception ex) { ErrorLog.Log($"[ComCtrl] rejoin after reconnect failed", ex); Dispatcher.UIThread.Post(() => OnConnectionLost?.Invoke()); return; }
                Dispatcher.UIThread.Post(() => OnReconnected?.Invoke());
            };
        }

        private void WireChatHub()
        {
            _chatHub!.On<ChatMessage>("ReceiveChatMessage", msg =>
            {
                Debug.WriteLine($"[CHAT] Received {msg.Id} from {msg.Sender} for channel {msg.ChannelId}");
                Dispatcher.UIThread.Post(() =>
                {
                    var collection = GetChannelMessages(msg.ChannelId);
                    Debug.WriteLine($"[CHAT] Adding to {msg.ChannelId}, collection hash={collection.GetHashCode()}, count before={collection.Count}");
                    collection.Add(msg);
                    Debug.WriteLine($"[CHAT] Collection count after add: {collection.Count}");
                    OnChatMessageReceived?.Invoke(msg.ChannelId, msg);
                });
            });

            _chatHub!.On<ChatChannel>("ChannelCreated", channel =>
            {
                Dispatcher.UIThread.Post(() => OnChannelCreated?.Invoke(channel));
            });

            _chatHub!.On<string>("InvitedToChannel", channelId =>
            {
                Dispatcher.UIThread.Post(() => OnInvitedToChannel?.Invoke(channelId));
            });

            _chatHub!.On<string, string>("ReceiveWhisper", (fromUser, text) =>
            {
                Dispatcher.UIThread.Post(() => OnWhisperReceived?.Invoke(fromUser, text));
            });

            _chatHub!.Reconnected += async _ =>
            {
                if (!string.IsNullOrEmpty(UserId)) await _chatHub.InvokeAsync("RegisterUser", UserId, _sessionToken);
                if (!string.IsNullOrEmpty(_currentChatChannelId)) await _chatHub.InvokeAsync("JoinChannel", _currentChatChannelId);
            };
        }

        private void WireMapHub()
        {
            _mapHub!.On<StrokeMessage>("ReceiveStroke", msg =>
                Dispatcher.UIThread.Post(() => OnStrokeReceived?.Invoke(msg)));

            _mapHub!.On<TokenAddedMessage>("TokenAdded", msg =>
                Dispatcher.UIThread.Post(() => OnTokenAdded?.Invoke(msg)));

            _mapHub!.On<TokenMovedMessage>("TokenMoved", msg =>
                Dispatcher.UIThread.Post(() => OnTokenMoved?.Invoke(msg)));

            _mapHub!.On<TokenRemovedMessage>("TokenRemoved", msg =>
                Dispatcher.UIThread.Post(() => OnTokenRemoved?.Invoke(msg)));

            _mapHub!.On<TokenResizedMessage>("TokenResized", msg =>
                Dispatcher.UIThread.Post(() => OnTokenResized?.Invoke(msg)));
            _mapHub!.On<TokenRotatedMessage>("TokenRotated", msg =>
                Dispatcher.UIThread.Post(() => OnTokenRotated?.Invoke(msg)));

            _mapHub!.On<PingMessage>("PingReceived", msg =>
                Dispatcher.UIThread.Post(() => OnPingReceived?.Invoke(msg)));

            _mapHub!.On<string>("StrokeUndone", id =>
                Dispatcher.UIThread.Post(() => OnStrokeUndone?.Invoke(id)));

            _mapHub!.On<MapActivatedMessage>("MapActivated", msg =>
                Dispatcher.UIThread.Post(() => OnMapActivated?.Invoke(msg)));

            _mapHub!.On<FogPaintMessage>("FogPainted", msg =>
                Dispatcher.UIThread.Post(() => OnFogPainted?.Invoke(msg)));

            _mapHub!.On<FogStateMessage>("FogUpdated", msg =>
                Dispatcher.UIThread.Post(() => OnFogUpdated?.Invoke(msg)));

            _mapHub!.On<WallStateMessage>("WallsUpdated", msg =>
                Dispatcher.UIThread.Post(() => OnWallsUpdated?.Invoke(msg)));

            _mapHub!.On<TerrainStateMessage>("TerrainUpdated", msg =>
                Dispatcher.UIThread.Post(() => OnTerrainUpdated?.Invoke(msg)));

            _mapHub!.On<MapObjectStateMessage>("MapObjectsUpdated", msg =>
                Dispatcher.UIThread.Post(() => OnMapObjectsUpdated?.Invoke(msg)));

            _mapHub!.On<DoorToggleMessage>("DoorToggled", msg =>
                Dispatcher.UIThread.Post(() => OnDoorToggled?.Invoke(msg)));

            _mapHub!.On<AoeTemplateMessage>("AoeTemplatePlaced", msg =>
                Dispatcher.UIThread.Post(() => OnAoeTemplatePlaced?.Invoke(msg)));

            _mapHub!.On<string>("AoeTemplatesCleared", id =>
                Dispatcher.UIThread.Post(() => OnAoeTemplatesCleared?.Invoke(id)));

            _mapHub!.On("MapDeactivated", () =>
                Dispatcher.UIThread.Post(() => OnMapDeactivated?.Invoke()));

            _mapHub!.On<PermissionsUpdateMessage>("PermissionsUpdated", msg =>
                Dispatcher.UIThread.Post(() => OnPermissionsUpdated?.Invoke(msg)));

            _mapHub!.On<CombatStateMessage>("CombatStateUpdated", msg =>
                Dispatcher.UIThread.Post(() => OnCombatStateUpdated?.Invoke(msg)));

            _mapHub!.On<CombatActionMessage>("CombatActionReceived", msg =>
                Dispatcher.UIThread.Post(() => OnCombatActionReceived?.Invoke(msg)));

            _mapHub!.On<CombatEconomyMessage>("CombatEconomyReceived", msg =>
                Dispatcher.UIThread.Post(() => OnCombatEconomyReceived?.Invoke(msg)));

            _mapHub!.On<HandoutRevealedMessage>("HandoutRevealed", msg =>
                Dispatcher.UIThread.Post(() => OnHandoutRevealed?.Invoke(msg)));

            _mapHub!.On<string>("HandoutHidden", id =>
                Dispatcher.UIThread.Post(() => OnHandoutHidden?.Invoke(id)));

            _mapHub!.Reconnected += async _ =>
            {
                await _mapHub.InvokeAsync("JoinCampaignMap", _currentCampaignId, _mapUserId, _sessionToken);
            };
        }

        private void WireGameHub()
        {
            _gameHub!.On<DiceRollMessage>("ReceiveDiceRoll", msg =>
                Dispatcher.UIThread.Post(() => OnDiceRollReceived?.Invoke(msg)));

            _gameHub!.On<SoundChunkMessage>("SoundChunkShared", chunk =>
                Dispatcher.UIThread.Post(() => OnSoundChunkShared?.Invoke(chunk)));
            _gameHub!.On<PlaySoundMessage>("SoundPlayed", msg =>
                Dispatcher.UIThread.Post(() => OnSoundPlayed?.Invoke(msg)));
            _gameHub!.On("MusicStopped", () =>
                Dispatcher.UIThread.Post(() => OnMusicStopped?.Invoke()));
            _gameHub!.On<MindmapSyncPayload>("MindmapSynced", payload =>
                Dispatcher.UIThread.Post(() => OnMindmapSynced?.Invoke(payload)));
            _gameHub!.On<string>("MindmapRevoked", mapId =>
                Dispatcher.UIThread.Post(() => OnMindmapRevoked?.Invoke(mapId)));
            _gameHub!.On<MindmapNodeOp>("MindNodeUpserted", op =>
                Dispatcher.UIThread.Post(() => OnMindNodeUpserted?.Invoke(op)));
            _gameHub!.On<MindmapNodeMoveOp>("MindNodeMoved", op =>
                Dispatcher.UIThread.Post(() => OnMindNodeMoved?.Invoke(op)));
            _gameHub!.On<MindmapNodeDeleteOp>("MindNodeDeleted", op =>
                Dispatcher.UIThread.Post(() => OnMindNodeDeleted?.Invoke(op)));
            _gameHub!.On<MindmapLinkOp>("MindLinkUpserted", op =>
                Dispatcher.UIThread.Post(() => OnMindLinkUpserted?.Invoke(op)));
            _gameHub!.On<MindmapLinkDeleteOp>("MindLinkDeleted", op =>
                Dispatcher.UIThread.Post(() => OnMindLinkDeleted?.Invoke(op)));

            _gameHub!.Reconnected += async _ =>
            {
                await _gameHub.InvokeAsync("RegisterUser", UserId, _sessionToken);
            };
        }

        public async Task ShareSoundChunkAsync(SoundChunkMessage chunk)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("ShareSoundChunk", chunk);
        }

        public async Task PlaySoundAsync(PlaySoundMessage msg)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("PlaySound", msg);
        }

        public async Task StopMusicForAllAsync()
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("StopMusicForAll");
        }

        public async Task PlaySoundForAsync(PlaySoundMessage msg, IEnumerable<string> userIds)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("PlaySoundFor", msg, userIds.ToArray());
        }

        public async Task SendCombatStateAsync(CombatStateMessage state)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("UpdateCombatState", _currentCampaignId, state);
        }

        public async Task SendCombatActionAsync(CombatActionMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("SendCombatAction", _currentCampaignId, msg);
        }

        public async Task SendCombatEconomyAsync(CombatEconomyMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("SendCombatEconomy", _currentCampaignId, msg);
        }

        public async Task SendDiceRollAsync(DiceRollMessage roll)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("SendDiceRoll", _currentCampaignId, roll);
        }

        public async Task LogRollAsync(string userId, string username, string summary)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("LogRoll", _currentCampaignId, userId, username, summary);
        }

        public async Task PushMindmapAsync(IEnumerable<string> userIds, MindmapSyncPayload payload)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("PushMindmap", userIds.ToArray(), payload);
        }

        public async Task RevokeMindmapAsync(string targetUserId, string mapId)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("RevokeMindmap", targetUserId, mapId);
        }

        public async Task PushMindNodeUpsertAsync(IEnumerable<string> userIds, MindmapNodeOp op)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("PushMindNodeUpsert", userIds.ToArray(), op);
        }

        public async Task PushMindNodeMoveAsync(IEnumerable<string> userIds, MindmapNodeMoveOp op)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("PushMindNodeMove", userIds.ToArray(), op);
        }

        public async Task PushMindNodeDeleteAsync(IEnumerable<string> userIds, MindmapNodeDeleteOp op)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("PushMindNodeDelete", userIds.ToArray(), op);
        }

        public async Task PushMindLinkUpsertAsync(IEnumerable<string> userIds, MindmapLinkOp op)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("PushMindLinkUpsert", userIds.ToArray(), op);
        }

        public async Task PushMindLinkDeleteAsync(IEnumerable<string> userIds, MindmapLinkDeleteOp op)
        {
            if (_gameHub == null || !IsConnected) return;
            await _gameHub.InvokeAsync("PushMindLinkDelete", userIds.ToArray(), op);
        }

        public async Task SubmitChangeAsync(ChangeNotification change)
        {
            if (_dataHub == null || !IsConnected) return;
            await _dataHub.InvokeAsync("BroadcastChange", _currentCampaignId, change);
        }

        public async Task OpenTradeAsync(TradeOfferMessage offer)
        {
            if (_dataHub == null || !IsConnected) return;
            await _dataHub.InvokeAsync("OpenTrade", _currentCampaignId, offer);
        }

        public async Task UpdateTradeAsync(TradeOfferMessage offer)
        {
            if (_dataHub == null || !IsConnected) return;
            await _dataHub.InvokeAsync("UpdateTrade", _currentCampaignId, offer);
        }

        public async Task RespondTradeAsync(TradeOfferMessage offer)
        {
            if (_dataHub == null || !IsConnected) return;
            await _dataHub.InvokeAsync("RespondTrade", _currentCampaignId, offer);
        }

        public async Task CancelTradeAsync(TradeOfferMessage offer)
        {
            if (_dataHub == null || !IsConnected) return;
            await _dataHub.InvokeAsync("CancelTrade", _currentCampaignId, offer);
        }

        public event Action<NotePresenceMessage>? OnNotePresence;

        public async Task SendNotePresenceAsync(NotePresenceMessage presence)
        {
            if (_noteHub == null || !IsConnected) return;
            await _noteHub.InvokeAsync("UpdateNotePresence", presence);
        }

        public async Task NotifyPageChangedAsync(string pageId, string changeType)
        {
            if (_noteHub == null || !IsConnected) return;
            await _noteHub.InvokeAsync("BroadcastPageChange", pageId, changeType, (NotePage?)null);
        }

        public async Task NotifyPageChangedAsync(NotePage page, string changeType)
        {
            if (_noteHub == null || !IsConnected || page == null) return;
            await _noteHub.InvokeAsync("BroadcastPageChange", page.Id, changeType, page);
        }

        public async Task NotifyShareAddedAsync(NotePage page, string targetUserId)
        {
            if (_noteHub == null || !IsConnected) return;
            await _noteHub.InvokeAsync("BroadcastShareAdded", page, targetUserId);
        }

        public async Task NotifyShareRemovedAsync(string pageId, string targetUserId)
        {
            if (_noteHub == null || !IsConnected) return;
            await _noteHub.InvokeAsync("BroadcastShareRemoved", pageId, targetUserId);
        }

        public async Task SendNoteUpdateAsync(string pageId, byte[] update)
        {
            if (_noteHub == null || !IsConnected || update == null || update.Length == 0) return;
            try { await _noteHub.InvokeAsync("BroadcastNoteUpdate", pageId, update); }
            catch (Exception ex) { ErrorLog.Log("[Notes] sending an edit failed, the next one carries it", ex); }
        }

        public async Task<byte[]> RequestNoteCatchUpAsync(string pageId, byte[] stateVector)
        {
            if (_noteHub == null || !IsConnected) return Array.Empty<byte>();
            try { return await _noteHub.InvokeAsync<byte[]>("RequestNoteCatchUp", pageId, stateVector) ?? Array.Empty<byte>(); }
            catch (Exception ex) { ErrorLog.Log("[Notes] catching up on a page failed", ex); return Array.Empty<byte>(); }
        }


        private void WireNoteHub()
        {
            _noteHub!.On<NotePageChangePayload>("NotePageChanged", msg =>
                Dispatcher.UIThread.Post(() => OnNotePageChanged?.Invoke(msg)));

            _noteHub!.On<NoteUpdatePayload>("NoteUpdated", msg =>
                Dispatcher.UIThread.Post(() => OnNoteUpdate?.Invoke(msg)));

            _noteHub!.On<NotePage>("NotePageInvited", page =>
                Dispatcher.UIThread.Post(() => OnNotePageInvited?.Invoke(page)));

            _noteHub!.On<string>("NotePageRevoked", pageId =>
                Dispatcher.UIThread.Post(() => OnNotePageRevoked?.Invoke(pageId)));

            _noteHub!.On<NotePresenceMessage>("NotePresence", p =>
                Dispatcher.UIThread.Post(() => OnNotePresence?.Invoke(p)));

            _noteHub!.Reconnected += async _ =>
            {
                await _noteHub.InvokeAsync("RegisterUser", UserId, _sessionToken);
                Dispatcher.UIThread.Post(() => OnNoteReconnected?.Invoke());
            };
        }


        public async Task ResizeTokenAsync(TokenResizedMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("ResizeToken", _currentCampaignId, msg);
        }
        public async Task RotateTokenAsync(TokenRotatedMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("RotateToken", _currentCampaignId, msg);
        }

        public async Task SendFogPaintAsync(FogPaintMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("PaintFog", _currentCampaignId, msg);
        }

        public async Task SendFogStateAsync(FogStateMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("UpdateFog", _currentCampaignId, msg);
        }

        public async Task<FogStateMessage?> FetchFogAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return null;
            return await _mapHub.InvokeAsync<FogStateMessage?>("FetchFog", _currentCampaignId, mapId);
        }

        public async Task SendWallStateAsync(WallStateMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("UpdateWalls", _currentCampaignId, msg);
        }

        public async Task SendDoorToggleAsync(DoorToggleMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("ToggleDoor", _currentCampaignId, msg);
        }

        public async Task<WallStateMessage?> FetchWallsAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return null;
            return await _mapHub.InvokeAsync<WallStateMessage?>("FetchWalls", _currentCampaignId, mapId);
        }

        public async Task SendTerrainStateAsync(TerrainStateMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("UpdateTerrain", _currentCampaignId, msg);
        }

        public async Task<TerrainStateMessage?> FetchTerrainAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return null;
            return await _mapHub.InvokeAsync<TerrainStateMessage?>("FetchTerrain", _currentCampaignId, mapId);
        }

        public async Task<AoeTemplateStateMessage?> FetchAoeTemplatesAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return null;
            return await _mapHub.InvokeAsync<AoeTemplateStateMessage?>("FetchAoeTemplates", _currentCampaignId, mapId);
        }

        public async Task SendMapObjectsStateAsync(MapObjectStateMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("UpdateMapObjects", _currentCampaignId, msg);
        }

        public async Task<MapObjectStateMessage?> FetchMapObjectsAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return null;
            return await _mapHub.InvokeAsync<MapObjectStateMessage?>("FetchMapObjects", _currentCampaignId, mapId);
        }

        public async Task<CombatStateMessage?> FetchCombatStateAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return null;
            return await _mapHub.InvokeAsync<CombatStateMessage?>("FetchCombatState", _currentCampaignId, mapId);
        }

        public async Task<List<TokenAddedMessage>> FetchTokensAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return new List<TokenAddedMessage>();
            return await _mapHub.InvokeAsync<List<TokenAddedMessage>>("FetchTokens", _currentCampaignId, mapId);
        }

        public async Task SendAoeTemplateAsync(AoeTemplateMessage msg)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("PlaceAoeTemplate", _currentCampaignId, msg);
        }

        public async Task SendClearAoeTemplatesAsync(string mapId)
        {
            if (_mapHub == null || !IsConnected) return;
            await _mapHub.InvokeAsync("ClearAoeTemplates", _currentCampaignId, mapId);
        }

        public async Task DisconnectAsync()
        {
            IsConnected = false;
            var hubs = new[] { _gameHub, _chatHub, _dataHub, _mapHub, _noteHub };
            await Task.WhenAll(hubs
                .Where(h => h != null)
                .Select(h => h!.StopAsync()));
        }

        public async Task StopServerAsync()
        {
            var host = _webHost;
            if (host == null) return;
            _webHost = null;
            try
            {
                await host.StopAsync();
            }
            catch (Exception ex) { ErrorLog.Log("Stopping the host failed", ex); }
            finally
            {
                host.Dispose();
                ServerFingerprint = "";
            }
        }

        private static readonly string _transportPrefPath = Path.Combine(GlobalVariables.AppDataLocal, "transport.txt");
        public static bool PlainHttpPreferred
        {
            get { try { return File.Exists(_transportPrefPath) && File.ReadAllText(_transportPrefPath).Trim().Equals("http", StringComparison.OrdinalIgnoreCase); } catch { return false; } }
            set { try { File.WriteAllText(_transportPrefPath, value ? "http" : "https"); } catch (Exception ex) { ErrorLog.Log("Could not save the transport preference", ex); } }
        }

        public string ServerFingerprint { get; private set; } = "";

        public async Task StartServer(string ip, string port, DatabaseManager dbManager, ActiveCampaignContext ctx)
        {
            ServerIp = ip;
            ServerPort = port;

            await Task.Run(FirewallHelper.EnsureFirewallRules);

            var cert = PlainHttpPreferred ? null : HostCertificate.LoadOrCreate();
            ServerFingerprint = cert == null ? "" : HostCertificate.ShortFingerprint(cert);
            _webHost = BuildHubHost((cert == null ? "http" : "https") + $"://0.0.0.0:{ServerPort}", dbManager, ctx, cert);
            await _webHost.StartAsync();
        }

        // A test hosts the same hubs on loopback and a random port, and it has no business poking the firewall to do it.
        internal static IWebHost BuildHubHost(string url, DatabaseManager dbManager, ActiveCampaignContext ctx, X509Certificate2? cert = null)
        {
            return new WebHostBuilder()
                .UseKestrel(k => { if (cert != null) k.ConfigureHttpsDefaults(h => h.ServerCertificate = cert); })
                .UseUrls(url)
                .ConfigureLogging(logging =>
                {
                    logging.AddDebug();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSignalR(options =>
                    {
#if DEBUG
                        options.EnableDetailedErrors = true;
#else
                        options.EnableDetailedErrors = false;
#endif
                        options.MaximumReceiveMessageSize = 100 * 1024 * 1024;
                    });
                    services.AddSingleton(dbManager);
                    services.AddSingleton(ctx);
                    services.AddSingleton<CampaignRepository>();
                    services.AddSingleton<NotePageRepository>();
                    services.AddSingleton<MindmapRepository>();
                    services.AddSingleton<SessionLogService>();
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapHub<GameHub>("/gamehub");
                        endpoints.MapHub<ChatHub>("/chat");
                        endpoints.MapHub<DataExchangeHub>("/datexchange");
                        endpoints.MapHub<BattlemapHub>("/map");
                        endpoints.MapHub<NotePageHub>("/notepage");
                    });
                })
                .Build();
        }

        public event Action<string>? OnInvitedToChannel;

        public async Task InviteToChannelAsync(string channelId, string targetUserId)
        {
            if (_chatHub == null || !IsConnected) return;
            await _chatHub.InvokeAsync("InviteToChannel", channelId, targetUserId);
        }
    }
}