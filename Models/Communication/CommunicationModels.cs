using Avalonia;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Net;

namespace Dujahit.Models.Communication
{
    /* This is a machine check and not a user check. Anything running on the dm's box is the dm, because loopback is the whole test.
       Fine on my own machine, not fine the moment the host transport goes anywhere, and the per user token card is where that gets fixed.
       Two clients in one process are both loopback, so a test has to be able to say who is who, which is what the override is for.
    */
    internal static class HubGuard
    {
        internal static Func<HubCallerContext, bool>? HostCheckOverride;

        public static bool IsHost(HubCallerContext ctx)
        {
            var over = HostCheckOverride;
            if (over != null) return over(ctx);
            var ip = ctx.GetHttpContext()?.Connection.RemoteIpAddress;
            return ip != null && IPAddress.IsLoopback(ip);
        }
    }

    // A token only protects a name that joined this server run, a host restart forgets them all and the join code is the gate again, real per user auth is still to come
    internal static class SessionTokens
    {
        private static readonly ConcurrentDictionary<string, string> _byUser = new(StringComparer.OrdinalIgnoreCase);

        internal static string Issue(string userId)
        {
            var token = Guid.NewGuid().ToString("N");
            _byUser[userId] = token;
            return token;
        }

        internal static void Adopt(string userId, string token) => _byUser[userId] = token;

        internal static bool HasToken(string userId) =>
            !string.IsNullOrEmpty(userId) && _byUser.ContainsKey(userId);

        internal static bool IsValid(string userId, string token) =>
            !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(token)
            && _byUser.TryGetValue(userId, out var t) && string.Equals(t, token, StringComparison.Ordinal);

        internal static bool CanBind(HubCallerContext ctx, string userId, string token) =>
            HubGuard.IsHost(ctx) || !HasToken(userId) || IsValid(userId, token);

        internal static void Reset() => _byUser.Clear();
    }

    public class ChatHub : Hub
    {
        private readonly CampaignRepository _repo;

        public ChatHub(CampaignRepository repo)
        {
            _repo = repo;
        }

        public async Task SendChatMessage(ChatMessage msg)
        {
            Debug.WriteLine($"[HUB] SendChatMessage entry: channel={msg.ChannelId}");
            msg.Timestamp = DateTime.UtcNow;
            if (string.IsNullOrEmpty(msg.ChannelId)) msg.ChannelId = "general";
            await _repo.SaveChatMessageAsync(msg);
            Debug.WriteLine($"[HUB] Broadcasting to channel_{msg.ChannelId}");
            await Clients.Group($"channel_{msg.ChannelId}").SendAsync("ReceiveChatMessage", msg);
            Debug.WriteLine($"[HUB] Broadcast complete");
        }

        public async Task CreateChannel(string campaignId, string channelName)
        {
            if (string.IsNullOrWhiteSpace(channelName))
                return;

            var channel = new ChatChannel
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = campaignId,
                Name = channelName.Trim(),
                Description = null,
                CreatedAt = DateTime.UtcNow
            };

            await _repo.SaveChatChannelAsync(channel);

            await Clients.All.SendAsync("ChannelCreated", channel);
        }

