using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using YDotNet.Document;
using YDotNet.Document.Options;
using YDotNet.Document.StickyIndexes;

namespace Dujahit.Models.Application
{
    public sealed class NoteCrdt : IDisposable
    {
        private const string TextKey = "note";

        private readonly Doc _doc;
        private bool _disposed;

        private NoteCrdt(Doc doc)
        {
            _doc = doc;
        }

        /* Two things learned the hard way, both measured:
           A. left to itself yrs hands out ids like 18 and 48 and two fresh documents collide about six times in a hundred, and two replicas sharing an id is the one thing a crdt cannot survive
           B. the id has to stay inside 32 bits, every pair I tried above that diverged, 200 out of 200, so this masks down to the same width Yjs itself uses
        */
        private const ulong SeedClientId = 1;

        private static ulong NewClientId()
        {
            Span<byte> bytes = stackalloc byte[4];
            RandomNumberGenerator.Fill(bytes);
            var id = BitConverter.ToUInt32(bytes) & 0x7FFFFFFF;
            return id <= SeedClientId ? SeedClientId + 1 : id;
        }

        public static NoteCrdt Empty() => new NoteCrdt(new Doc(new DocOptions { Id = NewClientId() }));

        public static NoteCrdt FromState(byte[]? state)
        {
            var crdt = Empty();
            crdt.ApplyUpdate(state);
            return crdt;
        }

        public static NoteCrdt FromState(byte[]? state, IEnumerable<byte[]>? updates)
        {
            var crdt = FromState(state);
            if (updates != null)
            {
                foreach (var update in updates) crdt.ApplyUpdate(update);
            }
            return crdt;
        }

        /* Seeding a page from its markdown happens independently on every replica that has never seen the document, and if each one writes
           those words under its own id they are all different insertions of the same sentence, so merging stacks up a copy per replica.
           Doing the seed under one reserved id makes every replica produce byte for byte the same operations, which merge to one copy.
        */
        public static NoteCrdt FromText(string? markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return Empty();
            using var seed = new NoteCrdt(new Doc(new DocOptions { Id = SeedClientId }));
            seed.ApplyLocalText(markdown);
            return FromState(seed.FullState());
        }

        public ulong ClientId => _disposed ? 0 : _doc.Id;

        public bool HasEverHeldText => !_disposed && StateVector().Length > 1;

        public string Text
        {
            get
            {
                if (_disposed) return "";
                var text = _doc.Text(TextKey);
                var txn = _doc.ReadTransaction();
                var value = text.String(txn);
                txn.Commit();
                return value ?? "";
            }
        }

        public bool ApplyLocalText(string? next)
        {
            if (_disposed) return false;
            next ??= "";
            var current = Text;
            if (string.Equals(current, next, StringComparison.Ordinal)) return false;

            var max = Math.Min(current.Length, next.Length);
            var head = 0;
            while (head < max && current[head] == next[head]) head++;
            var tail = 0;
            while (tail < max - head && current[current.Length - 1 - tail] == next[next.Length - 1 - tail]) tail++;

            var removeLength = current.Length - tail - head;
            var inserted = next.Substring(head, next.Length - tail - head);

            var text = _doc.Text(TextKey);
            var txn = _doc.WriteTransaction();
            if (removeLength > 0) text.RemoveRange(txn, (uint)head, (uint)removeLength);
            if (inserted.Length > 0) text.Insert(txn, (uint)head, inserted);
            txn.Commit();
            return true;
        }

        public byte[] StateVector()
        {
            if (_disposed) return Array.Empty<byte>();
            var txn = _doc.ReadTransaction();
            var sv = txn.StateVectorV1();
            txn.Commit();
            return sv ?? Array.Empty<byte>();
        }

        // The empty state vector is one zero byte, not zero bytes, and handing yrs an empty array throws out of the marshaller instead of saying so.
        private static readonly byte[] _emptyStateVector = { 0 };

        public byte[] DiffAgainst(byte[]? theirStateVector)
        {
            if (_disposed) return Array.Empty<byte>();
            var against = theirStateVector == null || theirStateVector.Length == 0 ? _emptyStateVector : theirStateVector;
            var txn = _doc.ReadTransaction();
            var diff = txn.StateDiffV1(against);
            txn.Commit();
            return diff ?? Array.Empty<byte>();
        }

        public byte[] FullState() => DiffAgainst(_emptyStateVector);

        public bool ApplyUpdate(byte[]? update)
        {
            if (_disposed || update == null || update.Length == 0) return false;
            var before = Text;
            var txn = _doc.WriteTransaction();
            txn.ApplyV1(update);
            txn.Commit();
            return !string.Equals(before, Text, StringComparison.Ordinal);
        }

        /* Diffing the before and after strings cannot tell whose letters are whose, so a caret sitting next to somebody else's
           word gets dragged past it and the next keystroke lands in the wrong place. A sticky index is the document's own answer,
           it marks the spot in the crdt itself and survives whatever the update did. Before association means text arriving at
           exactly the caret goes after it and I stay where I was, which is the behaviour I was chasing.
        */
        public bool ApplyUpdate(byte[]? update, int caret, out int newCaret)
        {
            newCaret = caret;
            if (_disposed || update == null || update.Length == 0) return false;

            var before = Text;
            var at = (uint)Math.Clamp(caret, 0, before.Length);
            var text = _doc.Text(TextKey);

            byte[]? mark = null;
            var marking = _doc.WriteTransaction();
            var sticky = text.StickyIndex(marking, at, StickyAssociationType.Before);
            if (sticky != null)
            {
                mark = sticky.Encode();
                sticky.Dispose();
            }
            marking.Commit();

            var applying = _doc.WriteTransaction();
            applying.ApplyV1(update);
            applying.Commit();

            var after = Text;
            if (mark != null)
            {
                var reading = _doc.WriteTransaction();
                var restored = StickyIndex.Decode(mark);
                if (restored != null)
                {
                    newCaret = (int)restored.Read(reading);
                    restored.Dispose();
                }
                reading.Commit();
            }
            newCaret = Math.Clamp(newCaret, 0, after.Length);
            return !string.Equals(before, after, StringComparison.Ordinal);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _doc.Dispose();
        }
    }
}
