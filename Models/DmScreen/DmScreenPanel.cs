using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Dujahit.Models.DmScreen
{
    public class DmScreenPanel : ReactiveObject
    {
        public string Id { get; }

        private string _title = "";
        public string Title { get => _title; set => this.RaiseAndSetIfChanged(ref _title, value); }

        private string _content = "";
        public string Content { get => _content; set => this.RaiseAndSetIfChanged(ref _content, value); }

        private int _sortOrder;
        public int SortOrder { get => _sortOrder; set => this.RaiseAndSetIfChanged(ref _sortOrder, value); }

        public bool IsTemplate { get; }

        private bool _isEditing;
        public bool IsEditing { get => _isEditing; set => this.RaiseAndSetIfChanged(ref _isEditing, value); }

        private bool _isShown = true;
        public bool IsShown { get => _isShown; set => this.RaiseAndSetIfChanged(ref _isShown, value); }

        // Any dice expression in the text becomes a button.
        public ObservableCollection<string> RollChips { get; } = new();
        public bool HasRollChips => RollChips.Count > 0;

        private static readonly Regex _dicePattern = new(@"\b(\d{0,2}d\d{1,3}(?:k[hl]\d)?(?:\s*[+-]\s*\d{1,3})?)\b", RegexOptions.IgnoreCase);

        private string _titleBackup = "";
        private string _contentBackup = "";

        public ReactiveCommand<Unit, Unit> EditCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
        public ReactiveCommand<string, Unit> RollChipCommand { get; }

        public DmScreenPanel(
            string id, string title, string content, int sortOrder, bool isTemplate,
            Func<DmScreenPanel, Task>? onSave = null,
            Func<DmScreenPanel, Task>? onDelete = null,
            Action<DmScreenPanel, string>? onRoll = null)
        {
            Id = id; _title = title; _content = content; _sortOrder = sortOrder; IsTemplate = isTemplate;

            EditCommand = ReactiveCommand.Create(() =>
            {
                _titleBackup = Title; _contentBackup = Content; IsEditing = true;
            });

            CancelCommand = ReactiveCommand.Create(() =>
            {
                Title = _titleBackup; Content = _contentBackup; IsEditing = false;
            });

            SaveCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (onSave != null) await onSave(this);
                IsEditing = false;
                RefreshRollChips();
            });

            DeleteCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (onDelete != null) await onDelete(this);
            });

            RollChipCommand = ReactiveCommand.Create<string>(expr => onRoll?.Invoke(this, expr));

            RefreshRollChips();
        }

        private void RefreshRollChips()
        {
            RollChips.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in _dicePattern.Matches(Content ?? ""))
            {
                var expr = m.Groups[1].Value.Replace(" ", "").ToLowerInvariant();
                if (seen.Add(expr)) RollChips.Add(expr);
            }
            this.RaisePropertyChanged(nameof(HasRollChips));
        }
    }
}
