using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Dujahit.Models;
using ReactiveUI;

namespace Dujahit.ViewModels
{
    public class RandomTablesViewModel : ViewModelBase
    {
        public ObservableCollection<RandomTableRowViewModel> Tables { get; } = new();

        public Action<string, bool>? RollToChat { get; set; }

        private bool _editorOpen;
        public bool EditorOpen { get => _editorOpen; set => this.RaiseAndSetIfChanged(ref _editorOpen, value); }

        private string _editorName = "";
        public string EditorName { get => _editorName; set => this.RaiseAndSetIfChanged(ref _editorName, value); }

        private string _editorDice = "";
        public string EditorDice { get => _editorDice; set => this.RaiseAndSetIfChanged(ref _editorDice, value); }

        private string _editorEntries = "";
        public string EditorEntries { get => _editorEntries; set => this.RaiseAndSetIfChanged(ref _editorEntries, value); }

        private string _editingId = "";

        private string _lastResult = "";
        public string LastResult
        {
            get => _lastResult;
            set { this.RaiseAndSetIfChanged(ref _lastResult, value); this.RaisePropertyChanged(nameof(HasResult)); }
        }
        public bool HasResult => !string.IsNullOrEmpty(LastResult);

        public ReactiveCommand<Unit, Unit> NewTableCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveTableCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelEditCommand { get; }

        public RandomTablesViewModel()
        {
            NewTableCommand = ReactiveCommand.Create(() =>
            {
                _editingId = "";
                EditorName = "";
                EditorDice = "";
                EditorEntries = "";
                EditorOpen = true;
            });
            SaveTableCommand = ReactiveCommand.CreateFromTask(SaveEditorAsync);
            CancelEditCommand = ReactiveCommand.Create(() => { EditorOpen = false; });
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;
            try
            {
                var tables = await App.PM.LoadRandomTablesAsync();
                Tables.Clear();
                foreach (var t in tables)
                    Tables.Add(new RandomTableRowViewModel(t, RollRow, EditRow, DeleteRowAsync));
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[RandomTables] load failed", ex);
            }
        }

        private void RollRow(RandomTableRowViewModel row)
        {
            var rolled = ProgramManager.RollOnTable(row.Table);
            if (rolled == null) return;
            LastResult = row.Table.Name + " [" + rolled.Value.Roll + "]: " + rolled.Value.Text;
            RollToChat?.Invoke("rolled on " + row.Table.Name + ": [" + rolled.Value.Roll + "] " + rolled.Value.Text, false);
        }

        private void EditRow(RandomTableRowViewModel row)
        {
            _editingId = row.Table.Id;
            EditorName = row.Table.Name;
            EditorDice = row.Table.DiceExpression;
            EditorEntries = ProgramManager.FormatTableEntries(row.Table.Entries);
            EditorOpen = true;
        }

        private async Task DeleteRowAsync(RandomTableRowViewModel row)
        {
            if (App.PM == null) return;
            await App.PM.DeleteRandomTableAsync(row.Table.Id);
            Tables.Remove(row);
        }

        private async Task SaveEditorAsync()
        {
            if (App.PM == null) return;
            var name = (EditorName ?? "").Trim();
            var entries = ProgramManager.ParseTableEntries(EditorEntries ?? "");
            if (name.Length == 0 || entries.Count == 0) return;
            var table = new RandomTable
            {
                Id = _editingId,
                Name = name,
                DiceExpression = (EditorDice ?? "").Trim(),
                Entries = entries
            };
            await App.PM.SaveRandomTableAsync(table);
            EditorOpen = false;
            await LoadAsync();
        }
    }

    public class RandomTableRowViewModel : ViewModelBase
    {
        public RandomTable Table { get; }
        public string Name => Table.Name;
        public string Subtitle => Table.Entries.Count == 0
            ? "empty"
            : (string.IsNullOrEmpty(Table.DiceExpression) ? "1d" + Table.Entries.Max(e => e.Max) : Table.DiceExpression) + ", " + Table.Entries.Count + " entries";
        public bool IsCustom => !Table.IsTemplate;

        private bool _confirmingDelete;
        public bool ConfirmingDelete
        {
            get => _confirmingDelete;
            set { this.RaiseAndSetIfChanged(ref _confirmingDelete, value); this.RaisePropertyChanged(nameof(DeleteLabel)); }
        }
        public string DeleteLabel => ConfirmingDelete ? "Sure?" : "Delete";

        public ReactiveCommand<Unit, Unit> RollCommand { get; }
        public ReactiveCommand<Unit, Unit> EditCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }

        public RandomTableRowViewModel(RandomTable table, Action<RandomTableRowViewModel> onRoll, Action<RandomTableRowViewModel> onEdit, Func<RandomTableRowViewModel, Task> onDelete)
        {
            Table = table;
            RollCommand = ReactiveCommand.Create(() => onRoll(this));
            EditCommand = ReactiveCommand.Create(() => onEdit(this));
            DeleteCommand = ReactiveCommand.CreateFromTask(async () =>
            {
                if (!ConfirmingDelete) { ConfirmingDelete = true; return; }
                await onDelete(this);
            });
        }
    }
}
