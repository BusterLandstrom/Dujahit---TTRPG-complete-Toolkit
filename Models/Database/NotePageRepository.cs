using Dujahit.Models.Application;
using Dujahit.Models.Communication;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Text.RegularExpressions;


namespace Dujahit.Models.Database
{
    public class NotePageRepository
    {
        private readonly DatabaseManager _db;

        public NotePageRepository(DatabaseManager db) => _db = db;

        public async Task<List<NotePageNode>> GetVisibleTreeAsync(
            string campaignId, string userId, bool isDm, CancellationToken ct = default)
        {
            var rows = await GetVisibleRowsAsync(campaignId, userId, isDm, ct);
            return BuildTree(rows);
        }

        public async Task<List<NotePage>> GetVisibleRowsAsync(
            string campaignId, string userId, bool isDm, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                SELECT Id, CampaignId, OwnerUserId, ParentPageId, Scope,
                        Title, Icon, ContentMarkdown, SortOrder,
                        RevisionNumber, CreatedAt, UpdatedAt
                FROM NotePages
                WHERE CampaignId = $cid
                    AND (
                        ($isDm = 1 AND Scope = 'campaign_story')
                        OR (Scope = 'private' AND OwnerUserId = $uid)
                        OR (Scope = 'shared'  AND (
                            OwnerUserId = $uid
                            OR Id IN (SELECT PageId FROM NotePageShares WHERE UserId = $uid)
                        ))
                        )
                ORDER BY Scope, SortOrder, CreatedAt;
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$isDm", isDm ? 1 : 0);

            var list = new List<NotePage>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add(ReadRow(r));
            return list;
        }

        public async Task<NotePage?> GetByIdAsync(string pageId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, OwnerUserId, ParentPageId, Scope,
                       Title, Icon, ContentMarkdown, SortOrder,
                       RevisionNumber, CreatedAt, UpdatedAt
                FROM NotePages
                WHERE Id = $id;
            """;
            cmd.Parameters.AddWithValue("$id", pageId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            return await r.ReadAsync(ct) ? ReadRow(r) : null;
        }

        public async Task<bool> CanUserSeePageAsync(
            string pageId, string userId, bool isDm, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1
                FROM NotePages p
                WHERE p.Id = $pid
                  AND (
                        ($isDm = 1 AND p.Scope = 'campaign_story')
                     OR (p.Scope = 'private' AND p.OwnerUserId = $uid)
                     OR (p.Scope = 'shared'  AND (
                            p.OwnerUserId = $uid
                         OR EXISTS (SELECT 1 FROM NotePageShares s
                                    WHERE s.PageId = p.Id AND s.UserId = $uid)
                        ))
                      )
                LIMIT 1;
            """;
            cmd.Parameters.AddWithValue("$pid", pageId);
            cmd.Parameters.AddWithValue("$uid", userId);
            cmd.Parameters.AddWithValue("$isDm", isDm ? 1 : 0);
            return (await cmd.ExecuteScalarAsync(ct)) != null;
        }

        public async Task<NotePage> CreateAsync(
            string campaignId,
            string? ownerUserId,
            string scope,
            string title,
            string? parentPageId = null,
            string? icon = null,
            CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var page = new NotePage
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = campaignId,
                OwnerUserId = ownerUserId,
                ParentPageId = parentPageId,
                Scope = scope,
                Title = string.IsNullOrWhiteSpace(title) ? "Untitled" : title,
                Icon = icon,
                ContentMarkdown = "",
                SortOrder = await GetNextSortOrderAsync(campaignId, parentPageId, scope, ct),
                RevisionNumber = 1,
                CreatedAt = now,
                UpdatedAt = now
            };

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NotePages
                    (Id, CampaignId, OwnerUserId, ParentPageId, Scope,
                     Title, Icon, ContentMarkdown, SortOrder,
                     RevisionNumber, CreatedAt, UpdatedAt)
                VALUES
                    ($id, $cid, $owner, $parent, $scope,
                     $title, $icon, $content, $sort,
                     $rev, $created, $updated);
            """;
            BindAll(cmd, page);
            await cmd.ExecuteNonQueryAsync(ct);
            return page;
        }

        public async Task<int> UpdateContentAsync(
            string pageId,
            string? newTitle,
            string? newIcon,
            string? newMarkdown,
            CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NotePages
                SET Title           = COALESCE($title, Title),
                    Icon            = COALESCE($icon, Icon),
                    ContentMarkdown = COALESCE($content, ContentMarkdown),
                    RevisionNumber  = RevisionNumber + 1,
                    UpdatedAt       = $updated
                WHERE Id = $id
                RETURNING RevisionNumber;
            """;
            cmd.Parameters.AddWithValue("$id", pageId);
            cmd.Parameters.AddWithValue("$title", (object?)newTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$icon", (object?)newIcon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$content", (object?)newMarkdown ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));