        public async Task JoinChannel(string channelId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"channel_{channelId}");
        }

        public async Task<List<ChatMessage>> GetRecentMessages(string channelId)
        {
            return await _repo.LoadMessagesAsync(channelId, 100);
        }

        public async Task LeaveChannel(string channelId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"channel_{channelId}");
        }

        public async Task InviteToChannel(string channelId, string targetUserId)
        {
            var connectionIds = ConnectionTracker.GetConnectionsForUser(targetUserId);

            foreach (var connId in connectionIds)
            {
                await Groups.AddToGroupAsync(connId, $"channel_{channelId}");
                await Clients.Client(connId).SendAsync("InvitedToChannel", channelId);
            }
        }

        public async Task WhisperToDm(string campaignId, string fromUsername, string text)
        {
            var dmUserId = await _repo.GetDmUserIdAsync(campaignId);
            if (string.IsNullOrEmpty(dmUserId)) return;

            foreach (var connId in ConnectionTracker.GetConnectionsForUser(dmUserId))
            {
                if (connId == Context.ConnectionId) continue;
                await Clients.Client(connId).SendAsync("ReceiveWhisper", fromUsername, text);
            }
        }

        public async Task WhisperToUser(string targetUserId, string fromUsername, string text)
        {
            if (string.IsNullOrEmpty(targetUserId)) return;

            foreach (var connId in ConnectionTracker.GetConnectionsForUser(targetUserId))
            {
                if (connId == Context.ConnectionId) continue;
                await Clients.Client(connId).SendAsync("ReceiveWhisper", fromUsername, text);
            }
        }

        public Task RegisterUser(string userId, string token = "")
        {
            if (!SessionTokens.CanBind(Context, userId, token)) return Task.CompletedTask;
            ConnectionTracker.Add(userId, Context.ConnectionId);
            Context.Items["userId"] = userId;
            return Task.CompletedTask;
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("userId", out var u) && u is string uid)
            {
                if (ConnectionTracker.RemoveAndIsLast(uid, Context.ConnectionId))
                    await Clients.Others.SendAsync("PlayerLeft", uid);
            }
            await base.OnDisconnectedAsync(exception);
        }
    }

    public class GameHub : Hub // Send character change information (this one needs to be suuuuuper modified to make it efficient) and have most like regular game info data stuff being sent the dataexchange is supposed to be more "infrastructure" based and this is supposed to be more gameplay based
    {
        private readonly CampaignRepository _repo;
        private readonly SessionLogService _sessionLog;
        private readonly MindmapRepository _mindmaps;

        public GameHub(CampaignRepository repo, SessionLogService sessionLog, MindmapRepository mindmaps)
        {
            _repo = repo;
            _sessionLog = sessionLog;
            _mindmaps = mindmaps;
        }

        private string CallerUid() => Context.Items.TryGetValue("userId", out var v) ? v as string ?? "" : "";

        public async Task SendDiceRoll(string campaignId, DiceRollMessage roll)
        {
            await _repo.SaveDiceRollAsync(campaignId, roll);

            var label = string.IsNullOrWhiteSpace(roll.Label) ? "" : $" [{roll.Label}]";
            var visibility = roll.IsPrivate ? " (private)" : "";
            var summary = $"{roll.Username} rolled {roll.Expression} = {roll.Total}{label}{visibility}";
            await _sessionLog.LogAsync("DiceRoll", roll.UserId, roll.Username, summary, JsonSerializer.Serialize(roll), roll.RollId);

            if (roll.IsPrivate)
            {
                await Clients.Caller.SendAsync("ReceiveDiceRoll", roll);
                return;
            }

            await Clients.All.SendAsync("ReceiveDiceRoll", roll);
        }

        public async Task LogRoll(string campaignId, string userId, string username, string summary)
        {
            if (!HubGuard.IsHost(Context) && CallerUid() != userId) return;
            await _sessionLog.LogAsync("Roll", userId, username, summary);
        }

        public async Task SendImage(string channel, string user, string imageBase64)
        {
            await Clients.All.SendAsync("ReceiveImage", channel, user, imageBase64);
        }


        public async Task SendCustomData(string channel, string messageType, object data)
        {
            await Clients.All.SendAsync("ReceiveCustomData", channel, messageType, data);
        }

        public async Task ShareSoundChunk(SoundChunkMessage chunk)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.Others.SendAsync("SoundChunkShared", chunk);
        }

        public async Task PlaySound(PlaySoundMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.Others.SendAsync("SoundPlayed", msg);
        }

        public async Task StopMusicForAll()
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.Others.SendAsync("MusicStopped");
        }

        public async Task RegisterUser(string userId, string token = "")
        {
            if (!SessionTokens.CanBind(Context, userId, token)) return;
            ConnectionTracker.Add(userId, Context.ConnectionId);
            Context.Items["userId"] = userId;

            // Only lives in the host mirror while they are offline, so registering is when it turns up.
            foreach (var mapId in await _mindmaps.ListMapIdsSharedWithAsync(userId))
            {
                var map = await _mindmaps.GetMapAsync(mapId);
                if (map == null || map.Scope != "shared") continue;
                await Clients.Caller.SendAsync("MindmapSynced", new MindmapSyncPayload
                {
                    Map = map,
                    Nodes = await _mindmaps.LoadNodesAsync(mapId),
                    Links = await _mindmaps.LoadLinksAsync(mapId)
                });
            }
        }

        public async Task PlaySoundFor(PlaySoundMessage msg, string[] userIds)
        {
            if (!HubGuard.IsHost(Context)) return;
            foreach (var uid in userIds)
                foreach (var cn in ConnectionTracker.GetConnectionsForUser(uid))
                    await Clients.Client(cn).SendAsync("SoundPlayed", msg);
        }

        public async Task PushMindmap(string[] userIds, MindmapSyncPayload payload)
        {
            if (payload?.Map == null || userIds == null) return;

            var isHost = HubGuard.IsHost(Context);
            if (!isHost)
            {
                var known = await _mindmaps.GetMapAsync(payload.Map.Id);
                var allowed = known == null
                    ? string.Equals(payload.Map.OwnerUserId, CallerUid(), StringComparison.OrdinalIgnoreCase)
                    : await CanEditMindmapAsync(payload.Map.Id);
                if (!allowed) return;

                // Host db mirrors any shared map that goes through it, skip this row and the owner's own next edit comes back refused like they are a stranger
                await _mindmaps.SaveMapAsync(payload.Map);
                foreach (var l in await _mindmaps.LoadLinksAsync(payload.Map.Id)) await _mindmaps.DeleteLinkAsync(l.Id);
                foreach (var n in await _mindmaps.LoadNodesAsync(payload.Map.Id)) await _mindmaps.DeleteNodeAsync(n.Id);
                if (payload.Nodes != null) foreach (var n in payload.Nodes) await _mindmaps.SaveNodeAsync(n);
                if (payload.Links != null) foreach (var l in payload.Links) await _mindmaps.SaveLinkAsync(l);
                foreach (var uid in userIds)
                    if (!string.IsNullOrEmpty(uid)) await _mindmaps.ShareMapAsync(payload.Map.Id, uid);
            }

            foreach (var uid in userIds)
                foreach (var cn in ConnectionTracker.GetConnectionsForUser(uid))
                    await Clients.Client(cn).SendAsync("MindmapSynced", payload);
        }

        private async Task<bool> CanEditMindmapAsync(string? mapId)
        {
            if (HubGuard.IsHost(Context)) return true;
            if (string.IsNullOrEmpty(mapId)) return false;
            var uid = CallerUid();
            if (string.IsNullOrEmpty(uid)) return false;
            var map = await _mindmaps.GetMapAsync(mapId);
            if (map == null) return false;
            if (string.Equals(map.OwnerUserId, uid, StringComparison.OrdinalIgnoreCase)) return true;
            if (map.Scope != "shared") return false;
            var shares = await _mindmaps.ListShareUserIdsAsync(mapId);
            return shares.Any(s => string.Equals(s, uid, StringComparison.OrdinalIgnoreCase));
        }

        // Every op lands in the host mirror too, otherwise the copy that serves late joiners drifts stale the moment somebody edits
        public async Task PushMindNodeUpsert(string[] userIds, MindmapNodeOp op)
        {
            if (!await CanEditMindmapAsync(op?.MapId)) return;
            if (!HubGuard.IsHost(Context) && op!.Node != null) await _mindmaps.SaveNodeAsync(op.Node);
            await SendMindOp(userIds, "MindNodeUpserted", op);
        }

        public async Task PushMindNodeMove(string[] userIds, MindmapNodeMoveOp op)
        {
            if (!await CanEditMindmapAsync(op?.MapId)) return;
            if (!HubGuard.IsHost(Context)) await _mindmaps.UpdateNodePositionAsync(op!.NodeId, op.X, op.Y);
            await SendMindOp(userIds, "MindNodeMoved", op);
        }

        public async Task PushMindNodeDelete(string[] userIds, MindmapNodeDeleteOp op)
        {
            if (!await CanEditMindmapAsync(op?.MapId)) return;
            if (!HubGuard.IsHost(Context)) await _mindmaps.DeleteNodeAsync(op!.NodeId);
            await SendMindOp(userIds, "MindNodeDeleted", op);
        }

        public async Task PushMindLinkUpsert(string[] userIds, MindmapLinkOp op)
        {
            if (!await CanEditMindmapAsync(op?.MapId)) return;
            if (!HubGuard.IsHost(Context) && op!.Link != null) await _mindmaps.SaveLinkAsync(op.Link);
            await SendMindOp(userIds, "MindLinkUpserted", op);
        }

        public async Task PushMindLinkDelete(string[] userIds, MindmapLinkDeleteOp op)
        {
            if (!await CanEditMindmapAsync(op?.MapId)) return;
            if (!HubGuard.IsHost(Context)) await _mindmaps.DeleteLinkAsync(op!.LinkId);
            await SendMindOp(userIds, "MindLinkDeleted", op);
        }

        private async Task SendMindOp(string[] userIds, string method, object? op)
        {
            foreach (var uid in userIds)
                foreach (var cn in ConnectionTracker.GetConnectionsForUser(uid))
                    await Clients.Client(cn).SendAsync(method, op);
        }

        public async Task RevokeMindmap(string targetUserId, string mapId)
        {
            if (!await CanManageMindmapAsync(mapId)) return;
            foreach (var cn in ConnectionTracker.GetConnectionsForUser(targetUserId))
                await Clients.Client(cn).SendAsync("MindmapRevoked", mapId);
        }

        private async Task<bool> CanManageMindmapAsync(string mapId)
        {
            if (HubGuard.IsHost(Context)) return true;
            if (string.IsNullOrEmpty(mapId)) return false;
            var uid = CallerUid();
            if (string.IsNullOrEmpty(uid)) return false;
            var map = await _mindmaps.GetMapAsync(mapId);
            return map != null && string.Equals(map.OwnerUserId, uid, StringComparison.OrdinalIgnoreCase);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("userId", out var u) && u is string uid)
            {
                if (ConnectionTracker.RemoveAndIsLast(uid, Context.ConnectionId))
                    await Clients.Others.SendAsync("PlayerLeft", uid);
            }
            await base.OnDisconnectedAsync(exception);
        }
    }

    public class NotePageHub : Hub
    {
        private readonly NotePageRepository _repo;

        public NotePageHub(NotePageRepository repo) => _repo = repo;

        public async Task RegisterUser(string userId, string token = "")
        {
            if (!SessionTokens.CanBind(Context, userId, token)) return;
            ConnectionTracker.Add(userId, Context.ConnectionId);
            Context.Items["userId"] = userId;

            // Same deal as the mindmaps, an invite that fired while this user was offline is re-served off the host mirror on the way in, the recievers revision guard makes a repeat harmless
            foreach (var pageId in await _repo.ListPageIdsSharedWithAsync(userId))
            {
                var page = await _repo.GetByIdAsync(pageId);
                if (page != null) await Clients.Caller.SendAsync("NotePageInvited", page);
            }
        }

        private string CallerUid() => Context.Items.TryGetValue("userId", out var v) ? v as string ?? "" : "";

        // Whole page goes over the wire. Reading the host db instead misses a player's edit and freezes the share at whatever it was when it went out.
        public async Task BroadcastPageChange(string pageId, string changeType, NotePage? page = null)
        {
            if (page != null && !string.Equals(page.Id, pageId, StringComparison.Ordinal)) return;

            if (page != null && !HubGuard.IsHost(Context))
            {
                var known = await _repo.GetByIdAsync(pageId);
                var uid = CallerUid();
                var allowed = known == null
                    ? string.Equals(page.OwnerUserId, uid, StringComparison.Ordinal)
                    : string.Equals(known.OwnerUserId, uid, StringComparison.Ordinal)
                      || (await _repo.ListSharesAsync(pageId)).Any(s => string.Equals(s.UserId, uid, StringComparison.OrdinalIgnoreCase));
                if (!allowed) return;
            }

            if (page != null) await _repo.UpsertRemoteAsync(page);
            if (changeType == "removed" && page == null)
            {
                var known = await _repo.GetByIdAsync(pageId);
                var canRemove = HubGuard.IsHost(Context)
                    || known == null
                    || string.Equals(known.OwnerUserId, CallerUid(), StringComparison.Ordinal);
                if (canRemove && known != null) await _repo.DeleteAsync(pageId);
            }

            page ??= await _repo.GetByIdAsync(pageId);
            if (page == null && changeType != "removed") return;

            var audience = page != null
                ? await _repo.ResolveAudienceAsync(pageId)
                : new HashSet<string>();

            await SendToAudienceAsync(audience, "NotePageChanged",
                new NotePageChangePayload
                {
                    PageId = pageId,
                    ChangeType = changeType,
                    Page = page
                },
                broadcastAllIfEmpty: changeType == "removed");
        }

        private async Task<bool> MayTouchPageAsync(string pageId)
        {
            if (HubGuard.IsHost(Context)) return true;
            var known = await _repo.GetByIdAsync(pageId);
            if (known == null) return false;
            var uid = CallerUid();
            if (string.Equals(known.OwnerUserId, uid, StringComparison.Ordinal)) return true;
            return (await _repo.ListSharesAsync(pageId)).Any(s => string.Equals(s.UserId, uid, StringComparison.OrdinalIgnoreCase));
        }

        /* The host keeps every delta so somebody joining later gets the page as it is rather than as it was when the host last typed.
           Appending is the whole job here, a keystroke run costs its own size instead of rewriting the page, and the fold back down
           into one snapshot happens on the compaction step once enough of them have stacked up.
        */
        public async Task BroadcastNoteUpdate(string pageId, byte[] update)
        {
            if (string.IsNullOrEmpty(pageId) || update == null || update.Length == 0) return;
            if (!await MayTouchPageAsync(pageId)) return;

            await _repo.EnsureCrdtSeedAsync(pageId);
            await _repo.AppendCrdtUpdateAsync(pageId, update, null);
            await _repo.CompactIfNeededAsync(pageId);

            var audience = await _repo.ResolveAudienceAsync(pageId);
            await SendToAudienceAsync(audience, "NoteUpdated",
                new NoteUpdatePayload { PageId = pageId, Update = update, FromUserId = CallerUid() },
                broadcastAllIfEmpty: false);
        }

        public async Task<byte[]> RequestNoteCatchUp(string pageId, byte[] stateVector)
        {
            if (string.IsNullOrEmpty(pageId)) return Array.Empty<byte>();
            if (!await MayTouchPageAsync(pageId)) return Array.Empty<byte>();

            var stored = await _repo.LoadCrdtStateAsync(pageId);
            var tail = await _repo.LoadCrdtUpdatesAsync(pageId);
            var page = await _repo.GetByIdAsync(pageId);
            var hasHistory = (stored != null && stored.Length > 0) || tail.Count > 0;
            if (!hasHistory && string.IsNullOrEmpty(page?.ContentMarkdown)) return Array.Empty<byte>();

            using var doc = hasHistory
                ? NoteCrdt.FromState(stored, tail)
                : NoteCrdt.FromText(page?.ContentMarkdown ?? "");
            if (!hasHistory)
                await _repo.SaveCrdtStateAsync(pageId, doc.FullState(), doc.Text);

            return doc.DiffAgainst(stateVector);
        }

        public async Task BroadcastShareAdded(NotePage page, string targetUserId)
        {
            if (page == null) return;
            if (!HubGuard.IsHost(Context) && !string.Equals(page.OwnerUserId, CallerUid(), StringComparison.Ordinal)) return;

            // Host db keeps a copy of every shared page and its share rows, that is how a later edit knows who to send to and how somebody who was offline for the invite still gets it
            await _repo.SaveInvitedPageAsync(page, targetUserId);

            foreach (var cn in ConnectionTracker.GetConnectionsForUser(targetUserId))
                await Clients.Client(cn).SendAsync("NotePageInvited", page);

            foreach (var cn in ConnectionTracker.GetConnectionsForUser(page.OwnerUserId ?? ""))
                await Clients.Client(cn).SendAsync("NotePageChanged",
                    new NotePageChangePayload
                    {
                        PageId = page.Id,
                        ChangeType = "updated",
                        Page = page
                    });
        }

        public async Task BroadcastShareRemoved(string pageId, string targetUserId)
        {
            foreach (var cn in ConnectionTracker.GetConnectionsForUser(targetUserId))
                await Clients.Client(cn).SendAsync("NotePageRevoked", pageId);
        }

        public async Task UpdateNotePresence(NotePresenceMessage p)
        {
            if (p == null || string.IsNullOrEmpty(p.PageId)) return;
            if (!HubGuard.IsHost(Context)) p = p with { UserId = CallerUid() };
            var audience = await _repo.ResolveAudienceAsync(p.PageId);
            foreach (var userId in audience)
                foreach (var cn in ConnectionTracker.GetConnectionsForUser(userId))
                    if (cn != Context.ConnectionId)
                        await Clients.Client(cn).SendAsync("NotePresence", p);
        }

        private async Task SendToAudienceAsync(
            HashSet<string> audience, string method, object payload, bool broadcastAllIfEmpty = false)
        {
            if (audience.Count == 0 && broadcastAllIfEmpty)
            {
                await Clients.All.SendAsync(method, payload);
                return;
            }

            foreach (var userId in audience)
            {
                foreach (var cn in ConnectionTracker.GetConnectionsForUser(userId))
                    await Clients.Client(cn).SendAsync(method, payload);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("userId", out var u) && u is string uid)
            {
                if (ConnectionTracker.RemoveAndIsLast(uid, Context.ConnectionId))
                    await Clients.Others.SendAsync("PlayerLeft", uid);
            }
            await base.OnDisconnectedAsync(exception);
        }
    }

    public class NotePageChangePayload
    {
        public string PageId { get; set; } = "";
        public string ChangeType { get; set; } = "";
        public NotePage? Page { get; set; }
    }

    public class NoteUpdatePayload
    {
        public string PageId { get; set; } = "";
        public byte[] Update { get; set; } = Array.Empty<byte>();
        public string FromUserId { get; set; } = "";
    }

    public record NotePresenceMessage(string PageId, string UserId, string Username, int Line, bool IsEditing, string ColorHex = "");

    public class DataExchangeHub : Hub
    {
        private readonly CampaignRepository _repo;
        private readonly SessionLogService _sessionLog;

        public DataExchangeHub(CampaignRepository repo, SessionLogService sessionLog)
        {
            _repo = repo;
            _sessionLog = sessionLog;
        }

        public async Task<CampaignBootstrapPayload> JoinCampaign(string userId, string username, string secret, string token = "")
        {
            if (!HubGuard.IsHost(Context))
            {
                var expected = await _repo.GetActiveJoinSecretAsync();
                if (!string.IsNullOrEmpty(expected) && !string.Equals(secret, expected, StringComparison.Ordinal))
                    throw new HubException("Wrong join code, ask the dm for the current one.");
                var dm = await _repo.GetActiveDmUserIdAsync();
                if (!string.IsNullOrEmpty(dm) && string.Equals(userId, dm, StringComparison.Ordinal))
                    throw new HubException("That account can't join from another machine.");
                if (SessionTokens.HasToken(userId) && !SessionTokens.IsValid(userId, token)
                    && ConnectionTracker.GetConnectionsForUser(userId).Count > 0)
                    throw new HubException("That account is connected right now, wait for the old session to drop.");
            }

            ConnectionTracker.Add(userId, Context.ConnectionId);
            Context.Items["userId"] = userId;

            await _repo.EnsureMemberAsync(userId, username);

            var payload = await _repo.BuildBootstrapAsync(userId);
            payload.SessionToken = SessionTokens.Issue(userId);

            var myColor = PlayerColorRegistry.GetOrAssign(userId);
            payload.AssignedColor = myColor;

            var colors = PlayerColorRegistry.Snapshot();
            foreach (var m in payload.Members)
                if (colors.TryGetValue(m.UserId, out var c)) m.Color = c;

            await Groups.AddToGroupAsync(Context.ConnectionId, payload.CampaignId);
            await Clients.OthersInGroup(payload.CampaignId)
                .SendAsync("PlayerJoined", userId, username);
            await Clients.OthersInGroup(payload.CampaignId)
                .SendAsync("PlayerColorChanged", userId, myColor);

            return payload;
        }

        public async Task RejoinCampaign(string campaignId, string userId, string username, string secret, long sinceChangeId, string token = "")
        {
            if (!HubGuard.IsHost(Context))
            {
                var expected = await _repo.GetActiveJoinSecretAsync();
                if (!string.IsNullOrEmpty(expected) && !string.Equals(secret, expected, StringComparison.Ordinal))
                    throw new HubException("Wrong join code, ask the dm for the current one.");
                var dm = await _repo.GetActiveDmUserIdAsync();
                if (!string.IsNullOrEmpty(dm) && string.Equals(userId, dm, StringComparison.Ordinal))
                    throw new HubException("That account can't join from another machine.");
                if (SessionTokens.HasToken(userId) && !SessionTokens.IsValid(userId, token)
                    && ConnectionTracker.GetConnectionsForUser(userId).Count > 0)
                    throw new HubException("That account is connected right now, wait for the old session to drop.");
                // Taking the client's own token after a host restart keeps the other four hubs working without a re-key race. Pretty sure a stranger cannot reach this line while the real user is online.
                if (!SessionTokens.IsValid(userId, token))
                {
                    if (!string.IsNullOrEmpty(token) && !SessionTokens.HasToken(userId)) SessionTokens.Adopt(userId, token);
                    else await Clients.Caller.SendAsync("SessionTokenIssued", SessionTokens.Issue(userId));
                }
            }

            ConnectionTracker.Add(userId, Context.ConnectionId);
            Context.Items["userId"] = userId;

            var myColor = PlayerColorRegistry.GetOrAssign(userId);

            await Groups.AddToGroupAsync(Context.ConnectionId, campaignId);
            await Clients.OthersInGroup(campaignId)
                .SendAsync("PlayerJoined", userId, username);
            await Clients.Group(campaignId)
                .SendAsync("PlayerColorChanged", userId, myColor);

            var missed = await _repo.GetChangesSinceAsync(campaignId, sinceChangeId);
            foreach (var c in missed)
                await Clients.Caller.SendAsync("ReceiveChange", c);
        }

        public async Task<bool> RequestColor(string campaignId, string userId, string hex)
        {
            if (!PlayerColorRegistry.TryChange(userId, hex)) return false;
            await Clients.Group(campaignId).SendAsync("PlayerColorChanged", userId, hex);
            return true;
        }

        public async Task BroadcastChange(string campaignId, ChangeNotification change)
        {
            if (!HubGuard.IsHost(Context))
            {
                var uid = Context.Items.TryGetValue("userId", out var u) && u is string s ? s : "";
                if (!await _repo.IsChangeAuthorizedAsync(uid, change))
                {
                    Debug.WriteLine($"[Hub] dropped unauthorized change {change?.EntityType}/{change?.EntityId} from {uid}");
                    return;
                }
                try
                {
                    var actorName = await _repo.GetUsernameAsync(uid);
                    var isSheet = string.Equals(change.EntityType, "Character", StringComparison.OrdinalIgnoreCase);
                    var summary = isSheet
                        ? actorName + " saved a change to their character sheet."
                        : actorName + " " + (string.IsNullOrEmpty(change.ChangeType) ? "changed" : change.ChangeType) + " an item in their inventory.";
                    var detail = JsonSerializer.Serialize(new { change.EntityType, change.EntityId, change.ChangeType, change.RevisionNumber });
                    await _sessionLog.LogAsync(isSheet ? "SheetEdit" : "ItemUse", uid, actorName, summary, detail);
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("[Hub] session log write failed for a change", ex);
                }
            }
            change.ChangeId = await _repo.RecordChangeAsync(campaignId, change);
            await Clients.Group(campaignId)
                .SendAsync("ReceiveChange", change);
        }

        public async Task OpenTrade(string campaignId, TradeOfferMessage offer)
        {
            await SendTradeAsync(campaignId, offer, "TradeOffered");
        }

        public async Task UpdateTrade(string campaignId, TradeOfferMessage offer)
        {
            await SendTradeAsync(campaignId, offer, "TradeUpdated");
        }

        public async Task CancelTrade(string campaignId, TradeOfferMessage offer)
        {
            await SendTradeAsync(campaignId, offer, "TradeCancelled");
        }

        public async Task RespondTrade(string campaignId, TradeOfferMessage offer)
        {
            if (!(offer.From.Accepted && offer.To.Accepted))
            {
                await SendTradeAsync(campaignId, offer, "TradeUpdated");
                return;
            }

            var result = await _repo.ApplyTradeAsync(campaignId, offer);
            await SendTradeAsync(campaignId, offer, "TradeResult", result.Result);

            if (result.Result.Success)
            {
                try
                {
                    await _sessionLog.LogAsync("Trade", offer.From.UserId, offer.From.CharacterName,
                        offer.From.CharacterName + " and " + offer.To.CharacterName + " completed a trade.",
                        JsonSerializer.Serialize(offer), "trade-" + offer.TradeId);
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("[Hub] session log write failed for a trade", ex);
                }
                foreach (var ch in result.ChangedCharacters)
                    await Clients.Group(campaignId).SendAsync("ReceiveChange", ch);
                foreach (var inst in result.ChangedInstances)
                    await Clients.Group(campaignId).SendAsync("ReceiveChange", inst);
            }
        }

        private async Task SendTradeAsync(string campaignId, TradeOfferMessage offer, string method, object? payload = null)
        {
            var dm = await _repo.GetDmUserIdAsync(campaignId);
            var users = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { offer.From.UserId, offer.To.UserId };
            if (!string.IsNullOrEmpty(dm)) users.Add(dm);

            var body = payload ?? offer;
            var seen = new HashSet<string>();
            foreach (var uid in users)
                foreach (var cn in ConnectionTracker.GetConnectionsForUser(uid))
                    if (seen.Add(cn))
                        await Clients.Client(cn).SendAsync(method, body);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("userId", out var userIdObj) && userIdObj is string userId)
            {
                if (ConnectionTracker.RemoveAndIsLast(userId, Context.ConnectionId))
                    await Clients.Others.SendAsync("PlayerLeft", userId);
            }
            await base.OnDisconnectedAsync(exception);
        }
    }

    public class BattlemapHub : Hub
    {
        private readonly CampaignRepository _repo;
        private readonly SessionLogService _sessionLog;
        private const int MaxStrokePoints = 4000;
        internal const int MaxStrokesPerSecond = 30;
        private static readonly ConcurrentDictionary<string, (long WindowStart, int Count)> _strokeWindows = new();

        public BattlemapHub(CampaignRepository repo, SessionLogService sessionLog)
        {
            _repo = repo;
            _sessionLog = sessionLog;
        }

        private string CallerUid() => Context.Items.TryGetValue("uid", out var v) ? v as string ?? "" : "";

        private async Task<bool> CanTouchTokenAsync(string tokenId)
        {
            if (HubGuard.IsHost(Context)) return true;
            var owner = await _repo.GetTokenOwnerUserIdAsync(tokenId);
            return owner != null && owner == CallerUid();
        }

        public async Task JoinCampaignMap(string campaignId, string userId = "", string token = "")
        {
            if (!SessionTokens.CanBind(Context, userId, token)) return;
            Context.Items["uid"] = userId;
            if (!string.IsNullOrEmpty(userId)) ConnectionTracker.Add(userId, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, $"map_{campaignId}");
        }

        public async Task SendStroke(string campaignId, StrokeMessage stroke)
        {
            if (stroke?.Points == null || stroke.Points.Count > MaxStrokePoints) return;
            if (!StrokeAllowed(Context.ConnectionId, Environment.TickCount64)) return;
            Debug.WriteLine($"[Map] Stroke from {Context.ConnectionId} on {campaignId}: " +
                            $"{stroke.StrokeId} ({stroke.Points.Count} pts)");
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("ReceiveStroke", stroke);
        }

        internal static bool StrokeAllowed(string connectionId, long nowMs)
        {
            var entry = _strokeWindows.AddOrUpdate(connectionId,
                _ => (nowMs, 1),
                (_, e) => nowMs - e.WindowStart >= 1000 ? (nowMs, 1) : (e.WindowStart, e.Count + 1));
            return entry.Count <= MaxStrokesPerSecond;
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _strokeWindows.TryRemove(Context.ConnectionId, out _);
            if (Context.Items.TryGetValue("uid", out var u) && u is string uid && uid.Length > 0)
            {
                if (ConnectionTracker.RemoveAndIsLast(uid, Context.ConnectionId))
                    await Clients.Others.SendAsync("PlayerLeft", uid);
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task AddToken(string campaignId, TokenAddedMessage token)
        {
            if (!HubGuard.IsHost(Context))
            {
                if (string.IsNullOrEmpty(token.CharacterId)) return;
                var owner = await _repo.GetCharacterOwnerUserIdAsync(token.CharacterId);
                if (owner == null || owner != CallerUid()) return;
            }
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("TokenAdded", token);
        }

        public async Task MoveToken(string campaignId, TokenMovedMessage move)
        {
            if (!await CanTouchTokenAsync(move.TokenId)) return;
            if (!HubGuard.IsHost(Context))
            {
                try
                {
                    var uid = CallerUid();
                    var name = await _repo.GetUsernameAsync(uid);
                    await _sessionLog.LogAsync("TokenMove", uid, name, name + " moved their token.", JsonSerializer.Serialize(move));
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("[Hub] session log write failed for a token move", ex);
                }
            }
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("TokenMoved", move);
        }

        public async Task RemoveToken(string campaignId, TokenRemovedMessage rm)
        {
            if (!await CanTouchTokenAsync(rm.TokenId)) return;
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("TokenRemoved", rm);
        }

        public async Task SendPing(string campaignId, PingMessage ping) =>
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("PingReceived", ping);

        public async Task UndoStroke(string campaignId, string strokeId) =>
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("StrokeUndone", strokeId);

        public async Task DeactivateMap(string campaignId)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.Group($"map_{campaignId}").SendAsync("MapDeactivated");
        }

        public async Task UpdatePermissions(string campaignId, PermissionsUpdateMessage perms)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.Group($"map_{campaignId}").SendAsync("PermissionsUpdated", perms);
        }
        public async Task ResizeToken(string campaignId, TokenResizedMessage msg)
        {
            if (!await CanTouchTokenAsync(msg.TokenId)) return;
            await Clients.OthersInGroup($"map_{campaignId}")
                 .SendAsync("TokenResized", msg);
        }

        public async Task ActivateMap(string campaignId, MapActivatedMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.Group($"map_{campaignId}").SendAsync("MapActivated", msg);
        }

        public async Task RotateToken(string campaignId, TokenRotatedMessage msg)
        {
            if (!await CanTouchTokenAsync(msg.TokenId)) return;
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("TokenRotated", msg);
        }

        public async Task PaintFog(string campaignId, FogPaintMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("FogPainted", msg);
        }

        public async Task UpdateFog(string campaignId, FogStateMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("FogUpdated", msg);
        }

        public async Task<FogStateMessage?> FetchFog(string campaignId, string mapId) =>
            await _repo.FetchMapFogMessageAsync(campaignId, mapId);

        public async Task UpdateWalls(string campaignId, WallStateMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("WallsUpdated", msg);
        }

        public async Task ToggleDoor(string campaignId, DoorToggleMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("DoorToggled", msg);
        }

        public async Task<WallStateMessage?> FetchWalls(string campaignId, string mapId) =>
            await _repo.FetchMapWallsMessageAsync(campaignId, mapId);

        public async Task UpdateTerrain(string campaignId, TerrainStateMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("TerrainUpdated", msg);
        }

        public async Task<TerrainStateMessage?> FetchTerrain(string campaignId, string mapId) =>
            await _repo.FetchMapTerrainMessageAsync(campaignId, mapId);

        public async Task<AoeTemplateStateMessage?> FetchAoeTemplates(string campaignId, string mapId) =>
            await _repo.FetchMapAoeTemplatesMessageAsync(campaignId, mapId);

        public async Task UpdateMapObjects(string campaignId, MapObjectStateMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("MapObjectsUpdated", msg);
        }

        public async Task<MapObjectStateMessage?> FetchMapObjects(string campaignId, string mapId) =>
            await _repo.FetchMapObjectsMessageAsync(campaignId, mapId);

        public async Task<CombatStateMessage?> FetchCombatState(string campaignId, string mapId) =>
            await _repo.FetchCombatStateMessageAsync(campaignId, mapId);

        public async Task<List<TokenAddedMessage>> FetchTokens(string campaignId, string mapId)
        {
            var list = new List<TokenAddedMessage>();
            foreach (var row in await _repo.LoadMapTokensAsync(mapId))
            {
                string? b64 = null;
                if (!string.IsNullOrEmpty(row.TokenImagePath) && File.Exists(row.TokenImagePath))
                {
                    try { b64 = Convert.ToBase64String(await File.ReadAllBytesAsync(row.TokenImagePath)); }
                    catch (Exception) { }
                }
                var size = Enum.TryParse<CreatureSize>(row.SizeName, out var parsed) ? parsed : CreatureSize.Medium;
                list.Add(new TokenAddedMessage(row.Id, Guid.NewGuid().ToString("N"),
                    new SerializablePoint(row.X, row.Y), row.Scale, row.Rotation, size, b64, row.OwnerCharacterId,
                    row.IsProp, row.Blocks, row.BlocksSight));
            }
            return list;
        }

        public async Task PlaceAoeTemplate(string campaignId, AoeTemplateMessage msg) =>
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("AoeTemplatePlaced", msg);

        public async Task ClearAoeTemplates(string campaignId, string mapId)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("AoeTemplatesCleared", mapId);
        }

        public async Task UpdateCombatState(string campaignId, CombatStateMessage state)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("CombatStateUpdated", state);
        }

        public async Task SendCombatAction(string campaignId, CombatActionMessage msg) =>
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("CombatActionReceived", msg);

        public async Task SendCombatEconomy(string campaignId, CombatEconomyMessage msg)
        {
            msg = msg with { SenderUserId = CallerUid() };
            if (!HubGuard.IsHost(Context))
            {
                try
                {
                    var uid = CallerUid();
                    var name = await _repo.GetUsernameAsync(uid);
                    var what = msg.Kind switch
                    {
                        "weapon-attack" => "swung a weapon",
                        "cast-action" or "cast-bonus" or "cast-reaction" or "cast-none" => "cast a spell" + (msg.Level > 0 ? " at level " + msg.Level : ""),
                        "sheet-condition" => "spent a resource with a condition",
                        "inspire" => "handed out an inspiration die",
                        _ => "used " + msg.Kind
                    };
                    await _sessionLog.LogAsync("CombatAction", uid, name, name + " " + what + ".", JsonSerializer.Serialize(msg));
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("[Hub] session log write failed for a combat action", ex);
                }
            }
            await Clients.OthersInGroup($"map_{campaignId}")
                         .SendAsync("CombatEconomyReceived", msg);
        }
        public async Task<byte[]?> FetchMapImage(string campaignId, string mapId)
        {
            var dir = Path.Combine(
                GlobalVariables.AppDataLocal,
                "assets", campaignId, "maps");
            var match = Directory.EnumerateFiles(dir, mapId + ".*").FirstOrDefault();
            if (match == null) return null;
            return await File.ReadAllBytesAsync(match);
        }

        public async Task<List<PlayerMapSummary>> FetchPlayerMaps(string campaignId) =>
            await _repo.FetchPlayerVisibleMapsAsync(campaignId);

        public async Task RevealHandout(string campaignId, HandoutRevealedMessage msg)
        {
            if (!HubGuard.IsHost(Context)) return;
            if (!string.IsNullOrEmpty(msg.TargetUserId))
            {
                foreach (var cn in ConnectionTracker.GetConnectionsForUser(msg.TargetUserId))
                    await Clients.Client(cn).SendAsync("HandoutRevealed", msg);
                return;
            }
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("HandoutRevealed", msg);
        }

        public async Task HideHandout(string campaignId, string handoutId)
        {
            if (!HubGuard.IsHost(Context)) return;
            await Clients.OthersInGroup($"map_{campaignId}").SendAsync("HandoutHidden", handoutId);
        }

        public async Task<byte[]?> FetchHandout(string campaignId, string handoutId)
        {
            var dir = Path.Combine(
                GlobalVariables.AppDataLocal,
                "assets", campaignId, "handouts");
            var match = Directory.EnumerateFiles(dir, handoutId + ".*").FirstOrDefault();
            if (match == null) return null;
            return await File.ReadAllBytesAsync(match);
        }
    }

    public record SerializablePoint(double X, double Y);
    public record StrokeMessage(string StrokeId, List<SerializablePoint> Points, string Color, double Thickness, string OwnerId = "");
    public record TokenAddedMessage(string TokenId, string ImageId, SerializablePoint Position, double Scale, double Rotation, CreatureSize Size, string? ImageBase64 = null, string? CharacterId = null, bool IsProp = false, bool Blocks = true, bool BlocksSight = false);
    public record TokenMovedMessage(string TokenId, SerializablePoint NewPosition);
    public record TokenRemovedMessage(string TokenId);
    public record PingMessage(string UserId, SerializablePoint Position, string Color);
    public record PermissionsUpdateMessage(bool CanDraw, bool CanPing);
    public record TokenResizedMessage(string TokenId, CreatureSize Size, double Scale, bool IsProp = false, bool Blocks = true, bool BlocksSight = false);
    public record TokenRotatedMessage(string TokenId, double Rotation);
    public record SoundChunkMessage(string Id, string CampaignId, string Name, string Kind, string FileName, int Index, int Total, string Base64);
    public record PlaySoundMessage(string Id, string Kind, bool Loop, int Volume);
    public record FogCellPoint(int Col, int Row);
    public record FogPaintMessage(string MapId, List<FogCellPoint> Cells, bool Hidden, bool Seen = false);
    public record FogStateMessage(string MapId, bool Enabled, int Cols, int Rows, List<FogCellPoint> Hidden, List<FogCellPoint>? Seen = null);
    public record MapActivatedMessage(string MapId, string GridKind, double Scale, int Width = 0, int Height = 0, double GridOffsetX = 0, double GridOffsetY = 0);
    public record PlayerMapSummary(string MapId, string Name, string GridKind, double Scale);
    public record HandoutRevealedMessage(string HandoutId, string Name, string TargetUserId = "");
    public record CombatantSnapshot(string Id, string Name, int Initiative, int CurrentHp, int MaxHp, bool IsPlayerCharacter, bool RevealExactHp, string? TokenId, int MaxActions = 1, int ActionsRemaining = 1, int MaxBonusActions = 1, int BonusActionsRemaining = 1, string? SpellSlots = null, bool Concentration = false, int DeathSaveSuccesses = 0, int DeathSaveFailures = 0, string? AttacksJson = null, bool IsFriendly = false, string? ExtrasJson = null);
    public record CombatStateMessage(string EncounterId, string MapId, bool CombatActive, int Round, string? ActiveCombatantId, List<CombatantSnapshot> Combatants);
    public record CombatActionMessage(string SourceCombatantId, string TargetCombatantId, string SpellName, int BaseLevel, int CastAtLevel, string EffectsJson, string CastingTime, int AbilityMod, int Prof, int SaveDc, int AttackBonus, int CharLevel);
    public record CombatEconomyMessage(string MapId, string CombatantId, string Kind, string TargetId = "", string AllyId = "", int Level = 0, string ItemId = "", string SpellId = "", string SenderUserId = "");
    public record DiceRollMessage(string RollId, string UserId, string Username, string Expression, int Total, string Breakdown, string? Label, bool IsPrivate);
    public record WallSegment(string Id, double X1, double Y1, double X2, double Y2, bool IsDoor, bool DoorOpen, bool BlocksSight);
    public record WallStateMessage(string MapId, bool Enabled, List<WallSegment> Walls);
    public record TerrainStateMessage(string MapId, List<FogCellPoint> Cells);

    public record AoeTemplateStateMessage(string MapId, List<AoeTemplateMessage> Templates);
    public record MapObjectPoint(int Col, int Row, string ItemId);
    public record MapObjectStateMessage(string MapId, List<MapObjectPoint> Cells);
    public record DoorToggleMessage(string MapId, string WallId, bool Open);
    public record AoeTemplateMessage(string Id, string MapId, string Shape, double OriginX, double OriginY, double DirectionDeg, double SizeFt, double WidthFt, string Color, string Damage = "", string DamageType = "", string SaveAbility = "", int SaveDc = 0, string Trigger = "", string Label = "", int RoundsLeft = 0, string Terrain = "", string Condition = "", int ConditionRounds = 0, string OwnerId = "");

    public record TradeItemLine(string InstanceId, string BaseItemId, string Name, int Quantity);
    public record TradeCurrencyLine(string CurrencyId, long Amount);
    public record TradeSide(string UserId, string CharacterId, string CharacterName, List<TradeItemLine> Items, List<TradeCurrencyLine> Currency, bool Accepted);
    public record TradeOfferMessage(string TradeId, TradeSide From, TradeSide To);
    public record TradeResultMessage(string TradeId, bool Success, string Summary, string? Reason);
}
