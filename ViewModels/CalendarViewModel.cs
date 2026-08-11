using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class CalendarViewModel : ViewModelBase
    {
        public ObservableCollection<CalendarEventViewModel> Events { get; } = new();
        public ObservableCollection<string> KindOptions { get; } = new() { "session", "event", "reminder" };

        private CalendarEventViewModel? _selected;
        public CalendarEventViewModel? Selected
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

        public CalendarViewModel()
        {
            AddCommand = ReactiveCommand.CreateFromTask(AddAsync);
            SaveSelectedCommand = ReactiveCommand.CreateFromTask(SaveSelectedAsync);
            DeleteSelectedCommand = ReactiveCommand.CreateFromTask(DeleteSelectedAsync);
            ArmDeleteCommand = ReactiveCommand.Create(() => { ConfirmingDelete = true; });
            CancelDeleteCommand = ReactiveCommand.Create(() => { ConfirmingDelete = false; });
        }

        public async Task LoadAsync()
        {
            var rows = await App.PM.LoadCalendarEventsAsync();
            Selected = null;
            Events.Clear();
            foreach (var e in rows) Events.Add(CalendarEventViewModel.FromModel(e));
            this.RaisePropertyChanged(nameof(HasEvents));
        }

        private async Task AddAsync()
        {
            var ev = new CalendarEventViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                Title = "New session",
                Kind = "session",
                EventDate = DateTime.Now.ToString("yyyy-MM-dd"),
                CreatedAt = DateTime.UtcNow
            };
            Events.Add(ev);
            Selected = ev;
            this.RaisePropertyChanged(nameof(HasEvents));
            await App.PM.SaveCalendarEventAsync(ev.ToModel());
        }

        private async Task SaveSelectedAsync()
        {
            var ev = Selected;
            if (ev == null) return;
            ev.RaiseDisplay();
            await App.PM.SaveCalendarEventAsync(ev.ToModel());
        }

        private async Task DeleteSelectedAsync()
        {
            var ev = Selected;
            if (ev == null) return;
            Events.Remove(ev);
            Selected = null;
            this.RaisePropertyChanged(nameof(HasEvents));
            await App.PM.DeleteCalendarEventAsync(ev.Id);
        }
    }

    public class CalendarEventViewModel : ViewModelBase
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        private string _title = "";
        public string Title { get => _title; set { this.RaiseAndSetIfChanged(ref _title, value); } }

        private string _kind = "session";
        public string Kind { get => _kind; set { this.RaiseAndSetIfChanged(ref _kind, value); } }

        private string? _eventDate;
        public string? EventDate { get => _eventDate; set => this.RaiseAndSetIfChanged(ref _eventDate, value); }

        private string? _inWorldDate;
        public string? InWorldDate { get => _inWorldDate; set => this.RaiseAndSetIfChanged(ref _inWorldDate, value); }

        private string? _notes;
        public string? Notes { get => _notes; set => this.RaiseAndSetIfChanged(ref _notes, value); }

        public string DateLabel =>
            !string.IsNullOrWhiteSpace(EventDate) ? EventDate!
            : !string.IsNullOrWhiteSpace(InWorldDate) ? InWorldDate!
            : "no date";

        public void RaiseDisplay()
        {
            this.RaisePropertyChanged(nameof(Title));
            this.RaisePropertyChanged(nameof(DateLabel));
        }

        public static CalendarEventViewModel FromModel(CalendarEvent e) => new()
        {
            Id = e.Id,
            CampaignId = e.CampaignId,
            Title = e.Title,
            Kind = string.IsNullOrWhiteSpace(e.Kind) ? "session" : e.Kind,
            EventDate = e.EventDate,
            InWorldDate = e.InWorldDate,
            Notes = e.Notes,
            CreatedAt = e.CreatedAt
        };

        public CalendarEvent ToModel() => new()
        {
            Id = Id,
            CampaignId = CampaignId,
            Title = Title,
            Kind = Kind,
            EventDate = EventDate,
            InWorldDate = InWorldDate,
            Notes = Notes,
            CreatedAt = CreatedAt
        };
    }
}
