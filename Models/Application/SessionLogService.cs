using Dujahit.Models.Database;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Dujahit.Models.Application
{
    public class SessionLogService
    {
        private readonly CampaignRepository _repo;
        private readonly HashSet<string> _recentKeys = new();
        private readonly Queue<string> _recentOrder = new();
        private readonly object _gate = new();

        public SessionLogService(CampaignRepository repo)
        {
            _repo = repo;
        }

        public async Task LogAsync(string eventType, string actorUserId, string actorName, string summary, string? detailJson = null, string? dedupeKey = null)
        {
            if (dedupeKey != null && !RememberKey(dedupeKey)) return;

            var entry = new SessionLogEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = DateTime.UtcNow,
                EventType = eventType,
                ActorUserId = actorUserId ?? "",
                ActorName = actorName ?? "",
                Summary = summary ?? "",
                DetailJson = detailJson
            };
            await _repo.AppendSessionLogAsync(entry);
        }

        private bool RememberKey(string key)
        {
            lock (_gate)
            {
                if (!_recentKeys.Add(key)) return false;
                _recentOrder.Enqueue(key);
                if (_recentOrder.Count > 512)
                    _recentKeys.Remove(_recentOrder.Dequeue());
                return true;
            }
        }
    }
}
