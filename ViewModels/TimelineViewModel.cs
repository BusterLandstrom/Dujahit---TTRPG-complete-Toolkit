using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class TimelineViewModel : ViewModelBase
    {
        public ObservableCollection<TimelineEventViewModel> Events { get; } = new();

        private TimelineEventViewModel? _selected;
        public TimelineEventViewModel? Selected
        {
            get => _selected;
            set { this.RaiseAndSetIfChanged(ref _selected, value); ConfirmingDelete = false; this.RaisePropertyChanged(nameof(HasSelected)); }
        }

        private bool _confirmingDelete;
        public bool ConfirmingDelete
        {
            get => _confirmingDelete;
            set => this.RaiseAndSetIfChanged(ref _confirmingDelete, value);
        }

        public bool HasSelected => Selected != null;
        public bool HasEvents => Events.Count > 0;

        public ReactiveCommand<Unit, Unit> AddCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> ArmDeleteCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelDeleteCommand { get; }
        public ReactiveCommand<TimelineEventViewModel, Unit> MoveUpCommand { get; }
        public ReactiveCommand<TimelineEventViewModel, Unit> MoveDownCommand { get; }

        public TimelineViewModel()
        {
            AddCommand = ReactiveCommand.CreateFromTask(AddAsync);
            SaveSelectedCommand = ReactiveCommand.CreateFromTask(SaveSelectedAsync);
            DeleteSelectedCommand = ReactiveCommand.CreateFromTask(DeleteSelectedAsync);
            ArmDeleteCommand = ReactiveCommand.Create(() => { ConfirmingDelete = true; });
            CancelDeleteCommand = ReactiveCommand.Create(() => { ConfirmingDelete = false; });
            MoveUpCommand = ReactiveCommand.CreateFromTask<TimelineEventViewModel>(e => MoveAsync(e, -1));
            MoveDownCommand = ReactiveCommand.CreateFromTask<TimelineEventViewModel>(e => MoveAsync(e, 1));
        }

        public async Task LoadAsync()
        {
            var rows = await App.PM.LoadTimelineEventsAsync();
            Selected = null;
            Events.Clear();
            foreach (var e in rows) Events.Add(TimelineEventViewModel.FromModel(e));
            this.RaisePropertyChanged(nameof(HasEvents));
        }

        private async Task AddAsync()
        {
            double nextSort = Events.Count > 0 ? Events[Events.Count - 1].SortOrder + 1 : 0;
            var ev = new TimelineEventViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                Title = "New event",
                SortOrder = nextSort,
                CreatedAt = DateTime.UtcNow
            };
            Events.Add(ev);
            Selected = ev;
            this.RaisePropertyChanged(nameof(HasEvents));
            await App.PM.SaveTimelineEventAsync(ev.ToModel());
        }

        private async Task SaveSelectedAsync()
        {
            var ev = Selected;
            if (ev == null) return;
            ev.RaiseDisplay();
            await App.PM.SaveTimelineEventAsync(ev.ToModel());
        }

        private async Task DeleteSelectedAsync()
        {
            var ev = Selected;
            if (ev == null) return;
            Events.Remove(ev);
            Selected = null;
            this.RaisePropertyChanged(nameof(HasEvents));
            await App.PM.DeleteTimelineEventAsync(ev.Id);
        }

        private async Task MoveAsync(TimelineEventViewModel ev, int dir)
        {
            if (ev == null) return;
            int i = Events.IndexOf(ev);
            int j = i + dir;
            if (i < 0 || j < 0 || j >= Events.Count) return;

            var other = Events[j];
            (ev.SortOrder, other.SortOrder) = (other.SortOrder, ev.SortOrder);
            Events.Move(i, j);
            await App.PM.SaveTimelineEventAsync(ev.ToModel());
            await App.PM.SaveTimelineEventAsync(other.ToModel());
        }
    }

    public class TimelineEventViewModel : ViewModelBase
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public double SortOrder { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        private string _title = "";
        public string Title { get => _title; set => this.RaiseAndSetIfChanged(ref _title, value); }

        private string? _description;
        public string? Description { get => _description; set => this.RaiseAndSetIfChanged(ref _description, value); }

        private string? _inWorldDate;
        public string? InWorldDate { get => _inWorldDate; set => this.RaiseAndSetIfChanged(ref _inWorldDate, value); }

        public string WhenLabel => string.IsNullOrWhiteSpace(InWorldDate) ? "" : InWorldDate!;

        public void RaiseDisplay()
        {
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(WhenLabel));
        }

        public static TimelineEventViewModel FromModel(TimelineEvent e) => new()
        {
            Id = e.Id,
            CampaignId = e.CampaignId,
            Title = e.Title,
            Description = e.Description,
            InWorldDate = e.InWorldDate,
            SortOrder = e.SortOrder,
            CreatedAt = e.CreatedAt
        };

        public TimelineEvent ToModel() => new()
        {
            Id = Id,
            CampaignId = CampaignId,
            Title = Title,
            Description = Description,
            InWorldDate = InWorldDate,
            SortOrder = SortOrder,
            CreatedAt = CreatedAt
        };
    }
}
