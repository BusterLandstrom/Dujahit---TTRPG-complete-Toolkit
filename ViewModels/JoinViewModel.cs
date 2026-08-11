using Dujahit.Models;
using Dujahit.Models.Application;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Dujahit.ViewModels
{
    public class JoinViewModel : ViewModelBase
    {
        private MainWindowViewModel _mainWVM;

        private string _campaignAddress;

        public string CampaignAddress 
        {
            get => _campaignAddress;
            set => this.RaiseAndSetIfChanged(ref _campaignAddress, value);
        }
        private string _campaignPort;

        public string CampaignPort
        {
            get => _campaignPort;
            set => this.RaiseAndSetIfChanged(ref _campaignPort, value);
        }

        private string _campaignCode = "";
        public string CampaignCode
        {
            get => _campaignCode;
            set => this.RaiseAndSetIfChanged(ref _campaignCode, value);
        }

        private string _status = "";
        public string Status
        {
            get => _status;
            set => this.RaiseAndSetIfChanged(ref _status, value);
        }

        private bool _isConnecting;
        public bool IsConnecting
        {
            get => _isConnecting;
            set => this.RaiseAndSetIfChanged(ref _isConnecting, value);
        }

        public ICommand JoinGame {  get; set; }

        public ICommand Back { get; set; }

        public JoinViewModel(MainWindowViewModel mwvm) 
        {
            _mainWVM = mwvm;

            JoinGame = ReactiveCommand.Create(InitJoin);
            Back = ReactiveCommand.Create(BackProcess);
        }

        public async Task InitJoin()
        {
            if (IsConnecting) return;

            var (address, port, pastedCode, fingerprint) = ParseInvite(CampaignAddress ?? "", CampaignPort ?? "");
            var code = pastedCode.Length > 0 ? pastedCode : (CampaignCode ?? "").Trim();
            if (pastedCode.Length > 0) CampaignCode = pastedCode;

            if (address.Length == 0) { Status = "Enter the host address or paste the whole invite the DM gave you."; return; }
            if (!int.TryParse(port, out var p) || p < 1 || p > 65535) { Status = "Port should be a number between 1 and 65535."; return; }

            var path = address + ":" + port + (fingerprint.Length > 0 ? "#" + fingerprint : "");

            try
            {
                Status = "";
                IsConnecting = true;
                await _mainWVM.ShowLoadingAndEnterAsync("Joining the campaign", async loading =>
                {
                    loading.Report("Knocking on the host's door", 0.25);
                    var connected = await Task.Run(() => App.PM.JoinCampaign(path, code, loading.Report));
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

                    var roleString = await App.PM.GetRoleAsync(campaignId, App.PM.GetUID());
                    return roleString switch
                    {
                        "dm" => UserRole.Dm,
                        "player" => UserRole.Player,
                        _ => UserRole.Spectator
                    };
                });
            }
            catch (Exception ex)
            {
                Status = FriendlyJoinError(ex);
            }
            finally
            {
                IsConnecting = false;
            }
        }

        internal static (string Address, string Port, string Code, string Fingerprint) ParseInvite(string addressBox, string portBox)
        {
            var address = addressBox.Trim();
            var port = string.IsNullOrWhiteSpace(portBox) ? "5555" : portBox.Trim();
            var code = "";
            var fingerprint = "";

            var hashIdx = address.IndexOf('#');
            if (hashIdx >= 0)
            {
                fingerprint = address[(hashIdx + 1)..].Trim();
                address = address[..hashIdx].Trim();
            }

            var pieces = address.Split(':', StringSplitOptions.TrimEntries);
            address = pieces.Length > 0 ? pieces[0] : "";
            if (pieces.Length > 1 && pieces[1].Length > 0) port = pieces[1];
            if (pieces.Length > 2 && pieces[2].Length > 0) code = pieces[2];

            return (address, port, code, fingerprint);
        }

        internal static string FriendlyJoinError(Exception ex)
        {
            var text = ex.ToString();
            bool has(string s) => text.Contains(s, StringComparison.OrdinalIgnoreCase);

            if (has("AuthenticationException") || has("certificate") || has("SSL"))
                return "The host answered but its identity didn't match the invite. Ask the DM for a fresh invite string.";
            if (has("refused") || has("unreachable") || has("No connection could be made") || has("SocketException") || has("HttpRequestException"))
                return "Couldn't reach the host. Check the address and port, and that the DM is hosting the game.";
            if (has("timeout") || has("TaskCanceled") || has("canceled"))
                return "The host didn't answer in time. Make sure you're on the same network or VPN, then try again.";
            return string.IsNullOrWhiteSpace(ex.Message) ? "Couldn't join the game." : ex.Message;
        }

        public void BackProcess()
        {
            _mainWVM.NavigateTo(new UserInitViewModel(_mainWVM));
        }
    }
}
