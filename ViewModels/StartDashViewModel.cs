using Dujahit.Models;
using Dujahit.Models.Application;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Dujahit.ViewModels
{
    public class StartDashViewModel : ViewModelBase
    {
        private MainWindowViewModel _mainWVM;

        private string? _campaignName;
        public string CampaignName
        {
            get => _campaignName;
            set
            {
                this.RaiseAndSetIfChanged(ref _campaignName, value);
                FilterCampaigns();
            }
        }

        private string _campaignNameWatermark = "Search or Create Campaign";
        public string CampaignNameWatermark
        {
            get => _campaignNameWatermark;
            set => this.RaiseAndSetIfChanged(ref _campaignNameWatermark, value);
        }

        private ObservableCollection<Campaign> _allCampaigns = new();
        public ObservableCollection<Campaign> Campaigns { get; } = new();

        private Campaign? _selectedCampaign;
        public Campaign SelectedCampaign
        {
            get => _selectedCampaign;
            set => this.RaiseAndSetIfChanged(ref _selectedCampaign, value);
        }

        public ICommand JoinCampaign { get; set; }
        public ICommand CreateCampaign { get; set; }
        public ICommand LoadCampaign { get; set; }

        public ICommand Back { get; set; }

        public StartDashViewModel(MainWindowViewModel mwvm)
        {
            _mainWVM = mwvm;

            Back = ReactiveCommand.Create(BackProcess);

            var joinCommand = ReactiveCommand.CreateFromTask(ChangeJoin);
            joinCommand.ThrownExceptions.Subscribe(ex => ErrorLog.Log($"[Join] connect failed", ex));

            JoinCampaign = joinCommand;
            CreateCampaign = ReactiveCommand.Create(() =>
            {
                if (!string.IsNullOrWhiteSpace(CampaignName))
                    _mainWVM.NavigateTo(new CreateCViewModel(_mainWVM, CampaignName));
                else
                    CampaignNameWatermark = "Give the campaign a name first";
            });
            LoadCampaign = ReactiveCommand.CreateFromTask(ChangeLoad);

            this.WhenAnyValue(x => x.SelectedCampaign)
               .Where(campaign => campaign != null)
               .ObserveOn(RxApp.MainThreadScheduler)
               .Subscribe(async campaign =>
               {
                   if (campaign.IsRemote)
                       await RejoinCampaignAsync(campaign);
                   else
                       await LoadCampaignAsync(campaign);
               });

            _ = LoadCampaignsAsync();
        }

        public void BackProcess()
        {
            _mainWVM.NavigateTo(new UserInitViewModel(_mainWVM));
        }

        private async Task LoadCampaignsAsync()
        {
            var list = await App.PM.ListMyCampaignsAsync();
            _allCampaigns = new ObservableCollection<Campaign>(list);
            FilterCampaigns();
        }

        private async Task LoadCampaignAsync(Campaign campaign)
        {
            var campaignId = campaign.Id;
            var title = string.IsNullOrWhiteSpace(campaign.Name) ? "Loading campaign" : $"Loading {campaign.Name}";
            await _mainWVM.ShowLoadingAndEnterAsync(title, async loading =>
            {
                return await Task.Run<UserRole?>(async () =>
                {
                    await App.PM.LoadCampaign(campaignId, loading.Report);
                    var roleString = await App.PM.GetRoleAsync(campaignId, App.PM.GetCurrentUser().Id);
                    return RoleFrom(roleString);
                });
            });
        }

        private async Task RejoinCampaignAsync(Campaign campaign)
        {
            var address = campaign.HostAddress ?? "";
            if (string.IsNullOrWhiteSpace(address))
            {
                CampaignNameWatermark = "That joined campaign has no saved host address";
                return;
            }

            var title = string.IsNullOrWhiteSpace(campaign.Name) ? "Rejoining campaign" : $"Rejoining {campaign.Name}";
            await _mainWVM.ShowLoadingAndEnterAsync(title, async loading =>
            {
                loading.Report("Knocking on the host's door", 0.25);
                var connected = await Task.Run(() => App.PM.JoinCampaign(address, campaign.JoinCode ?? "", loading.Report));
                if (!connected)
                {
                    loading.Fail("Couldn't reach that host, are they hosting right now?");
                    return (UserRole?)null;
                }

                var campaignId = App.PM.GetCampaignId();
                if (string.IsNullOrEmpty(campaignId))
                {
                    loading.Fail("Connected but the campaign hasn't synced yet");
                    return (UserRole?)null;
                }

                var roleString = await App.PM.GetRoleAsync(campaignId, App.PM.GetCurrentUser().Id);
                return RoleFrom(roleString);
            });
        }

        private static UserRole RoleFrom(string? roleString) => roleString switch
        {
            "dm" => UserRole.Dm,
            "player" => UserRole.Player,
            "spectator" => UserRole.Spectator,
            _ => UserRole.Spectator
        };

        private void FilterCampaigns()
        {
            Campaigns.Clear();
            var filtered = string.IsNullOrWhiteSpace(_campaignName)
                ? _allCampaigns
                : _allCampaigns.Where(c =>
                    c.Name.Contains(_campaignName, StringComparison.OrdinalIgnoreCase) ||
                    (c.Description?.Contains(_campaignName, StringComparison.OrdinalIgnoreCase) ?? false));

            foreach (var c in filtered)
                Campaigns.Add(c);
        }

        public async Task ChangeJoin()
        {
            var address = CampaignName?.Trim() ?? "";
            if (!address.Contains('/') && !address.Contains(':'))
            {
                CampaignName = "";
                CampaignNameWatermark = "Host address, like 192.168.1.20:5555";
                return;
            }

            await _mainWVM.ShowLoadingAndEnterAsync("Joining the campaign", async loading =>
            {
                loading.Report("Knocking on the host's door", 0.25);
                var connected = await Task.Run(() => App.PM.JoinCampaign(address, "", loading.Report));
                if (!connected)
                {
                    loading.Fail("Couldn't connect to that host");
                    return (UserRole?)null;
                }

                var campaignId = App.PM.GetCampaignId();
                if (string.IsNullOrEmpty(campaignId))
                {
                    loading.Fail("Connected but the campaign hasn't synced yet");
                    return (UserRole?)null;
                }

                var roleString = await App.PM.GetRoleAsync(campaignId, App.PM.GetCurrentUser().Id);
                return RoleFrom(roleString);
            });
        }

        public Task ChangeCreate()
        {
            if (!string.IsNullOrWhiteSpace(CampaignName))
            {
                _mainWVM.NavigateTo(new CreateCViewModel(_mainWVM, CampaignName));
            }
            else
            {
                CampaignNameWatermark = "Give the campaign a name first";
            }
            return Task.CompletedTask;
        }
        public async Task ChangeLoad() { }
    }
}