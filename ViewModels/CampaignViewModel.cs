using Dujahit.Models;
using Dujahit.Models.UI;
using Dujahit.Models.Application;
using Dujahit.Models.Database;
using Dujahit.Models.Communication;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dujahit.ViewModels
{
    public class CampaignViewModel : ViewModelBase
    {
        private MainWindowViewModel _mainWVM;

        private ViewModelBase? _currentContent;
        public ViewModelBase? CurrentContent
        {
            get => _currentContent;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentContent, value);
                RefreshReturnToFight();
            }
        }

        public MapSessionViewModel? LiveSession => _mapSession;

        private bool _returnToFightVisible;
        public bool ReturnToFightVisible
        {
            get => _returnToFightVisible;
            set => this.RaiseAndSetIfChanged(ref _returnToFightVisible, value);
        }

        public ReactiveCommand<Unit, Unit>? ReturnToFightCommand { get; private set; }

        private void RefreshReturnToFight()
        {
            this.RaisePropertyChanged(nameof(LiveSession));
            ReturnToFightVisible = _mapSession != null && !ReferenceEquals(_currentContent, _mapSession);
        }

        private ChatViewModel _chatWidget;
        public ChatViewModel ChatWidget
        {
            get => _chatWidget;
            set => this.RaiseAndSetIfChanged(ref _chatWidget, value);
        }

        public UserRole CurrentRole { get; }
        public string CampaignName { get; }
        public ObservableCollection<NavItem> NavItems { get; }

        private readonly CampaignDashboardViewModel _dashboard = new();
        private readonly CharacterDashboardViewModel _characters;
        private readonly DmScreenViewModel _dmScreen = new();
        private readonly SettingsViewModel _settings = new();
        private readonly NotesViewModel _notes;
        private readonly MindmapViewModel _mindmap = new();
        private readonly CodexViewModel? _codex;
        private readonly CompendiumViewModel _compendium;
        private readonly TemplateEditorViewModel? _templateEditor;
        private readonly MapHubViewModel _mapHub;
        private readonly DmCommandsViewModel? _dmCommands;
        private readonly EncounterBuilderViewModel? _encounters;
        private readonly EncounterRunnerViewModel? _encounterRunner;
        private readonly HandoutsViewModel? _handouts;
        private readonly SessionLogViewModel? _sessionLog;
        private readonly RandomTablesViewModel? _randomTables;
        private readonly CalendarViewModel? _calendar;
        private readonly TimelineViewModel? _timeline;
        private MapSessionViewModel? _mapSession;
        private readonly MapHubViewModel _sharedMapHub;
        private MapSessionViewModel? _sharedSession;
        public QuickNotesPanelViewModel QuickNotesPanel { get; }
        public QuickNotesWidgetViewModel QuickNotesWidget { get; }
        // The dm drives what plays, everybody gets the part that decides how loud it is on their own machine, that one is nobody else's to turn up
        public SoundboardViewModel? Soundboard { get; }
        public bool HasSoundboard => Soundboard != null;
        private SoundboardScreenViewModel? _soundboardScreen;

        private readonly Dictionary<string, CharacterSheetViewModel> _openSheets = new();

        public SideMenuViewModel SideMenu { get; }
        public event Action<TradeOfferMessage>? IncomingTradeRequested;
        public event Action<ItemPopupRequest>? ItemPopupRequested;

        private string _notification = "";
        public string Notification
        {
            get => _notification;
            set => this.RaiseAndSetIfChanged(ref _notification, value);
        }

        private bool _notificationVisible;
        public bool NotificationVisible
        {
            get => _notificationVisible;
            set => this.RaiseAndSetIfChanged(ref _notificationVisible, value);
        }

        private CancellationTokenSource? _notificationCts;

        public void ShowNotification(string text)
        {
            Notification = text;
            NotificationVisible = true;
            // Otherwise the old timer sleeps its four seconds and wakes up to do nothing.
            _notificationCts?.Cancel();
            _notificationCts?.Dispose();
            _notificationCts = new CancellationTokenSource();
            _ = HideNotificationLaterAsync(_notificationCts.Token);
        }

        private async Task HideNotificationLaterAsync(CancellationToken ct)
        {
            try { await Task.Delay(4000, ct); }
            catch (OperationCanceledException) { return; }
            NotificationVisible = false;
        }

        private string _rollToast = "";
        public string RollToast
        {
            get => _rollToast;
            set => this.RaiseAndSetIfChanged(ref _rollToast, value);
        }

        private bool _rollToastVisible;
        public bool RollToastVisible
        {
            get => _rollToastVisible;
            set => this.RaiseAndSetIfChanged(ref _rollToastVisible, value);
        }

        private CancellationTokenSource? _rollToastCts;

        public void ShowRollToast(string text)
        {
            RollToast = text;
            RollToastVisible = true;
            _rollToastCts?.Cancel();
            _rollToastCts?.Dispose();
            _rollToastCts = new CancellationTokenSource();
            _ = HideRollToastLaterAsync(_rollToastCts.Token);
        }

        private async Task HideRollToastLaterAsync(CancellationToken ct)
        {
            try { await Task.Delay(6000, ct); }
            catch (OperationCanceledException) { return; }
            RollToastVisible = false;
        }

        private void RelayRoll(string text, bool secret)
        {
            ShowRollToast(text);
            _ = ChatWidget.PostRollAsync(text, secret || (App.PM?.HideRolls ?? false));
            if (App.PM != null) _ = App.PM.ComController.LogRollAsync(App.PM.GetUID(), App.PM.GetUsername(), text);
        }

        private string _currentHandoutId = "";

        private Bitmap? _activeHandoutImage;
        public Bitmap? ActiveHandoutImage
        {
            get => _activeHandoutImage;
            set => this.RaiseAndSetIfChanged(ref _activeHandoutImage, value);
        }

        private bool _handoutVisible;
        public bool HandoutVisible
        {
            get => _handoutVisible;
            set => this.RaiseAndSetIfChanged(ref _handoutVisible, value);
        }

        private string _handoutName = "";
        public string HandoutName
        {
            get => _handoutName;
            set => this.RaiseAndSetIfChanged(ref _handoutName, value);
        }

        private bool _owedSaveVisible;
        public bool OwedSaveVisible
        {
            get => _owedSaveVisible;
            set => this.RaiseAndSetIfChanged(ref _owedSaveVisible, value);
        }

        private string _owedSaveLabel = "";
        public string OwedSaveLabel
        {
            get => _owedSaveLabel;
            set => this.RaiseAndSetIfChanged(ref _owedSaveLabel, value);
        }

        public ReactiveCommand<Unit, Unit> RollOwedSaveCommand { get; }
        public ReactiveCommand<Unit, Unit> DismissOwedSaveCommand { get; }
        public event Func<string, string, Task<bool>>? ConfirmLeaveAsync;

        private async Task LeaveCampaignAsync()
        {
            var message = CurrentRole == UserRole.Dm
                ? "Leaving shuts the table down, every player loses their connection and the map stops being shared. Nothing is deleted and you can host it again from the start screen."
                : "Leaving drops your connection to the table. Nothing is deleted and you can rejoin from the start screen.";

            if (ConfirmLeaveAsync != null && !await ConfirmLeaveAsync("Leave " + CampaignName + "?", message)) return;
            await _mainWVM.LeaveCampaignAsync();
        }

        public event Func<string, string, Task<bool>>? ConfirmDeleteAsync;

        private async Task DeleteCampaignAsync()
        {
            if (CurrentRole != UserRole.Dm) return;

            var campaignId = App.PM.GetCampaignId();
            if (string.IsNullOrEmpty(campaignId)) return;

            var message = "This wipes " + CampaignName + " and everything in it, the characters, the maps, the notes, the handouts, the chat and the rolls. "
                + "The rulebook and anything you have imported stay where they are, and every other campaign is untouched. There is no undo, so take a backup first if you are not sure.";

            if (ConfirmDeleteAsync == null || !await ConfirmDeleteAsync("Delete " + CampaignName + "?", message)) return;

            try
            {
                if (!await App.PM.DeleteCampaignAsync(campaignId))
                {
                    ShowNotification("Only the DM who owns this campaign can delete it.");
                    return;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[Delete] removing the campaign failed", ex);
                ShowNotification("Deleting the campaign failed, nothing was removed.");
                return;
            }

            await _mainWVM.LeaveCampaignAsync();
        }

        public async Task CloseMapSessionAsync()
        {
            var session = _mapSession;
            if (session == null) return;

            if (session.IsBroadcasting && CurrentRole == UserRole.Dm)
            {
                try { await App.PM.ComController.DeactivateMapAsync(); }
                catch (Exception ex) { ErrorLog.Log("[Map] telling the players the map closed failed", ex); }
                session.IsBroadcasting = false;
            }

            session.Canvas.Detach();
            session.Detach();
            _broadcastSub?.Dispose();
            _broadcastSub = null;
            _mapHub.ClearActiveMap();
            _mapSession = null;
            this.RaisePropertyChanged(nameof(LiveSession));
            OwedSaveVisible = false;
            if (ReferenceEquals(CurrentContent, session)) CurrentContent = _mapHub;
            ReturnToFightVisible = false;
        }

        public ReactiveCommand<Unit, Unit> DismissHandoutCommand { get; }

        public GlobalSearchViewModel Search { get; }

        private bool _searchOverlayVisible;
        public bool SearchOverlayVisible
        {
            get => _searchOverlayVisible;
            set => this.RaiseAndSetIfChanged(ref _searchOverlayVisible, value);
        }

        public ReactiveCommand<Unit, Unit> DismissSearchCommand { get; }

        // The view calls this off Ctrl+Q, the search itself scopes results to the role so a player only ever sees their own stuff.
        public void OpenSearch()
        {
            Search.Reset();
            SearchOverlayVisible = true;
        }

        private async void OnSearchResultChosen(SearchResultRow row)
        {
            try
            {
                SearchOverlayVisible = false;
                if (row == null) return;
                switch (row.Type)
                {
                    case "npc":
                        await OpenNpcSheetAsync(row.Id);
                        break;
                    case "character":
                        await OpenCharacterSheetAsync(row.Id);
                        break;
                    case "note":
                        CurrentContent = _notes;
                        _notes.SelectPageById(row.Id);
                        break;
                    case "codex":
                        if (_codex != null)
                        {
                            CurrentContent = _codex;
                            await _codex.LoadAsync();
                            _codex.SelectedTabIndex = 0;
                            _codex.Chapters.SelectPageById(row.Id);
                        }
                        break;
                    case "map":
                        if (CurrentRole == UserRole.Dm)
                            await OpenMapSessionAsync(row.Id, isBroadcasting: false);
                        else
                        {
                            await _sharedMapHub.LoadSharedAsync();
                            CurrentContent = _sharedMapHub;
                        }
                        break;
                    case "handout":
                        if (_handouts != null)
                        {
                            CurrentContent = _handouts;
                            await _handouts.LoadAsync();
                        }
                        break;
                    case "encounter":
                        if (_encounters != null)
                        {
                            CurrentContent = _encounters;
                            await _encounters.LoadAsync();
                        }
                        break;
                    case "calendar":
                        if (_calendar != null)
                        {
                            CurrentContent = _calendar;
                            await _calendar.LoadAsync();
                            var ev = _calendar.Events.FirstOrDefault(e => e.Id == row.Id);
                            if (ev != null) _calendar.Selected = ev;
                        }
                        break;
                    case "timeline":
                        if (_timeline != null)
                        {
                            CurrentContent = _timeline;
                            await _timeline.LoadAsync();
                            var tev = _timeline.Events.FirstOrDefault(e => e.Id == row.Id);
                            if (tev != null) _timeline.Selected = tev;
                        }
                        break;
                    case "item":
                        CurrentContent = _compendium;
                        _compendium.SelectedCategory = "Items";
                        _compendium.SearchText = row.Title;
                        break;
                    case "spell":
                        CurrentContent = _compendium;
                        _compendium.SelectedCategory = "Spells";
                        _compendium.SearchText = row.Title;
                        break;
                    case "mindmap":
                        CurrentContent = _mindmap;
                        await _mindmap.LoadAsync();
                        await _mindmap.OpenMapByIdAsync(row.Id);
                        break;
                }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnSearchResultChosen", ex); }
        }

        private string ResolveMemberName(string userId)
        {
            var m = App.PM.ComController.Members.FirstOrDefault(x => string.Equals(x.UserId, userId, StringComparison.Ordinal));
            return m?.Username ?? "A player";
        }

        public CampaignViewModel(MainWindowViewModel mwvm, UserRole role)
        {
            _mainWVM = mwvm;
            ChatWidget = new ChatViewModel();
            CurrentRole = role;
            CampaignName = App.PM.GetCampaignName();

            _characters = new CharacterDashboardViewModel(this);
            var isDm = role == UserRole.Dm;

            QuickNotesPanel = new QuickNotesPanelViewModel(
                App.PM.NoteRepo,
                App.PM.GetCampaignId(),
                App.PM.GetUID());

            QuickNotesPanel.Broadcast = async (page, changeType) =>
            {
                if (changeType == "removed") await App.PM.ComController.NotifyPageChangedAsync(page.Id, changeType);
                else await App.PM.ComController.NotifyPageChangedAsync(page, changeType);
            };

            QuickNotesWidget = new QuickNotesWidgetViewModel(QuickNotesPanel);

            _notes = new NotesViewModel(
                App.PM.NoteRepo,
                App.PM.GetCampaignId(),
                App.PM.GetUID(),
                isDm,
                QuickNotesPanel);

            if (isDm)
            {
                _codex = new CodexViewModel(
                    App.PM.NoteRepo,
                    App.PM.DbManager,
                    App.PM.GetCampaignId(),
                    App.PM.GetUID());
                _codex.Npcs.OpenNpcCreatorRequested += OpenNpcCreation;
                _codex.Npcs.OpenNpcRequested += async id => await OpenNpcEditorAsync(id);
                _codex.Npcs.ViewNpcRequested += async id => await OpenNpcSheetAsync(id);
                _codex.Npcs.OpenMonsterEditorRequested += async withImport => await OpenMonsterEditorAsync(withImport);
            }

            _compendium = new CompendiumViewModel(App.PM.DbManager, App.PM.GetCampaignId(), isDm);
            if (isDm) _templateEditor = new TemplateEditorViewModel();

            if (isDm) _dmCommands = new DmCommandsViewModel();

            if (isDm) _encounters = new EncounterBuilderViewModel();
            if (isDm) _encounterRunner = new EncounterRunnerViewModel();
            if (isDm) _handouts = new HandoutsViewModel();
            if (isDm) _sessionLog = new SessionLogViewModel();
            if (isDm) _randomTables = new RandomTablesViewModel { RollToChat = RelayRoll };
            if (isDm) _calendar = new CalendarViewModel();
            if (isDm) _timeline = new TimelineViewModel();
            Soundboard = new SoundboardViewModel(isDm);
            _soundboardScreen = new SoundboardScreenViewModel(Soundboard);

            _mapHub = new MapHubViewModel();
            _mapHub.OpenSessionRequested += async (mapId, broadcasting, imagePath) =>
            {
                await OpenMapSessionAsync(mapId, broadcasting, imagePath);
            };

            _sharedMapHub = new MapHubViewModel(isSharedView: true);
            _sharedMapHub.OpenSharedSessionRequested += async (mapId, gridKind, scale) =>
            {
                var gk = Enum.TryParse<GridKind>(gridKind, out var parsed) ? parsed : GridKind.Squares;
                await OpenSharedMapSessionAsync(mapId, gk, scale);
            };

            RefResolver.Init(
                App.PM.DbManager,
                App.PM.NoteRepo,
                App.PM.GetCampaignId(),
                App.PM.GetUID());

            RefResolver.NavigateRequested += OnRefNavigate;

            WireMapEvents();
            WireNoteEvents();
            WireGameDataEvents();

            DismissHandoutCommand = ReactiveCommand.Create(() => { HandoutVisible = false; });
            RollOwedSaveCommand = ReactiveCommand.Create(() =>
            {
                OwedSaveVisible = false;
                _mapSession?.RollOwedSaveFromPrompt?.Invoke();
            });
            DismissOwedSaveCommand = ReactiveCommand.Create(() => { OwedSaveVisible = false; });

            ReturnToFightCommand = ReactiveCommand.Create(() => { if (_mapSession != null) CurrentContent = _mapSession; });

            Search = new GlobalSearchViewModel { IsDm = isDm, UserId = App.PM.GetUID() };
            Search.ResultChosen += OnSearchResultChosen;
            DismissSearchCommand = ReactiveCommand.Create(() => { SearchOverlayVisible = false; });

            WireHandoutEvents();
            WirePresenceEvents();
            WireMindmapEvents();
            WireConnectionEvents();

            NavItem.NavError = ShowNotification;

            NavItems = new ObservableCollection<NavItem>(
                BuildNavItems().Where(n => n.IsVisibleFor(role)));

            _settings.IsDm = isDm;
            _settings.DeleteCampaignRequested += DeleteCampaignAsync;
            _dmScreen.RollToChat = RelayRoll;
            SideMenu = new SideMenuViewModel(CampaignName, NavItems, OpenSearch, () => CurrentContent = _settings, LeaveCampaignAsync);

            _currentCharacterSub = App.PM.CurrentCharacterService
                .WhenAnyValue(s => s.Current)
                .Subscribe(_ => OnCurrentCharacterChanged());

            // Everyone lands on the dashboard, a player with no character yet gets bounced to Characters once the open pipeline resolves who they are
            CurrentContent = _dashboard;
            _ = _dashboard.LoadAsync();

            var landing = NavItems.FirstOrDefault(n => n.IsVisibleFor(CurrentRole) && n.Label == "Dashboard");
            if (landing != null) SideMenu.Highlight(landing);
        }

        private void GoToCharactersScreen()
        {
            CurrentContent = _characters;
            var nav = NavItems.FirstOrDefault(n => n.IsVisibleFor(CurrentRole) && (n.Label == "Characters" || n.Label == "My Character"));
            if (nav != null) SideMenu.Highlight(nav);
        }

        private Action<MapActivatedMessage>? _onMapActivated;
        private Action? _onMapDeactivated;
        private Action<NotePresenceMessage>? _onNotePresence;
        private Action<NotePageChangePayload>? _onNotePageChanged;
        private Action<NoteUpdatePayload>? _onNoteUpdate;
        private Action? _onNoteReconnected;
        private Action<NotePage>? _onNotePageInvited;
        private Action<string>? _onNotePageRevoked;
        private Action<string, string>? _onGameDataChanged;
        private Action<TradeOfferMessage>? _onTradeOffered;
        private Action<HandoutRevealedMessage>? _onHandoutRevealed;
        private Action<string>? _onHandoutHidden;
        private Action<string, string>? _onPlayerJoined;
        private Action<string>? _onPlayerLeft;
        private Action<MindmapSyncPayload>? _onMindmapSynced;
        private Action<string>? _onMindmapRevoked;
        private Action<MindmapNodeOp>? _onMindNodeUpserted;
        private Action<MindmapNodeMoveOp>? _onMindNodeMoved;
        private Action<MindmapNodeDeleteOp>? _onMindNodeDeleted;
        private Action<MindmapLinkOp>? _onMindLinkUpserted;
        private Action<MindmapLinkDeleteOp>? _onMindLinkDeleted;
        private Action? _onReconnecting;
        private Action? _onReconnected;
        private Action? _onConnectionLost;
        private IDisposable? _currentCharacterSub;
        private IDisposable? _broadcastSub;

        public void Detach()
        {
            var com = App.PM?.ComController;
            if (com != null)
            {
                com.OnMapActivated -= _onMapActivated;
                com.OnMapDeactivated -= _onMapDeactivated;
                com.OnNotePresence -= _onNotePresence;
                com.OnNotePageChanged -= _onNotePageChanged;
                com.OnNoteUpdate -= _onNoteUpdate;
                com.OnNoteReconnected -= _onNoteReconnected;
                com.OnNotePageInvited -= _onNotePageInvited;
                com.OnNotePageRevoked -= _onNotePageRevoked;
                com.OnTradeOffered -= _onTradeOffered;
                com.OnHandoutRevealed -= _onHandoutRevealed;
                com.OnHandoutHidden -= _onHandoutHidden;
                com.OnPlayerJoined -= _onPlayerJoined;
                com.OnPlayerLeft -= _onPlayerLeft;
                com.OnMindmapSynced -= _onMindmapSynced;
                com.OnMindmapRevoked -= _onMindmapRevoked;
                com.OnMindNodeUpserted -= _onMindNodeUpserted;
                com.OnMindNodeMoved -= _onMindNodeMoved;
                com.OnMindNodeDeleted -= _onMindNodeDeleted;
                com.OnMindLinkUpserted -= _onMindLinkUpserted;
                com.OnMindLinkDeleted -= _onMindLinkDeleted;
                com.OnReconnecting -= _onReconnecting;
                com.OnReconnected -= _onReconnected;
                com.OnConnectionLost -= _onConnectionLost;
            }
            if (App.PM != null) App.PM.OnGameDataChanged -= _onGameDataChanged;
            RefResolver.NavigateRequested -= OnRefNavigate;
            _currentCharacterSub?.Dispose();
            _currentCharacterSub = null;
            _broadcastSub?.Dispose();
            _broadcastSub = null;
            _mapSession?.Canvas.Detach();
            _mapSession?.Detach();
            _sharedSession?.Canvas.Detach();
            _sharedSession?.Detach();
        }

        private void WireMapEvents()
        {
            _onMapActivated = async msg =>
            {
                var mapId = msg.MapId;
                string? localPath;
                if (CurrentRole == UserRole.Dm)
                {
                    localPath = _mapHub.Maps.FirstOrDefault(m => m.Id == mapId)?.ImagePath;
                }
                else
                {
                    localPath = null;
                    var bytes = await App.PM.ComController.FetchMapImageAsync(mapId);
                    if (bytes != null)
                    {
                        var cacheDir = Path.Combine(
                            GlobalVariables.AppDataLocal,
                            "cache", "maps");
                        Directory.CreateDirectory(cacheDir);
                        localPath = Path.Combine(cacheDir, mapId + ".png");
                        await File.WriteAllBytesAsync(localPath, bytes);
                    }
                }

                var gridKind = Enum.TryParse<GridKind>(msg.GridKind, out var gk)
                    ? gk
                    : GridKind.Squares;
                await OpenMapSessionAsync(mapId, isBroadcasting: true, imagePath: localPath, gridKind: gridKind, scale: msg.Scale, mapWidth: msg.Width, mapHeight: msg.Height,
                    gridOffsetX: msg.GridOffsetX, gridOffsetY: msg.GridOffsetY);
            };
            App.PM.ComController.OnMapActivated += _onMapActivated;

            _onMapDeactivated = () =>
            {
                if (CurrentRole == UserRole.Dm) return;
                if (_mapSession == null || !_mapSession.IsBroadcasting) return;

                bool wasWatching = ReferenceEquals(CurrentContent, _mapSession);
                _mapSession.Canvas.Detach();
                _mapSession.Detach();
                _mapSession = null;
                if (wasWatching)
                {
                    CurrentContent = _dashboard;
                    _ = _dashboard.LoadAsync();
                }
                RefreshReturnToFight();
            };
            App.PM.ComController.OnMapDeactivated += _onMapDeactivated;
        }

        private void WireNoteEvents()
        {
            _onNotePresence = p => _notes.ApplyPresence(p);
            App.PM.ComController.OnNotePresence += _onNotePresence;

            _onNotePageChanged = msg =>
            {
                _notes.ApplyRemoteChange(msg.PageId, msg.ChangeType, msg.Page);
                QuickNotesPanel.ApplyRemoteChange(msg.PageId, msg.ChangeType, msg.Page);
                if (msg.Page != null && msg.Page.Scope == "quicknote")
                    RefResolver.Invalidate("quicknote", msg.Page.Slug ?? "");
            };
            App.PM.ComController.OnNotePageChanged += _onNotePageChanged;

            _onNoteUpdate = msg => _ = _notes.ApplyRemoteNoteUpdateAsync(msg);
            App.PM.ComController.OnNoteUpdate += _onNoteUpdate;

            _onNoteReconnected = () => _ = _notes.ResyncAfterReconnectAsync();
            App.PM.ComController.OnNoteReconnected += _onNoteReconnected;

            _onNotePageInvited = async page =>
            {
                await App.PM.NoteRepo.SaveInvitedPageAsync(page, App.PM.GetUID());
                Dispatcher.UIThread.Post(() => _notes.ApplyRemoteChange(page.Id, "added", page));
            };
            App.PM.ComController.OnNotePageInvited += _onNotePageInvited;

            _onNotePageRevoked = pageId =>
                _notes.ApplyRemoteChange(pageId, "removed", null);
            App.PM.ComController.OnNotePageRevoked += _onNotePageRevoked;
        }

        private void WireGameDataEvents()
        {
            _onGameDataChanged = async (entityType, entityId) =>
            {
                if (entityType == "ItemInstance")
                {
                    foreach (var openSheet in _openSheets.Values) await openSheet.ReloadInventoryAsync();
                    return;
                }
                if (entityType != "Character") return;
                await _characters.LoadAsync();
                await _dashboard.LoadAsync();

                bool syncCombat = CurrentRole == UserRole.Dm && _mapSession != null && _mapSession.Initiative.CombatActive;

                if (_openSheets.TryGetValue(entityId, out var sheet))
                {
                    var rt = await App.PM.LoadCharacterByIdAsync(entityId);
                    if (rt != null)
                    {
                        sheet.ApplyRemoteUpdate(rt);
                        if (syncCombat) _mapSession!.ApplyCharacterVitals(rt);
                    }
                }
                else if (syncCombat)
                {
                    var rt = await App.PM.LoadCharacterByIdAsync(entityId);
                    if (rt != null) _mapSession!.ApplyCharacterVitals(rt);
                }
            };
            App.PM.OnGameDataChanged += _onGameDataChanged;

            _onTradeOffered = offer =>
            {
                if (App.PM != null && offer.To.UserId == App.PM.GetUID())
                    IncomingTradeRequested?.Invoke(offer);
            };
            App.PM.ComController.OnTradeOffered += _onTradeOffered;
        }

        private void WireHandoutEvents()
        {
            _onHandoutRevealed = async msg =>
            {
                try
                {
                    var bytes = await App.PM.ComController.FetchHandoutAsync(msg.HandoutId);
                    if (bytes == null) return;
                    using var ms = new MemoryStream(bytes);
                    var bmp = new Bitmap(ms);
                    Dispatcher.UIThread.Post(() =>
                    {
                        _currentHandoutId = msg.HandoutId;
                        ActiveHandoutImage = bmp;
                        HandoutName = msg.Name;
                        HandoutVisible = true;
                    });
                }
                catch (Exception ex)
                {
                    ErrorLog.Log($"[Handout] reveal failed", ex);
                }
            };
            App.PM.ComController.OnHandoutRevealed += _onHandoutRevealed;

            _onHandoutHidden = _ =>
                Dispatcher.UIThread.Post(() => HandoutVisible = false);
            App.PM.ComController.OnHandoutHidden += _onHandoutHidden;
        }

        private void WirePresenceEvents()
        {
            _onPlayerJoined = async (uid, uname) =>
            {
                ShowNotification(uname + " connected.");
                Soundboard?.RefreshPlayers();
                await _dashboard.LoadAsync();

                if (Soundboard != null)
                {
                    await Task.Delay(1500);
                    await Soundboard.ResyncLibraryAsync();
                }

                var session = _mapSession;
                if (CurrentRole == UserRole.Dm && session is { IsBroadcasting: true })
                {
                    await Task.Delay(1500);
                    if (session.IsBroadcasting) await session.ResyncAsync(false);
                    await Task.Delay(3000);
                    if (session.IsBroadcasting) await session.ResyncAsync(false);
                }

                if (CurrentRole == UserRole.Dm)
                {
                    var handout = App.PM.ComController.ActiveHandout;
                    if (handout != null)
                    {
                        await Task.Delay(1500);
                        await App.PM.ComController.RevealHandoutAsync(handout with { TargetUserId = uid });
                    }
                }

                await Task.Delay(1500);
                await _mindmap.RepushSharedMapsToAsync(uid);
            };
            App.PM.ComController.OnPlayerJoined += _onPlayerJoined;

            _onPlayerLeft = async uid =>
            {
                ShowNotification(ResolveMemberName(uid) + " lost connection.");
                Soundboard?.RefreshPlayers();
                await _dashboard.LoadAsync();
            };
            App.PM.ComController.OnPlayerLeft += _onPlayerLeft;
        }

        private void WireMindmapEvents()
        {
            _onMindmapSynced = payload => _ = _mindmap.ApplyRemoteSync(payload);
            _onMindmapRevoked = mapId => _mindmap.RemoveRevokedMap(mapId);
            _onMindNodeUpserted = op => _ = _mindmap.ApplyNodeUpsert(op);
            _onMindNodeMoved = op => _ = _mindmap.ApplyNodeMove(op);
            _onMindNodeDeleted = op => _ = _mindmap.ApplyNodeDelete(op);
            _onMindLinkUpserted = op => _ = _mindmap.ApplyLinkUpsert(op);
            _onMindLinkDeleted = op => _ = _mindmap.ApplyLinkDelete(op);

            App.PM.ComController.OnMindmapSynced += _onMindmapSynced;
            App.PM.ComController.OnMindmapRevoked += _onMindmapRevoked;
            App.PM.ComController.OnMindNodeUpserted += _onMindNodeUpserted;
            App.PM.ComController.OnMindNodeMoved += _onMindNodeMoved;
            App.PM.ComController.OnMindNodeDeleted += _onMindNodeDeleted;
            App.PM.ComController.OnMindLinkUpserted += _onMindLinkUpserted;
            App.PM.ComController.OnMindLinkDeleted += _onMindLinkDeleted;
        }

        private void WireConnectionEvents()
        {
            _onReconnecting = () =>
                ShowNotification("Connection to the host dropped, reconnecting...");

            _onReconnected = () =>
            {
                ShowNotification("Reconnected to the host.");
                _ = ResyncOpenMapAsync();
            };

            _onConnectionLost = () =>
                ShowNotification("Lost connection to the host. You may need to rejoin.");

            App.PM.ComController.OnReconnecting += _onReconnecting;
            App.PM.ComController.OnReconnected += _onReconnected;
            App.PM.ComController.OnConnectionLost += _onConnectionLost;
        }

        private List<NavItem> BuildNavItems() => new()
        {
            new("Dashboard",
                new[] { UserRole.Dm, UserRole.Player, UserRole.Spectator },
                async () => {
                    CurrentContent = _dashboard;
                    await _dashboard.LoadAsync();
                }),

            new("Characters",
                new[] { UserRole.Dm, UserRole.Player },
                async () => {
                    CurrentContent = _characters;
                    await _characters.LoadAsync();
                }),

            new("My Character",
                new[] { UserRole.Player },
                OpenMyCharacter),

            new("Maps",
                new[] { UserRole.Player, UserRole.Spectator },
                async () => {
                    await _sharedMapHub.LoadSharedAsync();
                    CurrentContent = _sharedMapHub;
                }),

            new("Maps",
                new[] { UserRole.Dm },
                async () => { CurrentContent = _mapHub; await _mapHub.LoadAsync(); }),

            new("Encounters",
                new[] { UserRole.Dm },
                async () => {
                    if (_encounters != null)
                    {
                        CurrentContent = _encounters;
                        await _encounters.LoadAsync();
                    }
                }),

            new("Run Encounter",
                new[] { UserRole.Dm },
                async () => {
                    if (_encounterRunner != null)
                    {
                        CurrentContent = _encounterRunner;
                        await _encounterRunner.LoadAsync();
                    }
                }),

            new("Notes",
                new[] { UserRole.Dm, UserRole.Player },
                async () => { CurrentContent = _notes; if (_notes.MyNotes.Count == 0 && _notes.SharedNotes.Count == 0) await _notes.LoadAsync(); }),

            new("Mindmap",
                new[] { UserRole.Dm, UserRole.Player },
                async () => { CurrentContent = _mindmap; await _mindmap.LoadAsync(); }),

            new("Calendar",
                new[] { UserRole.Dm },
                async () => {
                    if (_calendar != null)
                    {
                        CurrentContent = _calendar;
                        await _calendar.LoadAsync();
                    }
                }),

            new("Timeline",
                new[] { UserRole.Dm },
                async () => {
                    if (_timeline != null)
                    {
                        CurrentContent = _timeline;
                        await _timeline.LoadAsync();
                    }
                }),

            new("Handouts",
                new[] { UserRole.Dm },
                async () => {
                    if (_handouts != null)
                    {
                        CurrentContent = _handouts;
                        await _handouts.LoadAsync();
                    }
                }),

            new("Compendium",
                new[] { UserRole.Dm, UserRole.Player },
                async () => { CurrentContent = _compendium; if (_compendium.Entries.Count == 0) await _compendium.LoadAsync(); }),

            new("Codex", // Not sure if "Codex" is the best name
                new[] { UserRole.Dm },
                async () => { if (_codex != null) { CurrentContent = _codex; await _codex.LoadAsync(); } }),

            new("DM Screen",
                new[] { UserRole.Dm },
                () => CurrentContent = _dmScreen),

            new("Random Tables",
                new[] { UserRole.Dm },
                async () => {
                    if (_randomTables != null)
                    {
                        CurrentContent = _randomTables;
                        await _randomTables.LoadAsync();
                    }
                }),

            new("Session Log",
                new[] { UserRole.Dm },
                async () => {
                    if (_sessionLog != null)
                    {
                        CurrentContent = _sessionLog;
                        await _sessionLog.LoadAsync();
                    }
                }),

            new("Soundboard",
                new[] { UserRole.Dm, UserRole.Player },
                () => {
                    if (_soundboardScreen != null)
                    {
                        if (Soundboard?.IsDm == true) Soundboard.RefreshPlayers();
                        CurrentContent = _soundboardScreen;
                    }
                }),

            new("DM Commands",
                new[] { UserRole.Dm },
                async () => {
                    if (_dmCommands != null)
                    {
                        CurrentContent = _dmCommands;
                        await _dmCommands.LoadAsync();
                    }
                }),

            new("Templates",
                new[] { UserRole.Dm },
                async () => {
                    if (_templateEditor != null)
                    {
                        CurrentContent = _templateEditor;
                        await _templateEditor.LoadAsync();
                    }
                }),
        };

        // Sequential on purpose so each step gets its own progress line
        public async Task InitializeAsync(Action<string, double>? progress = null)
        {
            async Task step(string message, double fraction, Func<Task> work)
            {
                progress?.Invoke(message, fraction);
                await Task.Yield();
                try { await work(); }
                catch (Exception ex) { ErrorLog.Log($"[CampaignInit] {message} failed", ex); }
            }

            await step("Pinning up the quick notes", 0.84, () => QuickNotesPanel.LoadAsync());
            await step("Sorting the campaign notes", 0.88, () => _notes.LoadAsync());

            if (_codex != null)
                await step("Cataloguing the codex", 0.91, () => _codex.LoadAsync());

            await step("Stocking the compendium shelves", 0.94, () => _compendium.LoadAsync());

            if (CurrentRole == UserRole.Dm)
                await step("Unrolling the battle maps", 0.97, () => _mapHub.LoadAsync());

            await step("Gathering the party", 0.99, () => _characters.LoadAsync());

            if (CurrentRole == UserRole.Player)
            {
                await step("Finding your hero", 0.995, () => App.PM.ResolveCurrentUserCharacterAsync());
                if (App.PM.CurrentCharacterService.Current == null)
                    await Dispatcher.UIThread.InvokeAsync(GoToCharactersScreen);
            }

            await step("Reading the rulebook", 0.999, async () =>
            {
                await App.PM.EnsureRulesLoadedAsync();
                var gaps = App.PM.Rules?.DefaultedSections;
                if (gaps != null && gaps.Count > 0)
                    ShowNotification("Template does not define " + string.Join(", ", gaps) + ", running on built in defaults for those.");
            });
        }

        public void OpenCharacterCreation()
        {
            CurrentContent = new CharacterCreationViewModel(this);
        }

        public void OpenNpcCreation()
        {
            CurrentContent = CharacterCreationViewModel.ForNpc(this);
        }

        public async Task OpenMonsterEditorAsync(bool withImport)
        {
            if (_templateEditor == null) return;
            CurrentContent = _templateEditor;
            await _templateEditor.OpenMonstersAsync(withImport);
        }

        public void OpenCharacterEditor(CharacterRuntime runtime)
        {
            if (runtime == null) return;
            CurrentContent = new CharacterCreationViewModel(this, runtime);
        }

        public async Task FinishCharacterEditAsync(CharacterRuntime runtime)
        {
            if (runtime != null && _openSheets.ContainsKey(runtime.Id))
                _openSheets.Remove(runtime.Id);

            await _characters.LoadAsync();

            var fresh = await App.PM.LoadCharacterByIdAsync(runtime!.Id);
            if (fresh != null) OpenCharacterSheet(fresh);
            else CurrentContent = _characters;
        }

        public async Task FinishCharacterCreationAsync(string? newCharacterId, bool assignedToMe)
        {
            await _characters.LoadAsync();

            if (!string.IsNullOrEmpty(newCharacterId))
            {
                if (assignedToMe)
                {
                    var rt = await App.PM.ResolveCurrentUserCharacterAsync(newCharacterId);
                    if (rt != null) { OpenCharacterSheet(rt); return; }
                }

                await OpenCharacterSheetAsync(newCharacterId!);
                return;
            }

            CurrentContent = _characters;
        }

        public void CancelCharacterCreation() => CurrentContent = _characters;

        public async Task FinishNpcCreationAsync()
        {
            if (_codex != null)
            {
                await _codex.Npcs.LoadAsync();
                CurrentContent = _codex;
            }
            else
            {
                CurrentContent = _characters;
            }
        }

        public async Task OpenNpcEditorAsync(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || App.PM == null) return;
            var rt = await App.PM.LoadCharacterByIdAsync(npcId);
            if (rt != null) OpenCharacterEditor(rt);
        }

        public async Task OpenNpcSheetAsync(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || App.PM == null) return;
            var rt = await App.PM.LoadCharacterByIdAsync(npcId);
            if (rt != null) OpenCharacterSheet(rt);
        }

        public async Task OpenCharacterSheetAsync(string characterId)
        {
            if (string.IsNullOrEmpty(characterId) || App.PM == null) return;

            if (_openSheets.TryGetValue(characterId, out var cached))
            {
                CurrentContent = cached;
                return;
            }

            // Loads fresh from the db, and LoadCharacterByIdAsync also updates CurrentCharacterService along the way, fine for my own sheet, a dm peeking at somebody else's probably wants a non mutating variant someday.
            var runtime = await App.PM.LoadCharacterByIdAsync(characterId); // FIX
            if (runtime == null)
            {
                Debug.WriteLine($"[Campaign] Could not load character {characterId}");
                return;
            }

            var vm = BuildSheetVm(runtime);
            _openSheets[characterId] = vm;
            CurrentContent = vm;
        }

        private async void OnRefNavigate(string type, string id)
        {
            try
            {
                switch (type)
                {
                    case "npc":
                        {
                            await using var conn = await App.PM.DbManager.OpenAsync();
                            await using var cmd = conn.CreateCommand();
                            cmd.CommandText = """
                                SELECT Id FROM Characters
                                WHERE CampaignId = $cid AND CharacterKind = 'npc' AND Slug = $slug
                                LIMIT 1
                            """;
                            cmd.Parameters.AddWithValue("$cid", App.PM.GetCampaignId());
                            cmd.Parameters.AddWithValue("$slug", id);
                            var charId = await cmd.ExecuteScalarAsync() as string;
                            if (!string.IsNullOrEmpty(charId))
                                await OpenCharacterSheetAsync(charId);
                            break;
                        }
                    case "character":
                        {
                            await using var conn = await App.PM.DbManager.OpenAsync();
                            await using var cmd = conn.CreateCommand();
                            cmd.CommandText = """
                                SELECT Id FROM Characters
                                WHERE CampaignId = $cid AND CharacterKind = 'pc' AND Slug = $slug
                                LIMIT 1
                            """;
                            cmd.Parameters.AddWithValue("$cid", App.PM.GetCampaignId());
                            cmd.Parameters.AddWithValue("$slug", id);
                            var charId = await cmd.ExecuteScalarAsync() as string;
                            if (!string.IsNullOrEmpty(charId))
                                await OpenCharacterSheetAsync(charId);
                            break;
                        }
                    case "note":
                        {
                            CurrentContent = _notes;
                            _notes.SelectPageById(id);
                            break;
                        }
                    case "codex":
                        {
                            if (_codex != null)
                            {
                                CurrentContent = _codex;
                                await _codex.LoadAsync();
                                _codex.SelectedTabIndex = 0;
                                _codex.Chapters.SelectPageById(id);
                            }
                            break;
                        }
                    case "item":
                        {
                            await using var conn = await App.PM.DbManager.OpenAsync();
                            await using var cmd = conn.CreateCommand();
                            cmd.CommandText = $"SELECT Id, Name, ItemType, {CatalogResolver.ResolvedJsonSql("Items", "Items")} FROM Items WHERE Slug = $slug LIMIT 1";
                            CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                            cmd.Parameters.AddWithValue("$slug", id);
                            await using var r = await cmd.ExecuteReaderAsync();
                            if (await r.ReadAsync())
                                ItemPopupRequested?.Invoke(new ItemPopupRequest(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? "{}" : r.GetString(3)));
                            break;
                        }
                    case "mindmap":
                        {
                            CurrentContent = _mindmap;
                            await _mindmap.LoadAsync();
                            await _mindmap.SelectNodeBySlugAsync(id);
                            break;
                        }
                    case "quicknote":
                        // Quicknote clicks do nothing on purpose, the rendered text already shows the content and editing happens through the markdown text attribute.
                        break;
                }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnRefNavigate", ex); }
        }


        public void OpenCharacterSheet(CharacterRuntime runtime)
        {
            if (runtime == null) return;

            if (!_openSheets.TryGetValue(runtime.Id, out var vm))
            {
                vm = BuildSheetVm(runtime);
                _openSheets[runtime.Id] = vm;
            }

            CurrentContent = vm;
        }

        private CharacterSheetViewModel BuildSheetVm(CharacterRuntime runtime)
        {
            var vm = new CharacterSheetViewModel(runtime);
            vm.CanManageToken = CurrentRole == UserRole.Dm;

            vm.DmLogPing += async msg =>
            {
                if (string.IsNullOrWhiteSpace(msg)) return;
                ShowNotification(msg);
                // The dm keeps the running record, a player firing this from their own sheet cannot reach the host's log yet, that is the cross machine piece
                if (CurrentRole == UserRole.Dm && App.PM != null)
                {
                    await App.PM.LogEventAsync(msg);
                    if (_sessionLog != null) await _sessionLog.LoadAsync();
                }
            };

            vm.RollToChat = RelayRoll;

            // Solo has no network echo to sync off, so a heal or a slot spend on the sheet has to be handed to the live combatant right here or the tracker never sees Second Wind land.
            vm.VitalsChanged += rt =>
            {
                if (CurrentRole == UserRole.Dm && _mapSession != null && _mapSession.Initiative.CombatActive)
                    _mapSession.ApplyCharacterVitals(rt);
            };

            vm.CombatConditionApplied += condition =>
            {
                if (_mapSession == null || !_mapSession.Initiative.CombatActive) return;
                if (CurrentRole == UserRole.Dm)
                    _mapSession.Initiative.ApplyCharacterCondition(runtime.Id, condition);
                else
                    _ = App.PM.ComController.SendCombatEconomyAsync(new CombatEconomyMessage(_mapSession.MapId, runtime.Id, "sheet-condition", "", "", 0, condition));
            };

            vm.InspirationGranted += (targetId, targetName, die) =>
            {
                RelayRoll(runtime.Name + " inspires " + targetName + " (" + die + ").", false);
                if (_mapSession == null || !_mapSession.Initiative.CombatActive) return;
                if (CurrentRole == UserRole.Dm)
                    _mapSession.Initiative.ApplyCharacterInspiration(targetId, die);
                else
                    _ = App.PM.ComController.SendCombatEconomyAsync(new CombatEconomyMessage(_mapSession.MapId, runtime.Id, "inspire", targetId, "", 0, die));
            };

            vm.EditBaseRequested += () => OpenCharacterEditor(runtime);

            return vm;
        }

        private async Task OpenMyCharacter()
        {
            var runtime = App.PM.CurrentCharacterService.Current;

            if (runtime == null)
            {
                CurrentContent = new NoCharacterViewModel();
                return;
            }

            OpenCharacterSheet(runtime);
            await Task.CompletedTask;
        }

        private void OnCurrentCharacterChanged()
        {
            // The player's own character was swapped out. If their cached sheet is stale, I drop it so it gets rebuilt next time they open it.
            var current = App.PM.CurrentCharacterService.Current;

            if (current != null && _openSheets.TryGetValue(current.Id, out var cached))
            {
                if (CurrentContent == cached) return;
            }

            if (CurrentContent is CharacterSheetViewModel || CurrentContent is NoCharacterViewModel)
                _ = OpenMyCharacter();
        }

        public Task OpenMapSessionAsync(string mapId, bool isBroadcasting, string? imagePath = null, GridKind? gridKind = null, double? scale = null, int mapWidth = 0, int mapHeight = 0, double? gridOffsetX = null, double? gridOffsetY = null)
        {
            var ownCharacter = App.PM.CurrentCharacterService.Current;
            _mapSession?.Canvas.Detach();
            _mapSession?.Detach(); // Take the economy and combat handlers off too, a session left wired keeps broadcasting a snapshot that races the new one.
            _mapSession = new MapSessionViewModel(
                mapId, CurrentRole, ownCharacter,
                App.PM.ComController,
                isBroadcasting);
            _mapSession.OpenSheetRequested += async id => await OpenCharacterSheetAsync(id);
            _mapSession.TurnAlertRequested += ShowNotification;
            _mapSession.CloseRequested += async () =>
            {
                try { await CloseMapSessionAsync(); }
                catch (Exception ex)
                {
                    ErrorLog.Log("[Map] closing the map failed", ex);
                    ShowNotification("Couldn't close the map, the reason is in the log.");
                }
            };
            _broadcastSub?.Dispose();
            _broadcastSub = _mapSession.WhenAnyValue(s => s.IsBroadcasting)
                                       .Where(live => !live)
                                       .Subscribe(_ => _mapHub.ClearActiveMap());
            _mapSession.SaveOwed += label => Dispatcher.UIThread.Post(() => { OwedSaveLabel = label; OwedSaveVisible = true; });
            _mapSession.SaveSettled += () => Dispatcher.UIThread.Post(() => OwedSaveVisible = false);
            _mapSession.RollToChat = RelayRoll;
            _mapSession.Canvas.SetBackgroundImage(imagePath);
            _mapSession.Canvas.IsHost = CurrentRole == UserRole.Dm;
            _mapSession.Canvas.IsDungeonMaster = CurrentRole == UserRole.Dm;
            _mapSession.Canvas.Hub = _mapHub;
            _mapSession.Canvas.MyCharacterId = ownCharacter?.Id;
            if (CurrentRole != UserRole.Dm) _mapSession.Canvas.Mode = CanvasToolMode.Ping;
            var ownColor = ownCharacter?.ColorHex;
            _mapSession.Canvas.MyColor = string.IsNullOrWhiteSpace(ownColor) ? App.PM.ComController.MyColor : ownColor;

            // Player gets the grid straight from the activation message, the dm has no args here so it reads the choice back off its own loaded maps
            var meta = _mapHub.Maps.FirstOrDefault(m => m.Id == mapId);
            _mapSession.Canvas.LoadGrid(gridKind ?? meta?.GridKind ?? GridKind.Squares, scale ?? meta?.Scale ?? 1.0,
                gridOffsetX ?? meta?.GridOffsetX ?? 0, gridOffsetY ?? meta?.GridOffsetY ?? 0);
            _mapSession.Canvas.SetMapSize(
                mapWidth > 0 ? mapWidth : meta?.PixelWidth ?? 0,
                mapHeight > 0 ? mapHeight : meta?.PixelHeight ?? 0);

            _ = _mapSession.Canvas.LoadPersistedTokensAsync();
            _ = _mapSession.Canvas.LoadTokenLibraryAsync();
            _ = _mapSession.Canvas.InitFogAsync();
            _ = _mapSession.Canvas.InitWallsAsync();
            _ = _mapSession.Canvas.InitTerrainAsync();
            _ = _mapSession.Canvas.InitMapObjectsAsync();
            _ = _mapSession.Canvas.InitAoeTemplatesAsync();
            _ = _mapSession.Canvas.InitTokensAsync();
            _ = _mapSession.LoadCombatStateAsync();
            _ = _mapSession.InitCombatStateForPlayerAsync();

            CurrentContent = _mapSession;
            RefreshReturnToFight();
            return Task.CompletedTask;
        }

        // A player who dropped mid session gets the open map's fog, walls, tokens and combat pulled fresh off the host, the host already has the lot so it skips.
        private async Task ResyncOpenMapAsync()
        {
            var session = _mapSession;
            if (session == null || session.Canvas.IsHost) return;
            try
            {
                await session.Canvas.InitFogAsync();
                await session.Canvas.InitWallsAsync();
                await session.Canvas.InitTerrainAsync();
                await session.Canvas.InitMapObjectsAsync();
                await session.Canvas.InitAoeTemplatesAsync();
                await session.Canvas.InitTokensAsync();
                await session.InitCombatStateForPlayerAsync();
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Map resync on reconnect failed", ex);
                ShowNotification("Couldn't resync the map after reconnecting, ask the dm to nudge something.");
            }
        }

        public async Task OpenSharedMapSessionAsync(string mapId, GridKind gridKind, double scale)
        {
            var cacheDir = Path.Combine(GlobalVariables.AppDataLocal, "cache", "maps");
            Directory.CreateDirectory(cacheDir);
            string? localPath = Path.Combine(cacheDir, mapId + ".png");
            if (!File.Exists(localPath))
            {
                var bytes = await App.PM.ComController.FetchMapImageAsync(mapId);
                if (bytes != null)
                    await File.WriteAllBytesAsync(localPath, bytes);
                else
                    localPath = null;
            }

            _sharedSession?.Canvas.Detach();
            _sharedSession?.Detach();
            _sharedSession = new MapSessionViewModel(
                mapId, CurrentRole, ownCharacter: null,
                com: null, isBroadcasting: false, isSharedView: true);

            _sharedSession.Canvas.SetBackgroundImage(localPath);
            _sharedSession.Canvas.IsHost = false;
            _sharedSession.Canvas.IsDungeonMaster = false;
            _sharedSession.Canvas.TokensEnabled = false;
            _sharedSession.Canvas.ShowGrid = false;
            _sharedSession.Canvas.LoadGrid(gridKind, scale);
            _sharedSession.Canvas.Mode = CanvasToolMode.Ping;

            var ownCharacter = App.PM.CurrentCharacterService.Current;
            var ownColor = ownCharacter?.ColorHex;
            _sharedSession.Canvas.MyColor = string.IsNullOrWhiteSpace(ownColor) ? App.PM.ComController.MyColor : ownColor;

            CurrentContent = _sharedSession;
        }
    }

    public class NoCharacterViewModel : ViewModelBase
    {
        public string Message { get; } = "You don't have a character assigned to this campaign yet.";
    }
}