            var result = await cmd.ExecuteScalarAsync(ct);
            return result is long l ? (int)l : 0;
        }

        public const int CompactAfterUpdates = 64;
        public const int CompactKeepTail = 16;

        /* Appending a keystroke run costs the size of the run, rewriting the whole document costs the size of the page, and on a long
           session note the second one is thousands of times the first for the same typing. The snapshot in CrdtState is still the base,
           these rows are only what has happened since, and CompactCrdtAsync folds them back down once there are enough to bother.
        */
        public async Task AppendCrdtUpdateAsync(string pageId, byte[]? update, string? markdown, bool clearedOnPurpose = false, CancellationToken ct = default)
        {
            if (update == null || update.Length == 0) return;

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NotePageUpdates (PageId, Payload, CreatedAt) VALUES ($id, $payload, $updated);
                UPDATE NotePages
                SET ContentMarkdown = CASE
                        WHEN $content IS NULL THEN ContentMarkdown
                        WHEN $cleared = 1 OR $content <> '' OR ContentMarkdown = '' THEN $content
                        ELSE ContentMarkdown END,
                    UpdatedAt = $updated
                WHERE Id = $id;
            """;
            cmd.Parameters.AddWithValue("$id", pageId);
            cmd.Parameters.AddWithValue("$payload", update);
            cmd.Parameters.AddWithValue("$content", (object?)markdown ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$cleared", clearedOnPurpose ? 1 : 0);
            cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // A delta only makes sense on top of the words it was written against, so a page that has never been turned into a document gets that done before the first one is filed
        public async Task EnsureCrdtSeedAsync(string pageId, CancellationToken ct = default)
        {
            var snapshot = await LoadCrdtStateAsync(pageId, ct);
            if (snapshot != null && snapshot.Length > 0) return;
            if (await CountCrdtUpdatesAsync(pageId, ct) > 0) return;

            var page = await GetByIdAsync(pageId, ct);
            if (page == null || string.IsNullOrEmpty(page.ContentMarkdown)) return;

            using var doc = NoteCrdt.FromText(page.ContentMarkdown);
            await SaveCrdtStateAsync(pageId, doc.FullState(), doc.Text, false, ct);
        }

        public async Task CompactIfNeededAsync(string pageId, CancellationToken ct = default)
        {
            if (await CountCrdtUpdatesAsync(pageId, ct) < CompactAfterUpdates) return;
            var snapshot = await LoadCrdtStateAsync(pageId, ct);
            var updates = await LoadCrdtUpdatesAsync(pageId, ct);
            using var doc = NoteCrdt.FromState(snapshot, updates);
            await CompactCrdtAsync(pageId, doc.FullState(), doc.Text, doc.HasEverHeldText, ct);
        }

        public async Task<List<byte[]>> LoadCrdtUpdatesAsync(string pageId, CancellationToken ct = default)
        {
            var updates = new List<byte[]>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Payload FROM NotePageUpdates WHERE PageId = $id ORDER BY Seq";
            cmd.Parameters.AddWithValue("$id", pageId);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader[0] is byte[] blob && blob.Length > 0) updates.Add(blob);
            }
            return updates;
        }

        public async Task<int> CountCrdtUpdatesAsync(string pageId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM NotePageUpdates WHERE PageId = $id";
            cmd.Parameters.AddWithValue("$id", pageId);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is long l ? (int)l : 0;
        }

        /* The snapshot has to be on disk before any row it covers is deleted, so this runs as one transaction and the tail it leaves
           behind is slack for anybody mid catch up. A caller holding a document it could not fully load must not come through here,
           writing that as the snapshot is how a partial read becomes the permanent copy.
        */
        public async Task CompactCrdtAsync(string pageId, byte[]? state, string markdown, bool clearedOnPurpose = false, CancellationToken ct = default)
        {
            if (state == null || state.Length == 0) return;

            await using var conn = await _db.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            await using (var save = conn.CreateCommand())
            {
                save.Transaction = tx;
                save.CommandText = """
                    UPDATE NotePages
                    SET CrdtState       = $state,
                        ContentMarkdown = $content,
                        UpdatedAt       = $updated
                    WHERE Id = $id
                      AND ($cleared = 1 OR $content <> '' OR ContentMarkdown = '' OR CrdtState IS NULL);
                """;
                save.Parameters.AddWithValue("$id", pageId);
                save.Parameters.AddWithValue("$state", state);
                save.Parameters.AddWithValue("$content", markdown ?? "");
                save.Parameters.AddWithValue("$cleared", clearedOnPurpose ? 1 : 0);
                save.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
                if (await save.ExecuteNonQueryAsync(ct) == 0)
                {
                    await tx.RollbackAsync(ct);
                    return;
                }
            }

            await using (var prune = conn.CreateCommand())
            {
                prune.Transaction = tx;
                prune.CommandText = """
                    DELETE FROM NotePageUpdates
                    WHERE PageId = $id
                      AND Seq <= (SELECT MAX(Seq) FROM NotePageUpdates WHERE PageId = $id) - $keep;
                """;
                prune.Parameters.AddWithValue("$id", pageId);
                prune.Parameters.AddWithValue("$keep", CompactKeepTail);
                await prune.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
        }

        public async Task<byte[]?> LoadCrdtStateAsync(string pageId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT CrdtState FROM NotePages WHERE Id = $id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", pageId);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result as byte[];
        }

        /* Two guards:
           A. an empty document must never land on top of a good one, that is how a page blanks itself for everybody at once
           B. the markdown column is written alongside so the compendium, the search and the exporters keep working off plain text
        */
        public async Task SaveCrdtStateAsync(string pageId, byte[]? state, string markdown, bool clearedOnPurpose = false, CancellationToken ct = default)
        {
            if (state == null || state.Length == 0) return;

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NotePages
                SET CrdtState       = $state,
                    ContentMarkdown = $content,
                    UpdatedAt       = $updated
                WHERE Id = $id
                  AND ($cleared = 1 OR $content <> '' OR ContentMarkdown = '' OR CrdtState IS NULL);
            """;
            cmd.Parameters.AddWithValue("$id", pageId);
            cmd.Parameters.AddWithValue("$state", state);
            cmd.Parameters.AddWithValue("$content", markdown ?? "");
            cmd.Parameters.AddWithValue("$cleared", clearedOnPurpose ? 1 : 0);
            cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task ReparentAsync(
            string pageId,
            string? newParentId,
            CancellationToken ct = default)
        {
            var page = await GetByIdAsync(pageId, ct)
                       ?? throw new InvalidOperationException($"Page {pageId} not found.");

            if (!string.IsNullOrEmpty(newParentId))
                await GuardNoCycleAsync(pageId, newParentId, ct);

            var newSort = await GetNextSortOrderAsync(page.CampaignId, newParentId, page.Scope, ct);

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NotePages
                SET ParentPageId   = $parent,
                    SortOrder      = $sort,
                    RevisionNumber = RevisionNumber + 1,
                    UpdatedAt      = $updated
                WHERE Id = $id;
            """;
            cmd.Parameters.AddWithValue("$id", pageId);
            cmd.Parameters.AddWithValue("$parent", (object?)newParentId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$sort", newSort);
            cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeleteAsync(string pageId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM NotePages WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", pageId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> CountDescendantsAsync(string pageId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();

            cmd.CommandText = """
                WITH RECURSIVE descendants(Id) AS (
                    SELECT Id FROM NotePages WHERE ParentPageId = $pid
                    UNION ALL
                    SELECT p.Id FROM NotePages p
                    INNER JOIN descendants d ON p.ParentPageId = d.Id
                )
                SELECT COUNT(*) FROM descendants;
            """;
            cmd.Parameters.AddWithValue("$pid", pageId);

            var v = await cmd.ExecuteScalarAsync(ct);
            return v is long l ? (int)l : 0;
        }

        public async Task ShareAsync(
            string pageId,
            string targetUserId,
            string permission = NotePagePermission.Edit,
            CancellationToken ct = default)
        {
            var page = await GetByIdAsync(pageId, ct)
                       ?? throw new InvalidOperationException($"Page {pageId} not found.");
            if (page.Scope != NotePageScope.Shared)
                throw new InvalidOperationException(
                    $"Cannot invite to a '{page.Scope}' page. Promote it to 'shared' first.");

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NotePageShares (PageId, UserId, Permission, SharedAt)
                VALUES ($pid, $uid, $perm, $when)
                ON CONFLICT(PageId, UserId) DO UPDATE
                    SET Permission = excluded.Permission;
            """;
            cmd.Parameters.AddWithValue("$pid", pageId);
            cmd.Parameters.AddWithValue("$uid", targetUserId);
            cmd.Parameters.AddWithValue("$perm", permission);
            cmd.Parameters.AddWithValue("$when", DateTime.UtcNow.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task UnshareAsync(string pageId, string targetUserId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM NotePageShares WHERE PageId = $pid AND UserId = $uid;";
            cmd.Parameters.AddWithValue("$pid", pageId);
            cmd.Parameters.AddWithValue("$uid", targetUserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task UpsertRemoteAsync(NotePage page, CancellationToken ct = default)
        {
            if (page == null || string.IsNullOrEmpty(page.Id)) return;

            await using var conn = await _db.OpenAsync(ct);

            if (!string.IsNullOrEmpty(page.OwnerUserId))
            {
                await using var ou = conn.CreateCommand();
                ou.CommandText = "INSERT OR IGNORE INTO Users (Id, Username, CreatedAt) VALUES ($id, $name, $now);";
                ou.Parameters.AddWithValue("$id", page.OwnerUserId);
                ou.Parameters.AddWithValue("$name", page.OwnerUserId);
                ou.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
                await ou.ExecuteNonQueryAsync(ct);
            }

            await using var up = conn.CreateCommand();
            up.CommandText = """
                INSERT INTO NotePages
                    (Id, CampaignId, OwnerUserId, ParentPageId, Scope,
                     Title, Icon, ContentMarkdown, SortOrder,
                     RevisionNumber, CreatedAt, UpdatedAt)
                VALUES
                    ($id, $cid, $owner, $parent, $scope,
                     $title, $icon, $content, $sort,
                     $rev, $created, $updated)
                ON CONFLICT(Id) DO UPDATE SET
                    Title           = excluded.Title,
                    Icon            = excluded.Icon,
                    ContentMarkdown = excluded.ContentMarkdown,
                    Scope           = excluded.Scope,
                    RevisionNumber  = excluded.RevisionNumber,
                    UpdatedAt       = excluded.UpdatedAt
                WHERE excluded.RevisionNumber >= NotePages.RevisionNumber;
                """;
            BindAll(up, page);
            await up.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> SetScopeAsync(string pageId, string scope, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE NotePages
                SET Scope          = $scope,
                    RevisionNumber = RevisionNumber + 1,
                    UpdatedAt      = $updated
                WHERE Id = $id
                RETURNING RevisionNumber;
            """;
            cmd.Parameters.AddWithValue("$id", pageId);
            cmd.Parameters.AddWithValue("$scope", scope);
            cmd.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("o"));
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is long l ? (int)l : 0;
        }

        public async Task SaveInvitedPageAsync(NotePage page, string viewerUserId, CancellationToken ct = default)
        {
            if (page == null || string.IsNullOrEmpty(page.Id)) return;

            await UpsertRemoteAsync(page, ct);

            await using var conn = await _db.OpenAsync(ct);
            await using (var sh = conn.CreateCommand())
            {
                sh.CommandText = """
                    INSERT INTO NotePageShares (PageId, UserId, Permission, SharedAt)
                    VALUES ($pid, $uid, $perm, $when)
                    ON CONFLICT(PageId, UserId) DO UPDATE
                        SET Permission = excluded.Permission;
                    """;
                sh.Parameters.AddWithValue("$pid", page.Id);
                sh.Parameters.AddWithValue("$uid", viewerUserId);
                sh.Parameters.AddWithValue("$perm", NotePagePermission.Edit);
                sh.Parameters.AddWithValue("$when", DateTime.UtcNow.ToString("o"));
                await sh.ExecuteNonQueryAsync(ct);
            }
        }

        public async Task<List<string>> ListPageIdsSharedWithAsync(string userId, CancellationToken ct = default)
        {
            var ids = new List<string>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT PageId FROM NotePageShares WHERE UserId = $uid;";
            cmd.Parameters.AddWithValue("$uid", userId);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) ids.Add(r.GetString(0));
            return ids;
        }

        public async Task<List<NotePageShare>> ListSharesAsync(string pageId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT PageId, UserId, Permission, SharedAt
                FROM NotePageShares
                WHERE PageId = $pid;
            """;
            cmd.Parameters.AddWithValue("$pid", pageId);

            var list = new List<NotePageShare>();
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                list.Add(new NotePageShare
                {
                    PageId = r.GetString(0),
                    UserId = r.GetString(1),
                    Permission = r.GetString(2),
                    SharedAt = DateTime.Parse(r.GetString(3))
                });
            }
            return list;
        }

        public async Task<HashSet<string>> ResolveAudienceAsync(
            string pageId, CancellationToken ct = default)
        {
            var audience = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var page = await GetByIdAsync(pageId, ct);
            if (page == null) return audience;

            if (!string.IsNullOrEmpty(page.OwnerUserId))
                audience.Add(page.OwnerUserId);

            switch (page.Scope)
            {
                case NotePageScope.Private:
                    break;

                case NotePageScope.Shared:
                    foreach (var s in await ListSharesAsync(pageId, ct))
                        audience.Add(s.UserId);
                    break;

                case NotePageScope.CampaignStory:
                    await using (var conn = await _db.OpenAsync(ct))
                    await using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = """
                            SELECT UserId FROM CampaignMembers
                            WHERE CampaignId = $cid AND Role = 'dm';
                        """;
                        cmd.Parameters.AddWithValue("$cid", page.CampaignId);
                        await using var r = await cmd.ExecuteReaderAsync(ct);
                        while (await r.ReadAsync(ct))
                            audience.Add(r.GetString(0));
                    }
                    break;
            }
            return audience;
        }

        public async Task RecordChangeAsync(
            string campaignId,
            string pageId,
            string changeType,
            int revisionNumber,
            object? payload,
            CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ChangeLog
                    (CampaignId, EntityType, EntityId, ChangeType,
                     RevisionNumber, Timestamp, Payload)
                VALUES
                    ($cid, 'NotePage', $eid, $type, $rev, $ts, $payload);
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$eid", pageId);
            cmd.Parameters.AddWithValue("$type", changeType);
            cmd.Parameters.AddWithValue("$rev", revisionNumber);
            cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("$payload",
                payload == null ? DBNull.Value : JsonSerializer.Serialize(payload));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static NotePage ReadRow(SqliteDataReader r) => new()
        {
            Id = r.GetString(0),
            CampaignId = r.GetString(1),
            OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
            ParentPageId = r.IsDBNull(3) ? null : r.GetString(3),
            Scope = r.GetString(4),
            Title = r.GetString(5),
            Icon = r.IsDBNull(6) ? null : r.GetString(6),
            ContentMarkdown = r.GetString(7),
            SortOrder = r.GetInt32(8),
            RevisionNumber = r.GetInt32(9),
            CreatedAt = DateTime.Parse(r.GetString(10)),
            UpdatedAt = DateTime.Parse(r.GetString(11)),
        };

        private static void BindAll(SqliteCommand cmd, NotePage p)
        {
            cmd.Parameters.AddWithValue("$id", p.Id);
            cmd.Parameters.AddWithValue("$cid", p.CampaignId);
            cmd.Parameters.AddWithValue("$owner", (object?)p.OwnerUserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$parent", (object?)p.ParentPageId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$scope", p.Scope);
            cmd.Parameters.AddWithValue("$title", p.Title);
            cmd.Parameters.AddWithValue("$icon", (object?)p.Icon ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$content", p.ContentMarkdown);
            cmd.Parameters.AddWithValue("$sort", p.SortOrder);
            cmd.Parameters.AddWithValue("$rev", p.RevisionNumber);
            cmd.Parameters.AddWithValue("$created", p.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$updated", p.UpdatedAt.ToString("o"));
        }

        private async Task<int> GetNextSortOrderAsync(
            string campaignId, string? parentId, string scope, CancellationToken ct)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT COALESCE(MAX(SortOrder), -1) + 1
                FROM NotePages
                WHERE CampaignId = $cid
                  AND Scope = $scope
                  AND ((ParentPageId IS NULL AND $parent IS NULL)
                    OR ParentPageId = $parent);
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$scope", scope);
            cmd.Parameters.AddWithValue("$parent", (object?)parentId ?? DBNull.Value);
            var v = await cmd.ExecuteScalarAsync(ct);
            return v is long l ? (int)l : 0;
        }

        private async Task GuardNoCycleAsync(
            string movingId, string candidateParentId, CancellationToken ct)
        {
            var cur = candidateParentId;
            var safety = 0;
            while (!string.IsNullOrEmpty(cur))
            {
                if (string.Equals(cur, movingId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Reparent would create a cycle.");

                if (++safety > 1000)
                    throw new InvalidOperationException("Page hierarchy depth limit exceeded.");

                var p = await GetByIdAsync(cur, ct);
                cur = p?.ParentPageId;
            }
        }

        public async Task<NotePage> CreateQuickNoteAsync(
    string campaignId, string ownerUserId, string text, CancellationToken ct = default)
        {
            var slug = await GetNextQuickNoteSlugAsync(campaignId, ct);
            var now = DateTime.UtcNow;

            var page = new NotePage
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = campaignId,
                OwnerUserId = ownerUserId,
                ParentPageId = null,
                Scope = "quicknote",
                Title = slug,
                Slug = slug,
                Icon = null,
                ContentMarkdown = text ?? "",
                SortOrder = 0,
                RevisionNumber = 1,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO NotePages
                    (Id, CampaignId, OwnerUserId, ParentPageId, Scope,
                     Title, Slug, Icon, ContentMarkdown, SortOrder,
                     RevisionNumber, CreatedAt, UpdatedAt)
                VALUES
                    ($Id, $CampaignId, $OwnerUserId, NULL, 'quicknote',
                     $Title, $Slug, NULL, $Content, 0,
                     1, $Created, $Updated)
            """;
            cmd.Parameters.AddWithValue("$Id", page.Id);
            cmd.Parameters.AddWithValue("$CampaignId", page.CampaignId);
            cmd.Parameters.AddWithValue("$OwnerUserId", page.OwnerUserId!);
            cmd.Parameters.AddWithValue("$Title", page.Title);
            cmd.Parameters.AddWithValue("$Slug", page.Slug);
            cmd.Parameters.AddWithValue("$Content", page.ContentMarkdown);
            cmd.Parameters.AddWithValue("$Created", page.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$Updated", page.UpdatedAt.ToString("o"));
            await cmd.ExecuteNonQueryAsync(ct);

            return page;
        }

        public async Task<List<NotePage>> ListQuickNotesAsync(
            string campaignId, string ownerUserId, CancellationToken ct = default)
        {
            var result = new List<NotePage>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, OwnerUserId, ParentPageId, Scope, Title,
                       Slug, Icon, ContentMarkdown, SortOrder,
                       RevisionNumber, CreatedAt, UpdatedAt
                FROM NotePages
                WHERE CampaignId = $cid
                  AND Scope = 'quicknote'
                  AND OwnerUserId = $uid
                ORDER BY CreatedAt ASC
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", ownerUserId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                result.Add(new NotePage
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                    ParentPageId = r.IsDBNull(3) ? null : r.GetString(3),
                    Scope = r.GetString(4),
                    Title = r.GetString(5),
                    Slug = r.IsDBNull(6) ? null : r.GetString(6),
                    Icon = r.IsDBNull(7) ? null : r.GetString(7),
                    ContentMarkdown = r.GetString(8),
                    SortOrder = r.GetInt32(9),
                    RevisionNumber = r.GetInt32(10),
                    CreatedAt = DateTime.Parse(r.GetString(11)),
                    UpdatedAt = DateTime.Parse(r.GetString(12)),
                });
            }
            return result;
        }
        public async Task<NotePage?> UpdateQuickNoteTextAsync(
    string pageId, string newText, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            await using var conn = await _db.OpenAsync(ct);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    UPDATE NotePages
                    SET ContentMarkdown = $Text,
                        RevisionNumber  = RevisionNumber + 1,
                        UpdatedAt       = $Now
                    WHERE Id = $Id AND Scope = 'quicknote'
                """;
                cmd.Parameters.AddWithValue("$Text", newText ?? "");
                cmd.Parameters.AddWithValue("$Now", now.ToString("o"));
                cmd.Parameters.AddWithValue("$Id", pageId);
                var n = await cmd.ExecuteNonQueryAsync(ct);
                if (n == 0) return null;
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT Id, CampaignId, OwnerUserId, ParentPageId, Scope, Title,
                           Slug, Icon, ContentMarkdown, SortOrder,
                           RevisionNumber, CreatedAt, UpdatedAt
                    FROM NotePages WHERE Id = $Id
                """;
                cmd.Parameters.AddWithValue("$Id", pageId);
                await using var r = await cmd.ExecuteReaderAsync(ct);
                if (!await r.ReadAsync(ct)) return null;
                return new NotePage
                {
                    Id = r.GetString(0),
                    CampaignId = r.GetString(1),
                    OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                    ParentPageId = r.IsDBNull(3) ? null : r.GetString(3),
                    Scope = r.GetString(4),
                    Title = r.GetString(5),
                    Slug = r.IsDBNull(6) ? null : r.GetString(6),
                    Icon = r.IsDBNull(7) ? null : r.GetString(7),
                    ContentMarkdown = r.GetString(8),
                    SortOrder = r.GetInt32(9),
                    RevisionNumber = r.GetInt32(10),
                    CreatedAt = DateTime.Parse(r.GetString(11)),
                    UpdatedAt = DateTime.Parse(r.GetString(12)),
                };
            }
        }

        public async Task<NotePage?> GetQuickNoteBySlugAsync(
            string campaignId, string slug, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, CampaignId, OwnerUserId, ParentPageId, Scope, Title,
                       Slug, Icon, ContentMarkdown, SortOrder,
                       RevisionNumber, CreatedAt, UpdatedAt
                FROM NotePages
                WHERE CampaignId = $cid AND Scope = 'quicknote' AND Slug = $slug
                LIMIT 1
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$slug", slug);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) return null;
            return new NotePage
            {
                Id = r.GetString(0),
                CampaignId = r.GetString(1),
                OwnerUserId = r.IsDBNull(2) ? null : r.GetString(2),
                ParentPageId = r.IsDBNull(3) ? null : r.GetString(3),
                Scope = r.GetString(4),
                Title = r.GetString(5),
                Slug = r.IsDBNull(6) ? null : r.GetString(6),
                Icon = r.IsDBNull(7) ? null : r.GetString(7),
                ContentMarkdown = r.GetString(8),
                SortOrder = r.GetInt32(9),
                RevisionNumber = r.GetInt32(10),
                CreatedAt = DateTime.Parse(r.GetString(11)),
                UpdatedAt = DateTime.Parse(r.GetString(12)),
            };
        }

        public async Task DeleteQuickNoteAsync(string pageId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM NotePages WHERE Id = $Id AND Scope = 'quicknote'";
            cmd.Parameters.AddWithValue("$Id", pageId);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<string> GetNextQuickNoteSlugAsync(string campaignId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Slug FROM NotePages
                WHERE CampaignId = $cid AND Scope = 'quicknote' AND Slug LIKE 'qn-%'
            """;
            cmd.Parameters.AddWithValue("$cid", campaignId);

            var max = 0;
            var rx = new Regex(@"^qn-(\d+)$", RegexOptions.Compiled);
            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                if (r.IsDBNull(0)) continue;
                var m = rx.Match(r.GetString(0));
                if (!m.Success) continue;
                if (int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    if (n > max) max = n;
            }
            return $"qn-{max + 1}";
        }


        private static List<NotePageNode> BuildTree(List<NotePage> rows)
        {
            var nodesById = new Dictionary<string, NotePageNode>(rows.Count);
            foreach (var row in rows)
                nodesById[row.Id] = new NotePageNode { Page = row };

            var roots = new List<NotePageNode>();
            foreach (var node in nodesById.Values)
            {
                var parentId = node.Page.ParentPageId;
                if (!string.IsNullOrEmpty(parentId)
                    && nodesById.TryGetValue(parentId, out var parent))
                {
                    parent.Children.Add(node);
                }
                else
                {
                    roots.Add(node);
                }
            }

            int Compare(NotePageNode a, NotePageNode b) =>
                a.Page.SortOrder != b.Page.SortOrder
                    ? a.Page.SortOrder.CompareTo(b.Page.SortOrder)
                    : a.Page.CreatedAt.CompareTo(b.Page.CreatedAt);

            roots.Sort(Compare);
            foreach (var n in nodesById.Values)
                n.Children.Sort(Compare);
            return roots;
        }
    }
}