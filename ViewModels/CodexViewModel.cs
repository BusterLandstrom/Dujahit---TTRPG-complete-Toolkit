using Dujahit.Models.Database;
using Dujahit.ViewModels;
using ReactiveUI;
using System;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class CodexViewModel : ViewModelBase
    {
        public ChaptersViewModel Chapters { get; }
        public NpcManagerViewModel Npcs { get; }
        public ItemManagerViewModel Items { get; }

        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedTabIndex, value);
        }

        public CodexViewModel(
            NotePageRepository noteRepo,
            DatabaseManager db,
            string campaignId,
            string userId)
        {
            Chapters = new ChaptersViewModel(noteRepo, campaignId, userId);
            Npcs = new NpcManagerViewModel(db, campaignId, userId);
            Items = new ItemManagerViewModel(db, campaignId, userId);
        }

        public async Task LoadAsync()
        {
            await Task.WhenAll(
                Chapters.LoadAsync(),
                Npcs.LoadAsync(),
                Items.LoadAsync());
        }
    }
}