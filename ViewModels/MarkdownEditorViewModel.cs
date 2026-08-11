using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dujahit.Models;
using Dujahit.Models.Application;

namespace Dujahit.ViewModels
{
    public class MarkdownEditorViewModel : ViewModelBase
    {
        private string _markdown = "";
        public string Markdown
        {
            get => _markdown;
            set
            {
                this.RaiseAndSetIfChanged(ref _markdown, value ?? "");
                MarkdownChanged?.Invoke(_markdown);
                this.RaisePropertyChanged(nameof(IsDirty));
                this.RaisePropertyChanged(nameof(CountLabel));
            }
        }

        public string CountLabel
        {
            get
            {
                var chars = _markdown.Length;
                var words = 0;
                var inWord = false;
                foreach (var c in _markdown)
                {
                    if (char.IsWhiteSpace(c)) inWord = false;
                    else if (!inWord) { inWord = true; words++; }
                }
                return $"{words} {(words == 1 ? "word" : "words")}  ·  {chars} {(chars == 1 ? "char" : "chars")}";
            }
        }

        private string _baseline = "";
        public string Baseline => _baseline;
        public bool IsDirty => !string.Equals(_markdown, _baseline, StringComparison.Ordinal);

        public void AcceptBaseline()
        {
            _baseline = _markdown;
            this.RaisePropertyChanged(nameof(IsDirty));
        }

        public void AcceptBaseline(string saved)
        {
            _baseline = saved ?? "";
            this.RaisePropertyChanged(nameof(IsDirty));
        }

        public void Reset(string text)
        {
            _baseline = text ?? "";
            Markdown = _baseline;
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                this.RaiseAndSetIfChanged(ref _isEditing, value);
                this.RaisePropertyChanged(nameof(IsViewing));
            }
        }
        public bool IsViewing
        {
            get => !_isEditing;
            set { if (value != IsViewing) IsEditing = !value; }
        }

