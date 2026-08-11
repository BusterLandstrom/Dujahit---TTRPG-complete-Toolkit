using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.Models.Database
{
    public class MindmapRepository
    {
        private readonly DatabaseManager _db;

        public MindmapRepository(DatabaseManager db) => _db = db;

        public async Task<List<Mindmap>> ListVisibleMapsAsync(string campaignId, string userId, CancellationToken ct = default)
        {
            var result = new List<Mindmap>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, OwnerUserId, Scope, Title, ColorHex, RevisionNumber, CreatedAt, UpdatedAt
                FROM Mindmaps
                WHERE CampaignId = $cid
                  AND (
                        OwnerUserId = $uid
                     OR (Scope = 'shared' AND Id IN (SELECT MindmapId FROM MindmapShares WHERE UserId = $uid))
                      )
                ORDER BY Title COLLATE NOCASE";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", userId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) result.Add(ReadMap(r));
            return result;
        }

        public async Task<Mindmap?> GetMapAsync(string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, CampaignId, OwnerUserId, Scope, Title, ColorHex, RevisionNumber, CreatedAt, UpdatedAt
                FROM Mindmaps WHERE Id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? ReadMap(r) : null;
        }

        public async Task SaveMapAsync(Mindmap m, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO Mindmaps (Id, CampaignId, OwnerUserId, Scope, Title, ColorHex, RevisionNumber, CreatedAt, UpdatedAt)
                VALUES ($id, $cid, $owner, $scope, $title, $color, $rev, $created, $updated)
                ON CONFLICT(Id) DO UPDATE SET
                    Scope = excluded.Scope, Title = excluded.Title, ColorHex = excluded.ColorHex,
                    RevisionNumber = excluded.RevisionNumber, UpdatedAt = excluded.UpdatedAt";
            cmd.Parameters.AddWithValue("$id", m.Id);
            cmd.Parameters.AddWithValue("$cid", m.CampaignId);
            cmd.Parameters.AddWithValue("$owner", (object?)m.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$scope", m.Scope);
            cmd.Parameters.AddWithValue("$title", m.Title);
            cmd.Parameters.AddWithValue("$color", (object?)m.ColorHex ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rev", m.RevisionNumber);
            cmd.Parameters.AddWithValue("$created", m.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$updated", m.UpdatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> BumpMapRevisionAsync(string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE Mindmaps SET RevisionNumber = RevisionNumber + 1, UpdatedAt = $now
                WHERE Id = $id RETURNING RevisionNumber";
            cmd.Parameters.AddWithValue("$id", mapId);
            cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            var res = await cmd.ExecuteScalarAsync(ct);
            return res is long l ? (int)l : 0;
        }

        public async Task DeleteMapAsync(string mapId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Mindmaps WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", mapId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task ShareMapAsync(string mapId, string userId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO MindmapShares (MindmapId, UserId, SharedAt)
                VALUES ($mid, $uid, $when)
                ON CONFLICT(MindmapId, UserId) DO NOTHING";
            cmd.Parameters.AddWithValue("$mid", mapId);
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$when", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task UnshareMapAsync(string mapId, string userId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM MindmapShares WHERE MindmapId = $mid AND UserId = $uid";
            cmd.Parameters.AddWithValue("$mid", mapId);
            cmd.Parameters.AddWithValue("$uid", userId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<List<string>> ListMapIdsSharedWithAsync(string userId, CancellationToken ct = default)
        {
            var ids = new List<string>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT MindmapId FROM MindmapShares WHERE UserId = $uid;";
            cmd.Parameters.AddWithValue("$uid", userId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ids.Add(r.GetString(0));
            return ids;
        }

        public async Task<List<string>> ListShareUserIdsAsync(string mapId, CancellationToken ct = default)
        {
            var result = new List<string>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT UserId FROM MindmapShares WHERE MindmapId = $mid";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) result.Add(r.GetString(0));
            return result;
        }

        public async Task<List<string>> ResolveAudienceAsync(string mapId, CancellationToken ct = default)
        {
            var audience = new HashSet<string>(StringComparer.Ordinal);
            var map = await GetMapAsync(mapId, ct);
            if (map == null) return new List<string>();
            if (!string.IsNullOrEmpty(map.OwnerUserId)) audience.Add(map.OwnerUserId!);
            if (map.Scope == "shared")
                foreach (var uid in await ListShareUserIdsAsync(mapId, ct)) audience.Add(uid);
            return new List<string>(audience);
        }

        public async Task<List<MindmapNode>> LoadNodesAsync(string mapId, CancellationToken ct = default)
        {
            var result = new List<MindmapNode>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, MindmapId, CampaignId, Kind, Title, Body, ColorHex, NodeX, NodeY, Slug, CreatedAt
                FROM MindmapNodes WHERE MindmapId = $mid ORDER BY CreatedAt ASC";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) result.Add(ReadNode(r));
            return result;
        }

        public async Task SaveNodeAsync(MindmapNode n, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO MindmapNodes (Id, MindmapId, CampaignId, Kind, Title, Body, ColorHex, NodeX, NodeY, Slug, CreatedAt)
                VALUES ($id, $mid, $cid, $kind, $title, $body, $color, $x, $y, $slug, $created)
                ON CONFLICT(Id) DO UPDATE SET
                    Kind = excluded.Kind, Title = excluded.Title, Body = excluded.Body,
                    ColorHex = excluded.ColorHex, NodeX = excluded.NodeX, NodeY = excluded.NodeY, Slug = excluded.Slug";
            cmd.Parameters.AddWithValue("$id", n.Id);
            cmd.Parameters.AddWithValue("$mid", n.MindmapId);
            cmd.Parameters.AddWithValue("$cid", n.CampaignId);
            cmd.Parameters.AddWithValue("$kind", n.Kind);
            cmd.Parameters.AddWithValue("$title", n.Title);
            cmd.Parameters.AddWithValue("$body", n.Body);
            cmd.Parameters.AddWithValue("$color", (object?)n.ColorHex ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$x", n.NodeX);
            cmd.Parameters.AddWithValue("$y", n.NodeY);
            cmd.Parameters.AddWithValue("$slug", (object?)n.Slug ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$created", n.CreatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task UpdateNodePositionAsync(string nodeId, double x, double y, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE MindmapNodes SET NodeX = $x, NodeY = $y WHERE Id = $id";
            cmd.Parameters.AddWithValue("$x", x);
            cmd.Parameters.AddWithValue("$y", y);
            cmd.Parameters.AddWithValue("$id", nodeId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteNodeAsync(string nodeId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM MindmapNodes WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", nodeId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<MindmapNode?> GetNodeBySlugAsync(string campaignId, string slug, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, MindmapId, CampaignId, Kind, Title, Body, ColorHex, NodeX, NodeY, Slug, CreatedAt
                FROM MindmapNodes WHERE CampaignId = $cid AND Slug = $slug LIMIT 1";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$slug", slug);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? ReadNode(r) : null;
        }

        public async Task<List<MindmapLink>> LoadLinksAsync(string mapId, CancellationToken ct = default)
        {
            var result = new List<MindmapLink>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, MindmapId, CampaignId, FromNodeId, ToNodeId, Label, RelationType
                FROM MindmapLinks WHERE MindmapId = $mid";
            cmd.Parameters.AddWithValue("$mid", mapId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                result.Add(new MindmapLink
                {
                    Id = r.GetString(0),
                    MindmapId = r.GetString(1),
                    CampaignId = r.GetString(2),
                    FromNodeId = r.GetString(3),
                    ToNodeId = r.GetString(4),
                    Label = r.IsDBNull(5) ? null : r.GetString(5),
                    RelationType = r.IsDBNull(6) ? "" : r.GetString(6)
                });
            return result;
        }

        public async Task SaveLinkAsync(MindmapLink l, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO MindmapLinks (Id, MindmapId, CampaignId, FromNodeId, ToNodeId, Label, RelationType)
                VALUES ($id, $mid, $cid, $from, $to, $label, $rel)
                ON CONFLICT(Id) DO UPDATE SET Label = excluded.Label, RelationType = excluded.RelationType";
            cmd.Parameters.AddWithValue("$id", l.Id);
            cmd.Parameters.AddWithValue("$mid", l.MindmapId);
            cmd.Parameters.AddWithValue("$cid", l.CampaignId);
            cmd.Parameters.AddWithValue("$from", l.FromNodeId);
            cmd.Parameters.AddWithValue("$to", l.ToNodeId);
            cmd.Parameters.AddWithValue("$label", (object?)l.Label ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$rel", l.RelationType ?? "");
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteLinkAsync(string linkId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM MindmapLinks WHERE Id = $id";
            cmd.Parameters.AddWithValue("$id", linkId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static Mindmap ReadMap(SqliteDataReader r) => new()
        {
            Id = r.GetString(0),
            CampaignId = r.GetString(1),
            OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
            Scope = r.GetString(3),
            Title = r.GetString(4),
            ColorHex = r.IsDBNull(5) ? null : r.GetString(5),
            RevisionNumber = r.GetInt32(6),
            CreatedAt = DateTime.Parse(r.GetString(7)),
            UpdatedAt = DateTime.Parse(r.GetString(8))
        };

        private static MindmapNode ReadNode(SqliteDataReader r) => new()
        {
            Id = r.GetString(0),
            MindmapId = r.GetString(1),
            CampaignId = r.GetString(2),
            Kind = r.GetString(3),
            Title = r.GetString(4),
            Body = r.GetString(5),
            ColorHex = r.IsDBNull(6) ? null : r.GetString(6),
            NodeX = r.GetDouble(7),
            NodeY = r.GetDouble(8),
            Slug = r.IsDBNull(9) ? null : r.GetString(9),
            CreatedAt = DateTime.Parse(r.GetString(10))
        };
    }
}
