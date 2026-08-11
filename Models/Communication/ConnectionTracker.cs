using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Dujahit.Models.Communication
{
    public static class ConnectionTracker
    {
        private static readonly ConcurrentDictionary<string, HashSet<string>> _userConnections = new();
        private static readonly object _lock = new();

        public static void Add(string userId, string connectionId)
        {
            lock (_lock)
            {
                if (!_userConnections.TryGetValue(userId, out var set))
                {
                    set = new HashSet<string>();
                    _userConnections[userId] = set;
                }
                set.Add(connectionId);
            }
        }

        public static void Remove(string userId, string connectionId)
        {
            lock (_lock)
            {
                if (_userConnections.TryGetValue(userId, out var set))
                {
                    set.Remove(connectionId);
                    if (set.Count == 0)
                        _userConnections.TryRemove(userId, out _);
                }
            }
        }

        public static bool RemoveAndIsLast(string userId, string connectionId)
        {
            lock (_lock)
            {
                if (!_userConnections.TryGetValue(userId, out var set)) return false;
                set.Remove(connectionId);
                if (set.Count == 0)
                {
                    _userConnections.TryRemove(userId, out _);
                    return true;
                }
                return false;
            }
        }

        public static IReadOnlyCollection<string> GetConnectionsForUser(string userId)
        {
            lock (_lock)
            {
                return _userConnections.TryGetValue(userId, out var set)
                    ? set.ToArray()
                    : Array.Empty<string>();
            }
        }

        public static IEnumerable<KeyValuePair<string, HashSet<string>>> SnapshotForCleanup()
        {
            lock (_lock)
            {
                return _userConnections.ToArray();
            }
        }

        public static List<string> OnlineUserIds()
        {
            lock (_lock)
            {
                return _userConnections.Keys.ToList();
            }
        }
    }
}