        private bool _isReadOnly;
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                this.RaiseAndSetIfChanged(ref _isReadOnly, value);
                if (value && IsEditing) IsEditing = false;
            }
        }

        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }

        public void MoveCaretTo(int start)
        {
            SelectionStart = Math.Clamp(start, 0, _markdown.Length);
            SelectionLength = 0;
            SelectionChangeRequested?.Invoke(SelectionStart, 0);
        }

        public event Action<string>? MarkdownChanged;

        public ReactiveCommand<Unit, Unit> BoldCommand { get; }
        public ReactiveCommand<Unit, Unit> ItalicCommand { get; }
        public ReactiveCommand<Unit, Unit> StrikeCommand { get; }
        public ReactiveCommand<Unit, Unit> InlineCodeCommand { get; }
        public ReactiveCommand<string, Unit> HeadingCommand { get; }
        public ReactiveCommand<Unit, Unit> BulletListCommand { get; }
        public ReactiveCommand<Unit, Unit> NumberedListCommand { get; }
        public ReactiveCommand<Unit, Unit> TaskListCommand { get; }
        public ReactiveCommand<Unit, Unit> QuoteCommand { get; }
        public ReactiveCommand<Unit, Unit> CodeBlockCommand { get; }
        public ReactiveCommand<Unit, Unit> HorizontalRuleCommand { get; }
        public ReactiveCommand<Unit, Unit> TableCommand { get; }
        public ReactiveCommand<Unit, Unit> LinkCommand { get; }
        public ReactiveCommand<Unit, Unit> ImageCommand { get; }
        public ReactiveCommand<SearchResultRow, Unit> InsertReferenceCommand { get; }

        // Search hands back plenty of types, these are the ones the ref renderer can resolve and jump to, add a type here plus a case in RefResolver and OnRefNavigate to grow it.
        private static readonly HashSet<string> ReferenceableTypes =
            new(StringComparer.OrdinalIgnoreCase) { "character", "npc", "item", "note", "codex", "mindmap" };  // Chapters were missing here for ages

        public bool IsDm { get; set; } = true;
        public string UserId { get; set; } = "";

        public ObservableCollection<SearchResultRow> RefResults { get; } = new();

        private bool _refPickerOpen;
        public bool RefPickerOpen
        {
            get => _refPickerOpen;
            set
            {
                this.RaiseAndSetIfChanged(ref _refPickerOpen, value);
                if (value)
                {
                    _refQuery = "";
                    this.RaisePropertyChanged(nameof(RefQuery));
                    RefResults.Clear();
                    this.RaisePropertyChanged(nameof(RefShowEmpty));
                }
            }
        }

        private string _refQuery = "";
        public string RefQuery
        {
            get => _refQuery;
            set { this.RaiseAndSetIfChanged(ref _refQuery, value); _ = RunRefSearchAsync(); }
        }

        public bool RefShowEmpty => !string.IsNullOrWhiteSpace(_refQuery) && RefResults.Count == 0;

        private CancellationTokenSource? _refSearchCts;
        private async Task RunRefSearchAsync()
        {
            _refSearchCts?.Cancel();
            _refSearchCts?.Dispose();
            _refSearchCts = new CancellationTokenSource();
            var ct = _refSearchCts.Token;

            var q = _refQuery;
            if (string.IsNullOrWhiteSpace(q))
            {
                RefResults.Clear();
                this.RaisePropertyChanged(nameof(RefShowEmpty));
                return;
            }

            List<SearchHit> hits;
            try { hits = await App.PM.SearchCampaignAsync(q, IsDm, UserId, ct); }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;

            RefResults.Clear();
            foreach (var h in hits)
                if (ReferenceableTypes.Contains(h.Type))
                    RefResults.Add(new SearchResultRow(h));
            this.RaisePropertyChanged(nameof(RefShowEmpty));
        }

        private void InsertReference(SearchResultRow row)
        {
            if (row == null || !IsEditing) return;

            var title = (row.Title ?? "").Replace("'", "").Replace("\"", "");
            var tag = $"<ref type='{row.Type}' id='{row.Id}' text='{title}'/>";
            var start = Math.Clamp(SelectionStart, 0, _markdown.Length);

            InsertAtCursor(tag);

            RefPickerOpen = false;
            SelectionStart = start + tag.Length;
            SelectionLength = 0;
            SelectionChangeRequested?.Invoke(SelectionStart, 0);
        }

        public MarkdownEditorViewModel()
        {
            InsertReferenceCommand = ReactiveCommand.Create<SearchResultRow>(InsertReference);

            BoldCommand = ReactiveCommand.Create(() => Wrap("**", "**", "bold text"));
            ItalicCommand = ReactiveCommand.Create(() => Wrap("*", "*", "italic text"));
            StrikeCommand = ReactiveCommand.Create(() => Wrap("~~", "~~", "strikethrough"));
            InlineCodeCommand = ReactiveCommand.Create(() => Wrap("`", "`", "code"));

            HeadingCommand = ReactiveCommand.Create<string>(LinePrefix);

            BulletListCommand = ReactiveCommand.Create(() => LinePrefix("- "));
            NumberedListCommand = ReactiveCommand.Create(() => LinePrefix("1. "));
            TaskListCommand = ReactiveCommand.Create(() => LinePrefix("- [ ] "));
            QuoteCommand = ReactiveCommand.Create(() => LinePrefix("> "));

            CodeBlockCommand = ReactiveCommand.Create(() => Wrap("\n```\n", "\n```\n", "code block"));
            HorizontalRuleCommand = ReactiveCommand.Create(() => InsertAtCursor("\n\n---\n\n"));
            TableCommand = ReactiveCommand.Create(() => InsertAtCursor("\n\n| Column | Column |\n| --- | --- |\n| Cell | Cell |\n\n"));

            LinkCommand = ReactiveCommand.Create(LinkInsert);
            ImageCommand = ReactiveCommand.Create(() => Wrap("![", "](https://)", "alt text"));
        }

        private static readonly Regex _urlLike = new(@"^\s*(https?://|www\.)\S+\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private void LinkInsert()
        {
            if (!IsEditing) return;

            var s = _markdown;
            var sel = ClampSelection(s);
            var selected = sel.length > 0 ? s.Substring(sel.start, sel.length) : "";

            // Selected a bare url, so it becomes the target and the caret lands in the empty label ready to type.
            if (sel.length > 0 && _urlLike.IsMatch(selected))
            {
                var replacement = "[](" + selected.Trim() + ")";
                Markdown = s.Substring(0, sel.start) + replacement + s.Substring(sel.start + sel.length);
                SelectionStart = sel.start + 1;
                SelectionLength = 0;
                SelectionChangeRequested?.Invoke(SelectionStart, 0);
                return;
            }

            Wrap("[", "](https://)", "link text");
        }

        private void Wrap(string left, string right, string placeholder)
        {
            if (!IsEditing) return;

            var s = _markdown;
            var sel = ClampSelection(s);

            var selected = sel.length > 0
                ? s.Substring(sel.start, sel.length)
                : placeholder;

            var newText = s.Substring(0, sel.start)
                        + left + selected + right
                        + s.Substring(sel.start + sel.length);

            Markdown = newText;

            SelectionStart = sel.start + left.Length;
            SelectionLength = selected.Length;
            SelectionChangeRequested?.Invoke(SelectionStart, SelectionLength);
        }

        private void LinePrefix(string prefix)
        {
            if (!IsEditing) return;

            var s = _markdown;
            var sel = ClampSelection(s);

            var lineStart = s.LastIndexOf('\n', Math.Max(0, sel.start - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;

            // Otherwise the commands stack a second prefix instead of togglin
            var existing = StripLinePrefix(s, lineStart, out var afterStrip);
            var insertion = prefix;

            if (existing == prefix.TrimEnd())
                insertion = "";

            var newText = s.Substring(0, lineStart) + insertion + s.Substring(afterStrip);
            Markdown = newText;
        }

        private void InsertAtCursor(string text)
        {
            if (!IsEditing) return;
            var s = _markdown;
            var sel = ClampSelection(s);
            Markdown = s.Substring(0, sel.start) + text + s.Substring(sel.start + sel.length);
        }

        public event Action<int, int>? SelectionChangeRequested;

        private (int start, int length) ClampSelection(string s)
        {
            var start = Math.Clamp(SelectionStart, 0, s.Length);
            var len = Math.Clamp(SelectionLength, 0, s.Length - start);
            return (start, len);
        }

        private static readonly Regex LinePrefixRegex =
            new(@"^(#{1,6}\s|>\s|-\s|\*\s|\+\s|\d+\.\s)", RegexOptions.Compiled);

        private static string StripLinePrefix(string s, int lineStart, out int afterStrip)
        {
            var nextNewline = s.IndexOf('\n', lineStart);
            var lineEnd = nextNewline < 0 ? s.Length : nextNewline;
            var line = s.Substring(lineStart, lineEnd - lineStart);

            var m = LinePrefixRegex.Match(line);
            if (!m.Success)
            {
                afterStrip = lineStart;
                return "";
            }
            afterStrip = lineStart + m.Length;
            return m.Value.TrimEnd();
        }

        public bool ViewerOwnsPage { get; set; } = true;

        // Only the ref tags need a pass, the renderer does the rest of the markdown natively and there is no inline html for it to print raw
        public static async Task<string> PreRenderAsync(string md, bool viewerOwnsPage = true)
        {
            if (string.IsNullOrEmpty(md)) return "";
            return await RefRewriter.RewriteForRenderAsync(md, viewerOwnsPage);
        }
    }
}