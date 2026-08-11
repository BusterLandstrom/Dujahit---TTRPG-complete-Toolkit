using System;
using System.Collections.Generic;
using System.Text;

namespace Dujahit.Models.Application
{
    public static class ThreeWayMerge
    {
        public const string MineMarker = "<<<<<<<";
        public const string SplitMarker = "=======";
        public const string TheirsMarker = ">>>>>>>";

        public static bool HasCollision(string? text) =>
            !string.IsNullOrEmpty(text) && text!.Contains(SplitMarker, StringComparison.Ordinal) && text.Contains(MineMarker, StringComparison.Ordinal);

        public static string Merge(string baseText, string mine, string theirs) =>
            Merge(baseText, mine, theirs, null, null, out _);

        public static string Merge(string baseText, string mine, string theirs, out bool interleaved) =>
            Merge(baseText, mine, theirs, null, null, out interleaved);

        public static string Merge(string baseText, string mine, string theirs, string? mineLabel, string? theirsLabel, out bool interleaved)
        {
            interleaved = false;
            baseText ??= "";
            mine ??= "";
            theirs ??= "";
            if (string.Equals(mine, theirs, StringComparison.Ordinal)) return mine;
            if (string.Equals(baseText, mine, StringComparison.Ordinal)) return theirs;
            if (string.Equals(baseText, theirs, StringComparison.Ordinal)) return mine;

            var b = Split(baseText);
            var m = Split(mine);
            var t = Split(theirs);

            // A pathological paste could make the lcs table silly, past this it keeps what is on screen and lets the next edit try again rather than dumping both copies into the page.
            if ((long)b.Length * m.Length > 4_000_000 || (long)b.Length * t.Length > 4_000_000)
            {
                ErrorLog.Log("Note merge skipped, the page is too big to diff so the remote edit was left for the next round");
                return mine;
            }

            var matchM = LcsLines(b, m);
            var matchT = LcsLines(b, t);

            var result = new List<string>();
            int bi = 0, mi = 0, ti = 0;
            while (bi < b.Length || mi < m.Length || ti < t.Length)
            {
                if (bi < b.Length && matchM[bi] == mi && matchT[bi] == ti && mi < m.Length && ti < t.Length)
                {
                    result.Add(b[bi]);
                    bi++; mi++; ti++;
                    continue;
                }

                var bEnd = bi;
                while (bEnd < b.Length && !(matchM[bEnd] >= mi && matchT[bEnd] >= ti)) bEnd++;
                var mEnd = bEnd < b.Length ? matchM[bEnd] : m.Length;
                var tEnd = bEnd < b.Length ? matchT[bEnd] : t.Length;

                var bChunk = Slice(b, bi, bEnd);
                var mChunk = Slice(m, mi, mEnd);
                var tChunk = Slice(t, ti, tEnd);

                if (SameLines(mChunk, bChunk)) result.AddRange(tChunk);
                else if (SameLines(tChunk, bChunk)) result.AddRange(mChunk);
                else if (SameLines(mChunk, tChunk)) result.AddRange(mChunk);
                else
                {
                    interleaved = true;
                    var woven = WeaveText(string.Join("\n", bChunk), string.Join("\n", mChunk), string.Join("\n", tChunk));
                    result.AddRange(woven.Split('\n'));
                }

                bi = bEnd; mi = mEnd; ti = tEnd;
            }
            return string.Join("\n", result);
        }

        public static int TransformCaret(string? before, string? after, int caret)
        {
            before ??= "";
            after ??= "";
            if (caret <= 0) return 0;
            if (caret > before.Length) caret = before.Length;

            var max = Math.Min(before.Length, after.Length);
            var head = 0;
            while (head < max && before[head] == after[head]) head++;
            if (caret <= head) return caret;

            var tail = 0;
            while (tail < max - head && before[before.Length - 1 - tail] == after[after.Length - 1 - tail]) tail++;
            if (caret >= before.Length - tail) return caret + (after.Length - before.Length);

            return after.Length - tail;
        }

        private static string WeaveText(string b, string m, string t)
        {
            if (string.Equals(m, t, StringComparison.Ordinal)) return m;
            if (string.Equals(b, m, StringComparison.Ordinal)) return t;
            if (string.Equals(b, t, StringComparison.Ordinal)) return m;

            var mine = EditSpan(b, m);
            var theirs = EditSpan(b, t);

            var start = Math.Min(mine.Start, theirs.Start);
            var end = Math.Max(mine.End, theirs.End);
            if (end < start) end = start;

            var first = mine.Start <= theirs.Start ? mine : theirs;
            var second = ReferenceEquals(first, mine) ? theirs : mine;

            var sb = new StringBuilder();
            sb.Append(b, 0, start);
            sb.Append(Widen(b, first, start, end));
            sb.Append(Widen(b, second, start, end));
            sb.Append(b, end, b.Length - end);
            return sb.ToString();
        }

        private static string Widen(string b, EditRegion e, int start, int end) =>
            b.Substring(start, e.Start - start) + e.Text + b.Substring(e.End, end - e.End);

        private sealed class EditRegion
        {
            public int Start;
            public int End;
            public string Text = "";
        }

        private static EditRegion EditSpan(string b, string x)
        {
            var max = Math.Min(b.Length, x.Length);
            var head = 0;
            while (head < max && b[head] == x[head]) head++;
            var tail = 0;
            while (tail < max - head && b[b.Length - 1 - tail] == x[x.Length - 1 - tail]) tail++;
            return new EditRegion { Start = head, End = b.Length - tail, Text = x.Substring(head, x.Length - tail - head) };
        }

        private static string[] Split(string s) =>
            s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        private static List<string> Slice(string[] a, int from, int to)
        {
            var list = new List<string>();
            for (var i = from; i < to && i < a.Length; i++) list.Add(a[i]);
            return list;
        }

        private static bool SameLines(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        // -1 means the line did not survive, anything else is where it landed in the other text
        private static int[] LcsLines(string[] b, string[] x)
        {
            var dp = new int[b.Length + 1, x.Length + 1];
            for (var i = b.Length - 1; i >= 0; i--)
                for (var j = x.Length - 1; j >= 0; j--)
                    dp[i, j] = string.Equals(b[i], x[j], StringComparison.Ordinal)
                        ? dp[i + 1, j + 1] + 1
                        : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var match = new int[b.Length];
            for (var i = 0; i < b.Length; i++) match[i] = -1;
            int bi = 0, xi = 0;
            while (bi < b.Length && xi < x.Length)
            {
                if (string.Equals(b[bi], x[xi], StringComparison.Ordinal)) { match[bi] = xi; bi++; xi++; }
                else if (dp[bi + 1, xi] >= dp[bi, xi + 1]) bi++;
                else xi++;
            }
            return match;
        }

    }
}
