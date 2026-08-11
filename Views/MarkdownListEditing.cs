using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Text.RegularExpressions;

namespace Dujahit.Views
{
    public static class MarkdownListEditing
    {
        private static readonly Regex _orderedItem = new(@"^(?<indent>[ \t]*)(?<num>\d+)(?<delim>[.)])(?<space>[ \t]+)(?<body>.*)$", RegexOptions.Compiled);
        private static readonly Regex _unorderedItem = new(@"^(?<indent>[ \t]*)(?<bullet>[-*+])(?<space>[ \t]+)(?<body>.*)$", RegexOptions.Compiled);
        private static readonly Regex _taskBox = new(@"^\[[ xX]\](?<space>[ \t]+)(?<rest>.*)$", RegexOptions.Compiled);

        private const string IndentUnit = "  ";

        // Tunnel so we grab Enter and Tab before the TextBox turns them into a plain newline or tab.
        public static void Attach(TextBox box)
        {
            box.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }

        private static void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (sender is not TextBox box) return;

            if (e.Key == Key.Tab)
            {
                HandleTab(box, e);
                return;
            }

            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
            if (box.SelectionStart != box.SelectionEnd) return;

            var text = box.Text ?? "";
            var caret = box.CaretIndex;
            var lineStart = caret > 0 ? text.LastIndexOf('\n', caret - 1) + 1 : 0;
            var linePrefix = text.Substring(lineStart, caret - lineStart);

            var m = _orderedItem.Match(linePrefix);
            var ordered = m.Success;
            if (!ordered) m = _unorderedItem.Match(linePrefix);
            if (!m.Success) return;

            var body = m.Groups["body"].Value;
            var task = !ordered ? _taskBox.Match(body) : Match.Empty;
            var isTask = task.Success;
            var content = isTask ? task.Groups["rest"].Value : body;

            // Empty item, so Enter drops the marker and breaks out of the list instead of stacking another one
            if (string.IsNullOrWhiteSpace(content))
            {
                box.SelectionStart = lineStart;
                box.SelectionEnd = caret;
                box.SelectedText = "";
                box.CaretIndex = lineStart;
                e.Handled = true;
                return;
            }

            var indent = m.Groups["indent"].Value;
            var space = m.Groups["space"].Value;
            string nextPrefix;
            if (ordered)
                nextPrefix = indent + (int.Parse(m.Groups["num"].Value) + 1) + m.Groups["delim"].Value + space;
            else if (isTask)
                nextPrefix = indent + m.Groups["bullet"].Value + space + "[ ]" + task.Groups["space"].Value;
            else
                nextPrefix = indent + m.Groups["bullet"].Value + space;

            var insert = "\n" + nextPrefix;
            box.SelectedText = insert;
            box.CaretIndex = caret + insert.Length;
            e.Handled = true;
        }

        private static void HandleTab(TextBox box, KeyEventArgs e)
        {
            if (box.SelectionStart != box.SelectionEnd) return;

            var text = box.Text ?? "";
            var caret = box.CaretIndex;
            var lineStart = caret > 0 ? text.LastIndexOf('\n', caret - 1) + 1 : 0;
            var lineEnd = text.IndexOf('\n', caret);
            if (lineEnd < 0) lineEnd = text.Length;
            var line = text.Substring(lineStart, lineEnd - lineStart);

            if (!_orderedItem.IsMatch(line) && !_unorderedItem.IsMatch(line)) return;

            var outdent = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            if (outdent)
            {
                var remove = 0;
                while (remove < IndentUnit.Length && lineStart + remove < text.Length && text[lineStart + remove] == ' ')
                    remove++;
                if (remove == 0) { e.Handled = true; return; }
                box.SelectionStart = lineStart;
                box.SelectionEnd = lineStart + remove;
                box.SelectedText = "";
                box.CaretIndex = Math.Max(lineStart, caret - remove);
            }
            else
            {
                box.SelectionStart = lineStart;
                box.SelectionEnd = lineStart;
                box.SelectedText = IndentUnit;
                box.CaretIndex = caret + IndentUnit.Length;
            }
            e.Handled = true;
        }
    }
}
