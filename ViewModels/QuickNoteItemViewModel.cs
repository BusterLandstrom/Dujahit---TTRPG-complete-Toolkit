using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class QuickNoteItemViewModel : ViewModelBase
    {
        private readonly NotePageRepository _repo;
        private readonly Func<QuickNoteItemViewModel, Task> _onDelete;
        private readonly Func<NotePage, Task> _onSaved;

        private readonly IDisposable _saveSub;

        public string Id { get; }
        public string Slug { get; }

        private string _text;
        public string Text
        {
            get => _text;
            set => this.RaiseAndSetIfChanged(ref _text, value);
        }

        private int _revision;
        public int Revision
        {
            get => _revision;
            private set => this.RaiseAndSetIfChanged(ref _revision, value);
        }

        private bool _confirmingDelete;
        public bool ConfirmingDelete
        {
            get => _confirmingDelete;
            set => this.RaiseAndSetIfChanged(ref _confirmingDelete, value);
        }

        public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
        public ReactiveCommand<Unit, Unit> ArmDeleteCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelDeleteCommand { get; }

        public QuickNoteItemViewModel(
            NotePage source,
            NotePageRepository repo,
            Func<QuickNoteItemViewModel, Task> onDelete,
            Func<NotePage, Task> onSaved)
        {
            _repo = repo;
            _onDelete = onDelete;
            _onSaved = onSaved;

            Id = source.Id;
            Slug = source.Slug ?? "qn-?";
            _text = source.ContentMarkdown ?? "";
            _revision = source.RevisionNumber;

            _saveSub = this.WhenAnyValue(x => x.Text)
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(400))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SelectMany(t => Observable.FromAsync(() => SaveAsync(t)))
                .Subscribe();

            DeleteCommand = ReactiveCommand.CreateFromTask(async () => await _onDelete(this));
            ArmDeleteCommand = ReactiveCommand.Create(() => { ConfirmingDelete = true; });
            CancelDeleteCommand = ReactiveCommand.Create(() => { ConfirmingDelete = false; });
        }

        public void ApplyRemoteUpdate(NotePage page)
        {
            if (page == null) return;
            if (page.RevisionNumber <= _revision) return;

            _text = page.ContentMarkdown ?? "";
            this.RaisePropertyChanged(nameof(Text));
            Revision = page.RevisionNumber;
        }

        private async Task SaveAsync(string newText)
        {
            try
            {
                var updated = await _repo.UpdateQuickNoteTextAsync(Id, newText);
                if (updated == null) return;
                Revision = updated.RevisionNumber;
                await _onSaved(updated);
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[QuickNote] Save failed for {Slug}", ex);
            }
        }

        public void Dispose() => _saveSub.Dispose();
    }
}