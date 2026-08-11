using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dujahit.Models.Communication;
using Dujahit.Models.Settings;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Reactive;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;
using Dujahit.Models.Application;

namespace Dujahit.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private string _backgroundHex = "";
        public string BackgroundHex
        {
            get => _backgroundHex;
            set => this.RaiseAndSetIfChanged(ref _backgroundHex, value);
        }
        private string _foregroundHex = "";
        public string ForegroundHex
        {
            get => _foregroundHex;
            set => this.RaiseAndSetIfChanged(ref _foregroundHex, value);
        }
        private string _widgetHex = "";
        public string WidgetHex
        {
            get => _widgetHex;
            set => this.RaiseAndSetIfChanged(ref _widgetHex, value);
        }
        private string _widgetForegroundHex = "";
        public string WidgetForegroundHex
        {
            get => _widgetForegroundHex;
            set => this.RaiseAndSetIfChanged(ref _widgetForegroundHex, value);
        }
        private string _accentColorHex = "";
        public string AccentColorHex
        {
            get => _accentColorHex;
            set => this.RaiseAndSetIfChanged(ref _accentColorHex, value);
        }

        public double UiScalePercent
        {
            get => Math.Round(UiScaleService.Scale * 100);
            set
            {
                UiScaleService.SetUserScale(value / 100.0);
                this.RaisePropertyChanged();
                this.RaisePropertyChanged(nameof(UiScaleLabel));
            }
        }
        public string UiScaleLabel => $"{UiScalePercent:0}%";
        private string _accentHoverHex = "";
        public string AccentHoverHex
        {
            get => _accentHoverHex;
            set => this.RaiseAndSetIfChanged(ref _accentHoverHex, value);
        }
        private string _gryphGrayHex = "";
        public string DividerHex
        {
            get => _gryphGrayHex;
            set => this.RaiseAndSetIfChanged(ref _gryphGrayHex, value);
        }
        private string _mutedHex = "";
        public string MutedHex
        {
            get => _mutedHex;
            set => this.RaiseAndSetIfChanged(ref _mutedHex, value);
        }
        private string _dangerRedHex = "";
        public string DangerHex
        {
            get => _dangerRedHex;
            set => this.RaiseAndSetIfChanged(ref _dangerRedHex, value);
        }

        private string _newThemeName = "";
        public string NewThemeName
        {
            get => _newThemeName;
            set => this.RaiseAndSetIfChanged(ref _newThemeName, value);
        }

        private bool _isDm = true;
        public bool IsDm
        {
            get => _isDm;
            set => this.RaiseAndSetIfChanged(ref _isDm, value);
        }

        public ObservableCollection<Theme> Themes { get; } = new();

        private Theme? _selectedTheme;
        public Theme? SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedTheme, value);
                if (value != null) LoadIntoEditor(value);
            }
        }

        public ReactiveCommand<Unit, Unit> SaveTheme { get; }
        public ReactiveCommand<Unit, Unit> ImportTheme { get; }
        public ReactiveCommand<Unit, Unit> ExportTheme { get; }
        public ReactiveCommand<Unit, Unit> BackupDatabase { get; }
        public ReactiveCommand<Unit, Unit> RestoreDatabase { get; }
        public ReactiveCommand<Unit, Unit> OpenBackupsFolder { get; }
        public ReactiveCommand<Unit, Unit> ImportCharacter { get; }
        public ReactiveCommand<Unit, Unit> ExportCampaign { get; }
        public ReactiveCommand<Unit, Unit> ImportCampaign { get; }
        public ReactiveCommand<Unit, Unit> RegenerateJoinCode { get; }
        public ReactiveCommand<Unit, Unit> CopyInvite { get; }
        public ReactiveCommand<Unit, Unit> CopyLanInvite { get; }
        public ReactiveCommand<Unit, Unit> DeleteCampaign { get; }

        public event Func<Task>? DeleteCampaignRequested;

        private string _dataStatus = "";
        public string DataStatus
        {
            get => _dataStatus;
            set => this.RaiseAndSetIfChanged(ref _dataStatus, value);
        }

        private string _joinCode = "";
        public string JoinCode
        {
            get => _joinCode;
            set => this.RaiseAndSetIfChanged(ref _joinCode, value);
        }

        private string _hostAddress = "";
        public string HostAddress
        {
            get => _hostAddress;
            set => this.RaiseAndSetIfChanged(ref _hostAddress, value);
        }

        private string _hostPort = "";
        public string HostPort
        {
            get => _hostPort;
            set => this.RaiseAndSetIfChanged(ref _hostPort, value);
        }

        private string _inviteAddress = "";
        public string InviteAddress
        {
            get => _inviteAddress;
            set => this.RaiseAndSetIfChanged(ref _inviteAddress, value);
        }

        private string _lanAddress = "";

        private string _localAddressHint = "";
        public string LocalAddressHint
        {
            get => _localAddressHint;
            set => this.RaiseAndSetIfChanged(ref _localAddressHint, value);
        }

        public event Func<string, Task>? CopyToClipboardRequested;
        public event Func<string, string, Task<bool>>? ConfirmRestoreAsync;

        private bool _encryptTransport;
        public bool EncryptTransport
        {
            get => _encryptTransport;
            set
            {
                this.RaiseAndSetIfChanged(ref _encryptTransport, value);
                CommunicationController.PlainHttpPreferred = !value;
            }
        }

        private bool _hideRolls;
        public bool HideRolls
        {
            get => _hideRolls;
            set
            {
                this.RaiseAndSetIfChanged(ref _hideRolls, value);
                if (App.PM != null) App.PM.HideRolls = value;
            }
        }

        private string _presenceColorHex = "#FFD700";
        public string PresenceColorHex
        {
            get => _presenceColorHex;
            set
            {
                this.RaiseAndSetIfChanged(ref _presenceColorHex, value);
                if (App.PM != null) App.PM.DmPresenceColor = value;
            }
        }

        private bool _combatSettingsLoaded;

        private int _baseActions = 1;
        public int BaseActions
        {
            get => _baseActions;
            set { this.RaiseAndSetIfChanged(ref _baseActions, value); PersistCombatSettings(); }
        }

        private int _baseBonusActions = 1;
        public int BaseBonusActions
        {
            get => _baseBonusActions;
            set { this.RaiseAndSetIfChanged(ref _baseBonusActions, value); PersistCombatSettings(); }
        }

        private bool _autoFlanking = true;
        public bool AutoFlanking
        {
            get => _autoFlanking;
            set { this.RaiseAndSetIfChanged(ref _autoFlanking, value); PersistCombatSettings(); }
        }

        private bool _multiclassingAllowed = true;
        public bool MulticlassingAllowed
        {
            get => _multiclassingAllowed;
            set { this.RaiseAndSetIfChanged(ref _multiclassingAllowed, value); PersistCombatSettings(); }
        }

        private bool _dmIgnoresMovementBudget;
        public bool DmIgnoresMovementBudget
        {
            get => _dmIgnoresMovementBudget;
            set { this.RaiseAndSetIfChanged(ref _dmIgnoresMovementBudget, value); PersistCombatSettings(); }
        }

        private bool _enforceMovementBudget = true;
        public bool EnforceMovementBudget
        {
            get => _enforceMovementBudget;
            set { this.RaiseAndSetIfChanged(ref _enforceMovementBudget, value); PersistCombatSettings(); }
        }

        private bool _playerRollsOwnSaves;
        public bool PlayerRollsOwnSaves
        {
            get => _playerRollsOwnSaves;
            set { this.RaiseAndSetIfChanged(ref _playerRollsOwnSaves, value); PersistCombatSettings(); }
        }

        // Instant save like the hide rolls toggle right above it, the flag keeps the initial load from writing back over itself.
        private void PersistCombatSettings()
        {
            if (!_combatSettingsLoaded || App.PM == null) return;
            _ = App.PM.SaveCombatSettingsAsync(BaseActions, BaseBonusActions, AutoFlanking, MulticlassingAllowed, DmIgnoresMovementBudget,
                EnforceMovementBudget, PlayerRollsOwnSaves);
        }

        public SettingsViewModel()
        {
            SaveTheme = ReactiveCommand.CreateFromTask(ThemeSave);
            ImportTheme = ReactiveCommand.CreateFromTask(ImportThemeAsync);
            ExportTheme = ReactiveCommand.CreateFromTask(ExportThemeAsync);
            BackupDatabase = ReactiveCommand.CreateFromTask(BackupDatabaseAsync);
            RestoreDatabase = ReactiveCommand.CreateFromTask(RestoreDatabaseAsync);
            OpenBackupsFolder = ReactiveCommand.Create(OpenBackupsFolderImpl);
            ImportCharacter = ReactiveCommand.CreateFromTask(ImportCharacterAsync);
            ExportCampaign = ReactiveCommand.CreateFromTask(ExportCampaignAsync);
            ImportCampaign = ReactiveCommand.CreateFromTask(ImportCampaignAsync);
            RegenerateJoinCode = ReactiveCommand.CreateFromTask(RegenerateJoinCodeAsync);
            DeleteCampaign = ReactiveCommand.CreateFromTask(async () =>
            {
                var handler = DeleteCampaignRequested;
                if (handler != null) await handler();
            });
            _hideRolls = App.PM?.HideRolls ?? false;
            _presenceColorHex = App.PM?.DmPresenceColor ?? "#FFD700";
            this.RaisePropertyChanged(nameof(PresenceColorHex));
            _joinCode = App.PM?.GetJoinSecret() ?? "";
            _hostAddress = App.PM?.GetLanAddress() ?? "";
            _lanAddress = _hostAddress;
            _hostPort = App.PM?.ComController?.ServerPort ?? "";
            _localAddressHint = _lanAddress.Length > 0 ? "On your own network, click to copy the lan invite: " + _lanAddress + ":" + _hostPort : "";
            RebuildInvite();
            _encryptTransport = !CommunicationController.PlainHttpPreferred;
            CopyInvite = ReactiveCommand.CreateFromTask(async () =>
            {
                if (InviteAddress.Length > 0 && CopyToClipboardRequested != null) await CopyToClipboardRequested(InviteAddress);
            });
            CopyLanInvite = ReactiveCommand.CreateFromTask(async () =>
            {
                var invite = BuildInvite(_lanAddress);
                if (invite.Length == 0 || CopyToClipboardRequested == null) return;
                await CopyToClipboardRequested(invite);
                DataStatus = "Copied the lan invite, " + invite + ". That one only works for players on this same network.";
            });
            if (_hostAddress.Length > 0) _ = FetchPublicAddressAsync();
            _ = LoadThemesAsync();
            _ = LoadCombatSettingsAsync();
        }

        private async Task LoadCombatSettingsAsync()
        {
            if (App.PM == null) return;
            var (baseA, baseB) = await App.PM.GetCombatSettingsAsync();
            var flank = App.PM.CombatAutoFlanking;
            var mc = App.PM.Rules.MulticlassingAllowedByDm;
            var freeMove = App.PM.Rules.DmIgnoresMovementBudget;
            var enforceMove = App.PM.Rules.EnforceMovementBudget;
            var ownSaves = App.PM.Rules.PlayerRollsOwnSaves;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BaseActions = baseA;
                BaseBonusActions = baseB;
                AutoFlanking = flank;
                MulticlassingAllowed = mc;
                DmIgnoresMovementBudget = freeMove;
                EnforceMovementBudget = enforceMove;
                PlayerRollsOwnSaves = ownSaves;
                _combatSettingsLoaded = true;
            });
        }

        public async Task LoadThemesAsync()
        {
            var all = await App.PM.ThemeManager.GetAllThemesAsync();
            Dispatcher.UIThread.Post(() =>
            {
                Themes.Clear();
                foreach (var t in all) Themes.Add(t);
            });
        }

        private void LoadIntoEditor(Theme t)
        {
            BackgroundHex = t.Background;
            ForegroundHex = t.Foreground;
            WidgetHex = t.Widget;
            WidgetForegroundHex = t.WidgetForeground;
            AccentColorHex = t.AccentColor;
            AccentHoverHex = t.AccentHover;
            DividerHex = t.Divider;
            DangerHex = t.Danger;
            MutedHex = t.Muted;
            NewThemeName = t.Name;
            App.PM.ThemeManager.ApplyTheme(t);
            _ = App.PM.ThemeManager.MarkActiveAsync(t);
        }

        private Theme BuildThemeFromEditor() => new Theme
        {
            Name = string.IsNullOrWhiteSpace(NewThemeName) ? "Untitled" : NewThemeName.Trim(),
            Background = BackgroundHex,
            Foreground = ForegroundHex,
            Widget = WidgetHex,
            WidgetForeground = WidgetForegroundHex,
            AccentColor = AccentColorHex,
            AccentHover = AccentHoverHex,
            Divider = DividerHex,
            Danger = DangerHex,
            Muted = MutedHex,
        };

        public async Task ThemeSave()
        {
            await App.PM.ThemeManager.SetNewTheme(BuildThemeFromEditor());
            await LoadThemesAsync();
            NavItem.NavError?.Invoke("Theme saved and applied.");
        }

        private async Task ImportThemeAsync()
        {
            var sp = GetStorage();
            if (sp is null) return;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import theme",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Theme JSON") { Patterns = new[] { "*.json" } }
                }
            });

            var file = files.FirstOrDefault();
            if (file is null) return;

            await App.PM.ThemeManager.ImportTheme(file.Path.LocalPath);
            await LoadThemesAsync();
        }

        private async Task ExportThemeAsync()
        {
            var theme = SelectedTheme ?? BuildThemeFromEditor();

            var sp = GetStorage();
            if (sp is null) return;

            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export theme",
                SuggestedFileName = (string.IsNullOrWhiteSpace(theme.Name) ? "theme" : theme.Name) + ".json",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Theme JSON") { Patterns = new[] { "*.json" } }
                }
            });
            if (file is null) return;

            await App.PM.ThemeManager.ExportTheme(theme, file.Path.LocalPath);
        }

        private static IStorageProvider? GetStorage()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow is { } w)
                return w.StorageProvider;
            return null;
        }

        private async Task BackupDatabaseAsync()
        {
            if (App.PM == null) return;
            var sp = GetStorage();
            if (sp is null) return;

            var name = App.PM.GetCampaignName();
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Back up everything",
                SuggestedFileName = (string.IsNullOrWhiteSpace(name) ? "dujahit" : name) + "-backup.db",
                DefaultExtension = "db",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Dujahit backup") { Patterns = new[] { "*.db" } }
                }
            });
            if (file is null) return;

            await App.PM.BackupDatabaseToFileAsync(file.Path.LocalPath);
            DataStatus = "Backed up to " + file.Path.LocalPath;
        }

        private async Task ImportCharacterAsync()
        {
            if (App.PM == null) return;
            var sp = GetStorage();
            if (sp is null) return;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import character",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Character JSON") { Patterns = new[] { "*.json" } }
                }
            });

            var file = files.FirstOrDefault();
            if (file is null) return;

            var (newId, warnings) = await App.PM.ImportCharacterFromFileAsync(file.Path.LocalPath);
            DataStatus = newId == null
                ? "Could not read that character file."
                : warnings.Count == 0
                    ? "Character imported into this campaign."
                    : "Character imported, but this didn't come across: " + string.Join(", ", warnings) + ".";
        }

        private async Task ExportCampaignAsync()
        {
            if (App.PM == null) return;
            var sp = GetStorage();
            if (sp is null) return;

            var name = App.PM.GetCampaignName();
            var file = await sp.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export this campaign",
                SuggestedFileName = (string.IsNullOrWhiteSpace(name) ? "campaign" : name) + "-campaign.json",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("Campaign JSON") { Patterns = new[] { "*.json" } }
                }
            });
            if (file is null) return;

            await App.PM.ExportCampaignToFileAsync(file.Path.LocalPath);
            DataStatus = "Campaign exported with its maps, art, tokens and notes.";
        }

        private async Task ImportCampaignAsync()
        {
            if (App.PM == null) return;
            var sp = GetStorage();
            if (sp is null) return;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import a campaign",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Campaign JSON") { Patterns = new[] { "*.json" } }
                }
            });

            var file = files.FirstOrDefault();
            if (file is null) return;

            var (newId, warnings) = await App.PM.ImportCampaignFromFileAsync(file.Path.LocalPath);
            DataStatus = newId == null
                ? "Could not read that campaign file."
                : warnings.Count == 0
                    ? "Campaign imported. It's in your campaign list now."
                    : "Campaign imported, heads up on: " + string.Join(", ", warnings) + ".";
        }

        private async Task RestoreDatabaseAsync()
        {
            if (App.PM == null) return;
            var sp = GetStorage();
            if (sp is null) return;

            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Restore from a backup",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Dujahit database") { Patterns = new[] { "*.db" } }
                }
            });

            var file = files.FirstOrDefault();
            if (file is null) return;

            var message = "Everything you have now, every campaign, character, map and note, is replaced by what is in that file when Dujahit next starts. "
                + "A copy of your current database is put in the backups folder first, so this can be undone by restoring that one.";

            if (ConfirmRestoreAsync != null && !await ConfirmRestoreAsync("Restore from this backup?", message)) return;

            var problem = App.PM.StageDatabaseRestore(file.Path.LocalPath);
            DataStatus = problem
                ?? "Backup staged. Close and reopen Dujahit to finish the restore, your current data is copied to the backups folder first.";
        }

        private void OpenBackupsFolderImpl()
        {
            try
            {
                var dir = App.PM?.GetBackupsDirectory();
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch { }
        }

        private async Task RegenerateJoinCodeAsync()
        {
            if (App.PM == null) return;
            JoinCode = await App.PM.RegenerateJoinSecretAsync();
            RebuildInvite();
            DataStatus = "New join code, anyone using the old one will be turned away, send the fresh invite around.";
        }

        private void RebuildInvite() => InviteAddress = BuildInvite(_hostAddress);

        private string BuildInvite(string address)
        {
            if (address.Length == 0) return "";
            var fp = App.PM?.ComController?.ServerFingerprint ?? "";
            return address + ":" + _hostPort + (_joinCode.Length > 0 ? ":" + _joinCode : "") + (fp.Length > 0 ? "#" + fp : "");
        }

        private async Task FetchPublicAddressAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var ip = (await http.GetStringAsync("https://api.ipify.org")).Trim();
                if (ip.Length == 0 || ip.Length > 45) return;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HostAddress = ip;
                    RebuildInvite();
                });
            }
            catch { }
        }
    }
}