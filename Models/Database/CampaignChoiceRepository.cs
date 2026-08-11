using Microsoft.Data.Sqlite;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dujahit.Models.Application;

namespace Dujahit.Models.Database
{
    public class CampaignChoiceRepository
    {
        private readonly DatabaseManager _db;
        public CampaignChoiceRepository(DatabaseManager db) => _db = db;

        public async Task<List<ResolvedClassChoice>> GetChoicesForClassAsync(string classId, int level, CancellationToken ct = default)
        {
            var result = new List<ResolvedClassChoice>();
            if (string.IsNullOrEmpty(classId)) return result;

            await using var conn = await _db.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ClassId, Level, Kind, StoreAs, ChooseCount, Label, Description, OptionsJson
                FROM ClassChoices
                WHERE ClassId = $cid AND Level <= $lvl
                ORDER BY Level, Kind
                """;
            cmd.Parameters.AddWithValue("$cid", classId);
            cmd.Parameters.AddWithValue("$lvl", level);

            await using var r = await cmd.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                var optionsJson = r.IsDBNull(8) ? "[]" : r.GetString(8);
                List<ChoiceOption> options;
                try { options = JsonSerializer.Deserialize<List<ChoiceOption>>(optionsJson) ?? new(); }
                catch (JsonException) { options = new(); }

                result.Add(new ResolvedClassChoice
                {
                    Id = r.GetString(0),
                    ClassId = r.GetString(1),
                    Level = r.GetInt32(2),
                    Kind = r.GetString(3),
                    StoreAs = r.GetString(4),
                    ChooseCount = r.GetInt32(5),
                    Label = r.GetString(6),
                    Description = r.IsDBNull(7) ? "" : r.GetString(7),
                    Options = options
                });
            }
            return result;
        }
    }

    public class ResolvedClassChoice
    {
        public string Id { get; set; } = "";
        public string ClassId { get; set; } = "";
        public int Level { get; set; }
        public string Kind { get; set; } = "";
        public string StoreAs { get; set; } = "";
        public int ChooseCount { get; set; } = 1;
        public string Label { get; set; } = "";
        public string Description { get; set; } = "";
        public List<ChoiceOption> Options { get; set; } = new();
    }
}
