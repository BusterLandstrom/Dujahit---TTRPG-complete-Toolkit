using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class QuickNotesPanelViewModel : ViewModelBase
    {
        private readonly NotePageRepository _repo;
        private readonly string _campaignId;
        private readonly string _userId;

        public ObservableCollection<QuickNoteItemViewModel> Notes { get; } = new();

        private string _draft = "";
        public string Draft
        {
            get => _draft;
            set => this.RaiseAndSetIfChanged(ref _draft, value);
        }

        public ReactiveCommand<Unit, Unit> AddCommand { get; }

        public QuickNotesPanelViewModel(
            NotePageRepository repo,
            string campaignId,
            string userId)
        {
            _repo = repo;
            _campaignId = campaignId;
            _userId = userId;

            AddCommand = ReactiveCommand.CreateFromTask(AddAsync);
        }

        public async Task LoadAsync()
        {
            try
            {
                var pages = await _repo.ListQuickNotesAsync(_campaignId, _userId);
                Notes.Clear();
                foreach (var page in pages)
                    Notes.Add(BuildItem(page));
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[QuickNotes] Load failed", ex);
            }
        }

        private async Task AddAsync()
        {
            var text = (Draft ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                var page = await _repo.CreateQuickNoteAsync(_campaignId, _userId, text);
                Notes.Add(BuildItem(page));
                Draft = "";
                await BroadcastAsync(page, "added");
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[QuickNotes] Add failed", ex);
            }
        }

        private async Task DeleteItemAsync(QuickNoteItemViewModel item)
        {
            try
            {
                await _repo.DeleteQuickNoteAsync(item.Id);
                Notes.Remove(item);
                item.Dispose();
                await BroadcastAsync(new NotePage { Id = item.Id, Scope = "quicknote" }, "removed");
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[QuickNotes] Delete failed", ex);
            }
        }

        private QuickNoteItemViewModel BuildItem(NotePage page) =>
            new QuickNoteItemViewModel(
                page,
                _repo,
                DeleteItemAsync,
                async p => await BroadcastAsync(p, "updated"));

        public Func<NotePage, string, Task>? Broadcast { get; set; }

        private async Task BroadcastAsync(NotePage page, string changeType)
        {
            if (Broadcast == null) return;
            try { await Broadcast(page, changeType); }
            catch (Exception ex) { ErrorLog.Log($"[QuickNotes] Broadcast failed", ex); }
        }

        public void ApplyRemoteChange(string pageId, string changeType, NotePage? page)
        {
            if (changeType == "removed")
            {
                var match = Notes.FirstOrDefault(n => n.Id == pageId);
                if (match != null)
                {
                    Notes.Remove(match);
                    match.Dispose();
                }
                return;
            }

            if (page == null) return;
            if (page.Scope != "quicknote") return;
            if (page.OwnerUserId != _userId) return;

            var existing = Notes.FirstOrDefault(n => n.Id == page.Id);
            if (existing == null)
            {
                Notes.Add(BuildItem(page));
            }
            else
            {
                existing.ApplyRemoteUpdate(page);
            }
        }
    }
}