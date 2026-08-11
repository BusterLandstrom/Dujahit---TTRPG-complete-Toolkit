using Avalonia.Media;
using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using Dujahit.Views;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{

    /*
     
        This File might need to be migrated, no sure i dont like how i only put these here
         
    */
    public class CharacterCardViewModel : ViewModelBase
    {
        public string Id { get; }
        public string Name { get; }
        public string RaceName { get; }
        public string ClassName { get; }
        public int Level { get; }
        public bool IsUnassigned { get; }

        public string RaceAndClassLine =>
            string.IsNullOrWhiteSpace(RaceName) && string.IsNullOrWhiteSpace(ClassName)
                ? "Unconfigured"
                : $"{(string.IsNullOrWhiteSpace(RaceName) ? "-" : RaceName)}  •  {(string.IsNullOrWhiteSpace(ClassName) ? "-" : ClassName)}";

        public string LevelLine => $"Level {Level}";

        public CharacterCardViewModel(CharacterListEntry row)
        {
            Id = row.Id;
            Name = row.Name;
            RaceName = row.RaceName ?? "";
            ClassName = row.ClassName ?? "";
            Level = row.Level;
            IsUnassigned = string.IsNullOrEmpty(row.OwnerUserId);
        }
    }

    public class CharacterDashboardViewModel : ViewModelBase
    {
        private readonly CampaignViewModel? _parent;
        private ObservableCollection<CharacterCardViewModel> _characters = new();
        public ObservableCollection<CharacterCardViewModel> Characters
        {
            get => _characters;
            set => this.RaiseAndSetIfChanged(ref _characters, value);
        }

        private bool _isEmpty = true;
        public bool IsEmpty
        {
            get => _isEmpty;
            set => this.RaiseAndSetIfChanged(ref _isEmpty, value);
        }

        private string _characterCountLabel = "0 characters";
        public string CharacterCountLabel
        {
            get => _characterCountLabel;
            set => this.RaiseAndSetIfChanged(ref _characterCountLabel, value);
        }

        public ReactiveCommand<Unit, Unit> CreateUnassignedCharacterCommand { get; }
        public ReactiveCommand<CharacterCardViewModel, Unit> OpenSheetCommand { get; }

        public CharacterDashboardViewModel() : this(null) { }

        public CharacterDashboardViewModel(CampaignViewModel? parent)
        {
            _parent = parent;


            OpenSheetCommand =
                ReactiveCommand.CreateFromTask<CharacterCardViewModel>(OpenSheetAsync);

            CreateUnassignedCharacterCommand = ReactiveCommand.Create(OpenCreation);
            OpenSheetCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[Dashboard] open sheet failed", ex));
            CreateUnassignedCharacterCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[Dashboard] create failed", ex));
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;

            Characters.Clear();

            var rows = await App.PM.GetCampaignCharactersAsync();

            if (_parent?.CurrentRole != UserRole.Dm)
            {
                var me = App.PM.GetUID();
                rows = rows.Where(c => string.Equals(c.OwnerUserId, me, StringComparison.Ordinal)).ToList();
            }

            foreach (var c in rows)
                Characters.Add(new CharacterCardViewModel(c));

            UpdateCounters();
        }

        private void OpenCreation()
        {
            _parent?.OpenCharacterCreation();
        }

        public string CreateButtonLabel =>
            _parent?.CurrentRole == UserRole.Player ? "+ Create My Character" : "+ New Character";

        private void UpdateCounters()
        {
            IsEmpty = Characters.Count == 0;
            CharacterCountLabel = Characters.Count == 1
                ? "1 character"
                : $"{Characters.Count} characters";
        }

        /*private async Task CreateUnassignedCharacterAsync()
        {
            Debug.WriteLine("[Dashboard] CreateUnassignedCharacterAsync FIRED");
            Debug.WriteLine($"[Dashboard] _program null? {App.PM == null}");
            if (App.PM == null) return;

            var row = await App.PM.CreateUnassignedCharacterAsync();
            Debug.WriteLine($"[Dashboard] row returned: {row?.Id ?? "NULL"}");
            try
            {
                if (row != null)
                {
                    Characters.Add(new CharacterCardViewModel(row));
                    UpdateCounters();
                    Debug.WriteLine($"[Dashboard] Characters count now: {Characters.Count}");
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Dashboard] EXCEPTION", ex);
            }
        }*/

        private async Task OpenSheetAsync(CharacterCardViewModel card)
        {
            if (card == null || _parent == null) return;
            await _parent.OpenCharacterSheetAsync(card.Id);
        }
    }
}