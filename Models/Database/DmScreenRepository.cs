using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.Models.Database
{
    public class DmScreenRepository
    {
        private readonly DatabaseManager _db;

        public DmScreenRepository(DatabaseManager db) => _db = db;

        public async Task<List<(string Id, string Title, string Content, int SortOrder)>>
            GetPanelsAsync(string campaignId, CancellationToken ct = default)
        {
            var list = new List<(string, string, string, int)>();
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT Id, Title, Content, SortOrder
                FROM DmScreenPanels
                WHERE CampaignId = $cid
                ORDER BY SortOrder, Title;";
            cmd.Parameters.AddWithValue("$cid", campaignId);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
                list.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3)));
            return list;
        }

        public async Task UpsertPanelAsync(
            string id, string campaignId, string? userId,
            string title, string content, int sortOrder, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow.ToString("o");
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO DmScreenPanels
                    (Id, CampaignId, UserId, Title, Content, SortOrder, CreatedAt, UpdatedAt)
                VALUES ($id, $cid, $uid, $title, $content, $sort, $now, $now)
                ON CONFLICT(Id) DO UPDATE SET
                    Title     = excluded.Title,
                    Content   = excluded.Content,
                    SortOrder = excluded.SortOrder,
                    UpdatedAt = excluded.UpdatedAt;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$cid", campaignId);
            cmd.Parameters.AddWithValue("$uid", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$title", title);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$sort", sortOrder);
            cmd.Parameters.AddWithValue("$now", now);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task DeletePanelAsync(string id, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM DmScreenPanels WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        public async Task<string?> GetTemplateJsonForCampaignAsync(
            string campaignId, CancellationToken ct = default)
        {
            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT t.JsonContent
                FROM Campaigns c
                JOIN CampaignTemplates t ON t.TemplateId = c.TemplateId
                WHERE c.Id = $cid;";
            cmd.Parameters.AddWithValue("$cid", campaignId);
            return await cmd.ExecuteScalarAsync(ct) as string;
        }
    }
}