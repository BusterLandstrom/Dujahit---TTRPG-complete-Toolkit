using Dujahit.Models;
using Dujahit.Models.Application;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Dujahit.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private ViewModelBase _contentPanel;

        public ViewModelBase ContentPanel
        {
            get => _contentPanel;
            set => this.RaiseAndSetIfChanged(ref _contentPanel, value);
        }

        private CampaignViewModel? _openCampaign;

        private double _uiScale = 1.0;
        public double UiScale
        {
            get => _uiScale;
            private set => this.RaiseAndSetIfChanged(ref _uiScale, value);
        }

        public MainWindowViewModel()
        {
            HookScale();
            ContentPanel = new InitViewModel(this);
        }

        public MainWindowViewModel(LoadingViewModel startupLoading)
        {
            HookScale();
            ContentPanel = startupLoading;
        }

        private void HookScale()
        {
            UiScaleService.EnsureLoaded();
            UiScale = UiScaleService.Scale;
            UiScaleService.Changed += () => Dispatcher.UIThread.Post(() => UiScale = UiScaleService.Scale);
        }

        public void ShowInitialView()
        {
            ContentPanel = new InitViewModel(this);
        }

        public void NavigateTo(ViewModelBase viewModel)
        {
            ContentPanel = viewModel;
        }

        public async Task LeaveCampaignAsync()
        {
            var leaving = _openCampaign;
            _openCampaign = null;

            if (leaving != null)
            {
                try
                {
                    if (leaving.LiveSession != null) await leaving.CloseMapSessionAsync();
                }
                catch (Exception ex) { ErrorLog.Log("[Leave] closing the open map failed", ex); }

                try { leaving.Detach(); }
                catch (Exception ex) { ErrorLog.Log("[Leave] detaching the campaign failed", ex); }
            }

            var com = App.PM?.ComController;
            if (com != null)
            {
                try { await com.DisconnectAsync(); }
                catch (Exception ex) { ErrorLog.Log("[Leave] disconnecting failed", ex); }
                try { await com.StopServerAsync(); }
                catch (Exception ex) { ErrorLog.Log("[Leave] stopping the host failed", ex); }
            }

            NavigateTo(new StartDashViewModel(this));
        }

        public async Task ShowLoadingAndEnterAsync(string title, Func<LoadingViewModel, Task<UserRole?>> heavyWork)
        {
            var loading = new LoadingViewModel(title, () => NavigateTo(new StartDashViewModel(this)));
            NavigateTo(loading);

            UserRole? role;
            try
            {
                role = await heavyWork(loading);
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Enter]", ex);
                loading.Fail(JoinViewModel.FriendlyJoinError(ex));
                return;
            }

            if (role == null)
            {
                if (!loading.HasFailed) loading.Fail("Could not enter the campaign");
                return;
            }

            loading.Report("Setting the table", 0.80);
            _openCampaign?.Detach();
            var campaign = new CampaignViewModel(this, role.Value);
            _openCampaign = campaign;
            await campaign.InitializeAsync(loading.Report);
            loading.Report("Ready", 1.0);
            NavigateTo(campaign);
        }
    }
}
