using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Dujahit.Models;
using Dujahit.Models.Application;
using Dujahit.Models.Communication;
using Dujahit.Models.Database;
using Dujahit.Models.UI;
using ReactiveUI;
using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Avalonia.Threading;

namespace Dujahit.ViewModels
{
    public class MapHubViewModel : ViewModelBase
    {
        public ObservableCollection<MapSummaryViewModel> Maps { get; } = new();

        public ReactiveCommand<Unit, Unit> CreateMapCommand { get; }
        public ReactiveCommand<Unit, Unit> CreateBlankMapCommand { get; }
        public ReactiveCommand<MapSummaryViewModel, Unit> ActivateMapCommand { get; }
        public ReactiveCommand<MapSummaryViewModel, Unit> EditMapCommand { get; }
        public ReactiveCommand<MapSummaryViewModel, Unit> DeleteMapCommand { get; }
        public ReactiveCommand<MapSummaryViewModel, Unit> ToggleVisibilityCommand { get; }
        public ReactiveCommand<MapSummaryViewModel, Unit> OpenSharedMapCommand { get; }
        public event Action<string, bool, string?>? OpenSessionRequested;
        public event Action<string, string, double>? OpenSharedSessionRequested;
        public event Action? CreateMapRequested;
        public event Action? CreateBlankMapRequested;

        public bool IsSharedView { get; }
        public bool IsDmView => !IsSharedView;
        public bool ShowEmptyState => IsDmView && Maps.Count == 0;

        public string HubSubtitle => IsSharedView
            ? "Maps your DM has shared with the party"
            : "Battle maps and overland regions for this campaign";

        public MapHubViewModel(bool isSharedView = false)
        {
            IsSharedView = isSharedView;
            Maps.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(ShowEmptyState));
            CreateMapCommand = ReactiveCommand.CreateFromTask(CreateMapAsync);
            CreateBlankMapCommand = ReactiveCommand.Create(() => { CreateBlankMapRequested?.Invoke(); });
            ActivateMapCommand = ReactiveCommand.CreateFromTask<MapSummaryViewModel>(ActivateMapAsync);
            EditMapCommand = ReactiveCommand.Create<MapSummaryViewModel>(EditMap);
            DeleteMapCommand = ReactiveCommand.CreateFromTask<MapSummaryViewModel>(DeleteMapAsync);
            ToggleVisibilityCommand = ReactiveCommand.CreateFromTask<MapSummaryViewModel>(ToggleVisibilityAsync);
            OpenSharedMapCommand = ReactiveCommand.Create<MapSummaryViewModel>(OpenSharedMap);
        }

        public async Task LoadAsync()
        {
            Maps.Clear();
            var rows = await App.PM.LoadMapsAsync();
            foreach (var row in rows)
            {
                var summary = new MapSummaryViewModel(row.Id, row.Name)
                {
                    ImagePath = row.MapPath,
                    Thumbnail = LoadThumbnail(row.MapPath),
                    GridKind = row.GridKind,
                    Scale = row.Scale,
                    PixelWidth = row.Width,
                    PixelHeight = row.Height,
                    PlayerVisible = row.PlayerVisible
                };
                Maps.Add(summary);
            }
        }

        private static Bitmap? LoadThumbnail(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                using var full = new Bitmap(path);
                return full.CreateScaledBitmap(new PixelSize(260, 160), BitmapInterpolationMode.HighQuality);
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[MapHub] thumbnail load failed", ex);
                return null;
            }
        }

        private Task CreateMapAsync()
        {
            CreateMapRequested?.Invoke();
            return Task.CompletedTask;
        }

        private async Task ActivateMapAsync(MapSummaryViewModel map)
        {
            foreach (var m in Maps) m.IsActive = false;
            map.IsActive = true;
            await App.PM.ComController.ActivateMapAsync(map.Id, map.GridKind.ToString(), map.Scale, map.PixelWidth, map.PixelHeight);
            OpenSessionRequested?.Invoke(map.Id, true, map.ImagePath);
        }

        public void ClearActiveMap()
        {
            foreach (var m in Maps) m.IsActive = false;
        }

        private void EditMap(MapSummaryViewModel map)
        {
            OpenSessionRequested?.Invoke(map.Id, false, map.ImagePath);
        }

        public async Task AddMapFromImage(string name, Bitmap thumbnail, string sourcePath, int width, int height, GridKind gridKind, double scale)
        {
            var mapId = Guid.NewGuid().ToString("N");
            var across = scale > 0 ? width / (GridOverlay.BaseCellPx * scale) : 0;
            ErrorLog.Log($"[map] new '{name}' {width}x{height} scale={scale} cell={GridOverlay.BaseCellPx * scale} squaresAcross={across:0.#}");
            if (across < 4) ErrorLog.Log($"[map] '{name}' is only {across:0.#} squares across, that cell size is almost certainly wrong");
            var ext = Path.GetExtension(sourcePath);

            var campaignId = App.PM.GetCampaignId();
            var assetsDir = Path.Combine(
                GlobalVariables.AppDataLocal,
                "assets", campaignId, "maps");
            Directory.CreateDirectory(assetsDir);

            var destPath = Path.Combine(assetsDir, mapId + ext);
            File.Copy(sourcePath, destPath, overwrite: true);

            var map = new MapSummaryViewModel(mapId, name)
            {
                Thumbnail = thumbnail,
                ImagePath = destPath,
                GridKind = gridKind,
                Scale = scale
            };
            Maps.Add(map);

            await App.PM.SaveMapAsync(new Map
            {
                Id = mapId,
                CampaignId = campaignId,
                Name = name,
                Width = width,
                Height = height,
                Scale = scale,
                GridKind = gridKind,
                MapPath = destPath,
                CreatedAt = DateTime.UtcNow
            });
        }

        // Grid only, no art, so a dm with nothing on disk can still run a fight. Canvas draws the grid over an empty background.
        public async Task AddBlankMap(string name, GridKind gridKind, int cols, int rows)
        {
            var mapId = Guid.NewGuid().ToString("N");
            var campaignId = App.PM.GetCampaignId();
            var cell = GridOverlay.BaseCellPx;
            var width = (int)Math.Round(Math.Max(1, cols) * cell);
            var height = (int)Math.Round(Math.Max(1, rows) * cell);

            var map = new MapSummaryViewModel(mapId, string.IsNullOrWhiteSpace(name) ? "New map" : name)
            {
                Thumbnail = null,
                ImagePath = null,
                GridKind = gridKind,
                Scale = 1.0,
                PixelWidth = width,
                PixelHeight = height
            };
            Maps.Add(map);

            await App.PM.SaveMapAsync(new Map
            {
                Id = mapId,
                CampaignId = campaignId,
                Name = map.Name,
                Width = width,
                Height = height,
                Scale = 1.0,
                GridKind = gridKind,
                MapPath = "",
                CreatedAt = DateTime.UtcNow
            });

            OpenSessionRequested?.Invoke(mapId, false, null);
        }

        private async Task DeleteMapAsync(MapSummaryViewModel map)
        {
            Maps.Remove(map);
            await App.PM.DeleteMapAsync(map.Id);
        }

        private async Task ToggleVisibilityAsync(MapSummaryViewModel map)
        {
            map.PlayerVisible = !map.PlayerVisible;
            await App.PM.SetMapPlayerVisibleAsync(map.Id, map.PlayerVisible);
        }

        private void OpenSharedMap(MapSummaryViewModel map)
        {
            OpenSharedSessionRequested?.Invoke(map.Id, map.GridKind.ToString(), map.Scale);
        }

        public async Task LoadSharedAsync()
        {
            Maps.Clear();
            var rows = await App.PM.ComController.FetchPlayerMapsAsync();
            foreach (var row in rows)
            {
                var gridKind = Enum.TryParse<GridKind>(row.GridKind, out var gk) ? gk : GridKind.Squares;
                var summary = new MapSummaryViewModel(row.MapId, row.Name)
                {
                    GridKind = gridKind,
                    Scale = row.Scale,
                    PlayerVisible = true
                };
                Maps.Add(summary);
                _ = LoadSharedThumbnailAsync(summary);
            }
        }

        private async Task LoadSharedThumbnailAsync(MapSummaryViewModel summary)
        {
            try
            {
                var cacheDir = Path.Combine(GlobalVariables.AppDataLocal, "cache", "maps");
                Directory.CreateDirectory(cacheDir);
                var localPath = Path.Combine(cacheDir, summary.Id + ".png");
                if (!File.Exists(localPath))
                {
                    var bytes = await App.PM.ComController.FetchMapImageAsync(summary.Id);
                    if (bytes == null) return;
                    await File.WriteAllBytesAsync(localPath, bytes);
                }
                summary.ImagePath = localPath;
                var thumb = LoadThumbnail(localPath);
                if (thumb != null) summary.Thumbnail = thumb;
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[MapHub] shared thumbnail failed", ex);
            }
        }
    }

    public class MapSummaryViewModel : ViewModelBase
    {
        public string Id { get; }

        private string _name = "";
        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        private Bitmap? _thumbnail;
        public Bitmap? Thumbnail
        {
            get => _thumbnail;
            set => this.RaiseAndSetIfChanged(ref _thumbnail, value);
        }

        public string? ImagePath { get; set; }
        public GridKind GridKind { get; set; } = GridKind.Squares;
        public string GridLabel => GridKind == GridKind.Hexes ? "Overland" : "Battle";
        public double Scale { get; set; } = 1.0;
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => this.RaiseAndSetIfChanged(ref _isActive, value);
        }

        private bool _playerVisible;
        public bool PlayerVisible
        {
            get => _playerVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _playerVisible, value);
                this.RaisePropertyChanged(nameof(VisibilityLabel));
            }
        }

        public string VisibilityLabel => PlayerVisible ? "Players: On" : "Players: Off";

        public MapSummaryViewModel(string id, string name)
        {
            Id = id;
            _name = name;
        }
    }

    public class MapSessionViewModel : ViewModelBase
    {
        public MapCanvasViewModel Canvas { get; }
        public InitiativeTrackerViewModel Initiative { get; }
        public UserRole CurrentRole { get; }
        public ViewModelBase? RoleSidePanel { get; }
        public string MapId { get; }

        public bool ShowSidebar { get; } = true;

        private readonly CommunicationController? _com;
        private string _encounterId = Guid.NewGuid().ToString("N");
        private Action<CombatStateMessage>? _combatStateHandler;
        private Action<CombatEconomyMessage>? _economyHandler;
        private Action? _localStateHandler;

        private string _ownCharacterId = "";
        private string? _lastActiveCombatantId;
        private string? _lastTurnCombatantId;
        private int _lastTurnCounter = -1;
        private int _lastTurnRound = -1;
        public event Action<string>? TurnAlertRequested;

        private bool _isBroadcasting;
        public bool IsBroadcasting
        {
            get => _isBroadcasting;
            set
            {
                this.RaiseAndSetIfChanged(ref _isBroadcasting, value);
                Canvas.IsBroadcasting = value;
                this.RaisePropertyChanged(nameof(BroadcastLabel));
            }
        }

        public bool CanBroadcast => CurrentRole == UserRole.Dm && _com != null;

        public event Action? CloseRequested;
        public event Action? PlayerDisplayRequested;
        public ReactiveCommand<Unit, Unit> CloseMapCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenPlayerDisplayCommand { get; }
        public string BroadcastLabel => IsBroadcasting ? "Stop sharing" : "Go live to players";
        public string ReturnLabel => Initiative.CombatActive ? "Back to the fight, round " + Initiative.Round : "Back to the map";
        public ReactiveCommand<Unit, Unit>? ToggleBroadcastCommand { get; private set; }
        public ReactiveCommand<Unit, Unit>? ResyncCommand { get; private set; }

        private CombatantViewModel? _pendingTokenLink;
        public CombatantViewModel? PendingTokenLink
        {
            get => _pendingTokenLink;
            set => this.RaiseAndSetIfChanged(ref _pendingTokenLink, value);
        }

        public event Action<string>? OpenSheetRequested;

        public Action<string, bool>? RollToChat;

        public MapSessionViewModel(
            string mapId,
            UserRole role,
            CharacterRuntime? ownCharacter,
            CommunicationController? com = null,
            bool isBroadcasting = false,
            MapCanvasViewModel? canvas = null,
            InitiativeTrackerViewModel? initiative = null,
            bool isSharedView = false)
        {
            MapId = mapId;
            CurrentRole = role;
            _com = com;
            Canvas = canvas ?? new MapCanvasViewModel(mapId, com);
            Initiative = initiative ?? new InitiativeTrackerViewModel();
            IsBroadcasting = isBroadcasting;
            _ownCharacterId = ownCharacter?.Id ?? "";

            Canvas.OpenSheetRequested += id => OpenSheetRequested?.Invoke(id);

            if (isSharedView)
            {
                ShowSidebar = false;
                return;
            }

            Canvas.MonsterTokenPlaced += OnMonsterTokenPlaced;
            Canvas.TokenRemovedLocally += OnTokenRemovedLocally;
            Canvas.OpportunityAttackProvoked += OnOpportunityAttack;
            Canvas.CombatantOnPlayerSide = id => Initiative.Combatants.FirstOrDefault(c => c.Id == id)?.OnPlayerSide ?? false;
            Canvas.IsCombatRunning = () => Initiative.CombatActive;
            Canvas.CombatantForToken = t => Initiative.Combatants.FirstOrDefault(x =>
                (!string.IsNullOrEmpty(t.CombatantId) && x.Id == t.CombatantId)
                || (!string.IsNullOrEmpty(x.TokenId) && x.TokenId == t.Id));
            Canvas.ResolveTokenSide = t =>
            {
                var c = Canvas.CombatantForToken(t);
                return c == null ? TokenSide.None : (c.OnPlayerSide ? TokenSide.Friendly : TokenSide.Enemy);
            };
            Initiative.CombatEnded += () =>
            {
                if (CurrentRole != UserRole.Dm) return;
                _ = SweepAfterCombatAsync();
            };
            Initiative.ConcentrationEnded += caster =>
            {
                if (CurrentRole != UserRole.Dm || caster == null) return;
                _ = DropConcentrationAreasAsync(caster);
            };
            Initiative.SnapshotApplied += () => { Canvas.RefreshTokenSides(); Canvas.UpdateReachableForActive(); };
            Initiative.Combatants.CollectionChanged += OnCombatantsChanged;

            Initiative.ActiveCombatantChanged += active =>
            {
                var freshTurn = Initiative.TurnCounter != _lastTurnCounter
                                || Initiative.Round != _lastTurnRound
                                || !string.Equals(active?.Id, _lastTurnCombatantId, StringComparison.Ordinal);
                var roundAdvanced = _lastTurnRound >= 0 && Initiative.Round > _lastTurnRound;
                _lastTurnCounter = Initiative.TurnCounter;
                _lastTurnRound = Initiative.Round;
                _lastTurnCombatantId = active?.Id;

                foreach (var t in Canvas.Tokens)
                {
                    var isActive = active != null && (t.CombatantId == active.Id || (!string.IsNullOrEmpty(active.TokenId) && t.Id == active.TokenId));
                    t.IsActiveCombatant = isActive;
                    if (!freshTurn || (!isActive && active != null)) continue;
                    t.FeetMoved = 0;
                    t.FeetAnchorX = t.X;
                    t.FeetAnchorY = t.Y;
                    t.TurnStartX = t.X;
                    t.TurnStartY = t.Y;
                }
                Canvas.UpdateReachableForActive();

                if (freshTurn && active != null && CurrentRole == UserRole.Dm && RoleSidePanel is DmCombatPanelViewModel hazardPanel)
                    hazardPanel.ResolveHazardsFor(active, "turn-start", Canvas.AoeTemplates,
                        Canvas.CellSize / (App.PM?.Rules?.FeetPerSquare ?? 5.0),
                        App.PM?.Rules?.DefaultAoeWidthFeet ?? 5.0,
                        id =>
                        {
                            var tok = Canvas.Tokens.FirstOrDefault(t => t.CombatantId == id);
                            return tok == null ? null : (tok.X, tok.Y);
                        });

                if (freshTurn && active != null && CurrentRole == UserRole.Dm && RoleSidePanel is DmCombatPanelViewModel burnPanel)
                {
                    burnPanel.RollRechargesFor(active);
                    burnPanel.ResolveOngoingDamageFor(active, "turn-start");
                }

                if (roundAdvanced && CurrentRole == UserRole.Dm) _ = ExpireTimedAreasAsync();
            };

            Canvas.DashSpent += (c, cost) =>
            {
                RollToChat?.Invoke(c.Name + " dashes, " + GameRules.CostPool(cost) + " spent.", false);
                Initiative.NotifyStateChanged();
            };

            Initiative.PushCombatant = Canvas.PushCombatant;
            Initiative.InitiativeCleared += OnInitiativeCleared;
            CloseMapCommand = ReactiveCommand.Create(() => CloseRequested?.Invoke());
            OpenPlayerDisplayCommand = ReactiveCommand.Create(() => PlayerDisplayRequested?.Invoke());
            if (role == UserRole.Dm) Canvas.AoeTemplatePlacedHere += ResolveAreaCastAt;
            if (role == UserRole.Dm)
            {
                var dmCombat = new DmCombatPanelViewModel(Initiative, (t, s) => RollToChat?.Invoke(t, s), Canvas.IsFlanking, Canvas.CombatantDistanceCells, Canvas.LineToTarget, Canvas.PushCombatant);
                dmCombat.AoeTemplateRequested += ArmAoeTemplate;
                dmCombat.AreaCastArmed += ArmAreaCast;
                dmCombat.RollOwedSaveFor = RollOwedSave;
                dmCombat.StandingUp += (c, feet) =>
                {
                    var token = Canvas.Tokens.FirstOrDefault(t => t.CombatantId == c.Id);
                    if (token == null) return;
                    token.FeetMoved += feet;
                    token.FeetAnchorX = token.X;
                    token.FeetAnchorY = token.Y;
                    Canvas.UpdateReachableForActive();
                };
                RoleSidePanel = dmCombat;
                Initiative.IsDm = true;
                if (RoleSidePanel is DmCombatPanelViewModel dmp)
                {
                    dmp.ResetMoveRequested += c =>
                    {
                        var tok = Canvas.Tokens.FirstOrDefault(t => t.CombatantId == c.Id || (!string.IsNullOrEmpty(c.TokenId) && t.Id == c.TokenId));
                        if (tok != null) _ = Canvas.RevertTokenToTurnStart(tok);
                    };
                    dmp.AoeSaveRequested += () => ResolveAoeSaves(dmp);
                }

                _localStateHandler = async () =>
                {
                    Canvas.RefreshTokenSides();
                    Canvas.UpdateReachableForActive();
                    this.RaisePropertyChanged(nameof(ReturnLabel));
                    var snapshot = Initiative.BuildSnapshot(_encounterId, MapId);
                    if (_com != null)
                    {
                        try
                        {
                            await _com.SendCombatStateAsync(snapshot);
                        }
                        catch (Exception ex)
                        {
                            ErrorLog.Log("Combat state broadcast failed", ex);
                            NavItem.NavError?.Invoke("Couldn't push the combat state to the players, they may be looking at a stale fight.");
                        }
                    }
                    // Async void off a plain Action, so anything leaking out here goes straight to the AppDomain handler
                    try
                    {
                        await PersistSnapshotAsync(snapshot);
                    }
                    catch (Exception ex)
                    {
                        ErrorLog.Log("Combat state persist failed", ex);
                        NavItem.NavError?.Invoke("Couldn't save the combat state, the fight may come back wrong if you reopen the map.");
                    }
                };
                Initiative.StateChanged += _localStateHandler;
                Initiative.ConcentrationEnded += OnConcentrationEnded;
                if (App.PM != null) App.PM.OnGameDataChanged += OnCharacterConcentrationChanged;

                ToggleBroadcastCommand = ReactiveCommand.CreateFromTask(async () =>
                {
                    if (_com == null) return;
                    if (IsBroadcasting)
                    {
                        await _com.DeactivateMapAsync();
                        IsBroadcasting = false;
                    }
                    else
                    {
                        await _com.ActivateMapAsync(MapId, Canvas.GridKind.ToString(), Canvas.MapScale,
                            (int)Math.Round(Canvas.MapPixelWidth), (int)Math.Round(Canvas.MapPixelHeight));
                        IsBroadcasting = true;
                    }
                });

                ResyncCommand = ReactiveCommand.CreateFromTask(() => ResyncAsync(true));

                if (_com != null)
                {
                    _economyHandler = msg =>
                    {
                        if (msg.MapId != MapId) return;
                        _ = ApplyPlayerEconomy(msg.CombatantId, msg.Kind, msg.TargetId, msg.AllyId, msg.Level, msg.ItemId, msg.SpellId, msg.SenderUserId);
                    };
                    _com.OnCombatEconomyReceived += _economyHandler;
                }
            }
            else if (role == UserRole.Player && ownCharacter != null)
            {
                var quick = new CharacterQuickPanelViewModel(ownCharacter, Initiative, (t, s) => RollToChat?.Invoke(t, s));
                quick.AoeTemplateRequested += ArmAoeTemplate;
                quick.SaveOwed += label => SaveOwed?.Invoke(label);
                quick.SaveSettled += () => SaveSettled?.Invoke();
                RollOwedSaveFromPrompt = () => quick.RollOwedSaveCommand.Execute().Subscribe();
                quick.EconomyActionRequested += (kind, targetId, allyId, level, itemId, spellId) =>
                {
                    var own = quick.OwnCombatant;
                    if (own == null) return;
                    _ = _com?.SendCombatEconomyAsync(new CombatEconomyMessage(MapId, own.Id, kind, targetId, allyId, level, itemId, spellId));
                };
                RoleSidePanel = quick;
            }

            if (role != UserRole.Dm && _com != null)
            {
                _combatStateHandler = state =>
                {
                    if (state.MapId != MapId) return;
                    _encounterId = state.EncounterId;
                    Initiative.ApplySnapshot(state);
                    this.RaisePropertyChanged(nameof(ReturnLabel));

                    if (state.CombatActive
                        && !string.IsNullOrEmpty(state.ActiveCombatantId)
                        && state.ActiveCombatantId != _lastActiveCombatantId
                        && string.Equals(state.ActiveCombatantId, _ownCharacterId, StringComparison.Ordinal))
                    {
                        TurnAlertRequested?.Invoke("Your turn!");
                    }
                    _lastActiveCombatantId = state.ActiveCombatantId;
                };
                _com.OnCombatStateUpdated += _combatStateHandler;
            }
            
            if (RoleSidePanel is DmCombatPanelViewModel dmPanel)
            {
                dmPanel.LinkTokenRequested += c => PendingTokenLink = c;
                dmPanel.PlayerPulledIn += (combatant, opt) => { _ = SpawnAndLinkAsync(combatant, opt); };
                dmPanel.EncounterChosen += preset => { _ = SpawnEncounterAsync(preset); };
                dmPanel.SummonRequested += (caster, spellId, slotLevel) => { _ = SummonForSpellAsync(caster, spellId, slotLevel); };
                dmPanel.DismissSummonsRequested += c =>
                {
                    var casterId = c.IsSummon ? c.OwnerCharacterId : c.Id;
                    var owner = Initiative.Combatants.FirstOrDefault(x => x.Id == casterId) ?? c;
                    var gone = DismissSummonsOf(casterId);
                    if (gone > 0)
                        RollToChat?.Invoke(owner.Name + " dismisses " + gone + " summoned creature" + (gone == 1 ? "." : "s."), false);
                    else
                        dmPanel.LogLine(owner.Name + " has nothing summoned to dismiss.");
                };
            }

            Canvas.WhenAnyValue(c => c.SelectedToken)
                  .Subscribe(token =>
                  {
                      if (token != null && PendingTokenLink != null)
                      {
                          var combatant = PendingTokenLink;
                          token.CombatantId = combatant.Id;
                          token.CharacterId = combatant.IsPlayerCharacter ? combatant.Id : null;
                          combatant.TokenId = token.Id;
                          PendingTokenLink = null;

                          if (Initiative.ActiveCombatant == combatant)
                              token.IsActiveCombatant = true;

                          _ = Canvas.PersistTokenAsync(token);
                          Initiative.NotifyStateChanged();
                      }
                  });
        }

        private void OnOpportunityAttack(TokenViewModel moved, TokenViewModel enemy)
        {
            if (!Initiative.CombatActive) return;
            var attackerC = Canvas.CombatantForToken?.Invoke(enemy);
            var targetC = Canvas.CombatantForToken?.Invoke(moved);
            if (targetC != null && targetC.Disengaged) return;
            if (attackerC != null && targetC != null && RoleSidePanel is DmCombatPanelViewModel dm)
                dm.ResolveOpportunityAttack(attackerC, targetC);
            else
                RollToChat?.Invoke(CombatantName(moved) + " left " + CombatantName(enemy) + "'s reach, " + CombatantName(enemy) + " can make an opportunity attack.", false);
        }

        private string CombatantName(TokenViewModel token)
        {
            var c = Initiative.Combatants.FirstOrDefault(x => x.Id == token.CombatantId);
            return c?.Name ?? "A combatant";
        }

        // Every combatant token inside a placed template rolls the save, the dm still applies the damage since only they know the spell. A token caught by two templates only rolls once
        private void ResolveAoeSaves(DmCombatPanelViewModel dm)
        {
            if (Canvas.AoeTemplates.Count == 0) { dm.LogLine("No templates placed, nothing to roll saves against."); return; }
            var feetPerSquare = App.PM?.Rules?.FeetPerSquare ?? 5.0;
            var defaultLineFt = App.PM?.Rules?.DefaultLineWidthFeet ?? 5.0;
            var pxPerFoot = feetPerSquare > 0 ? Canvas.CellSize / feetPerSquare : 0;
            if (pxPerFoot <= 0) return;

            var rolled = new HashSet<string>();
            foreach (var token in Canvas.Tokens)
            {
                if (string.IsNullOrEmpty(token.CombatantId) || rolled.Contains(token.CombatantId)) continue;
                if (!Canvas.AoeTemplates.Any(t => t.Contains(token.X, token.Y, pxPerFoot, defaultLineFt)
                    && !Canvas.SightBlocked(new Point(t.OriginX, t.OriginY), new Point(token.X, token.Y)))) continue;
                var combatant = Initiative.Combatants.FirstOrDefault(c => c.Id == token.CombatantId);
                if (combatant == null) continue;
                rolled.Add(token.CombatantId);
                dm.RollSave(combatant, dm.AoeSaveAbility, dm.SaveDc);
            }
            dm.LogLine(rolled.Count == 0
                ? "No tokens caught in the templates."
                : rolled.Count + " caught, rolled " + dm.AoeSaveAbility + " saves vs DC " + dm.SaveDc + ".");
        }

        public async Task ResyncAsync(bool announce)
        {
            if (_com == null || CurrentRole != UserRole.Dm || !IsBroadcasting) return;
            await _com.ActivateMapAsync(MapId, Canvas.GridKind.ToString(), Canvas.MapScale,
                (int)Math.Round(Canvas.MapPixelWidth), (int)Math.Round(Canvas.MapPixelHeight));
            await _com.SendCombatStateAsync(Initiative.BuildSnapshot(_encounterId, MapId));
            if (announce) TurnAlertRequested?.Invoke("Resynced the map and combat to players.");
        }

        private async void OnCharacterConcentrationChanged(string entityType, string entityId)
        {
            try
            {
                if (CurrentRole != UserRole.Dm || !string.Equals(entityType, "Character", StringComparison.OrdinalIgnoreCase)) return;
                _economyOwnerCache.Remove(entityId);
                var combatant = Initiative.Combatants.FirstOrDefault(c => c.IsPlayerCharacter && c.Id == entityId);
                if (combatant == null || App.PM == null) return;
                try
                {
                    var rt = await App.PM.LoadCharacterByIdAsync(entityId);
                    if (rt == null || rt.Concentration == combatant.Concentration) return;
                    combatant.Concentration = rt.Concentration;
                    if (!rt.Concentration) Initiative.EndConcentrationEffects(combatant);
                    Initiative.NotifyStateChanged();
                }
                catch (Exception ex) { ErrorLog.Log($"[Map] concentration push failed", ex); }
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnCharacterConcentrationChanged", ex); }
        }

        public async Task InitCombatStateForPlayerAsync()
        {
            if (CurrentRole == UserRole.Dm || _com == null) return;
            try
            {
                var snap = await _com.FetchCombatStateAsync(MapId);
                if (snap != null && snap.MapId == MapId)
                {
                    _encounterId = snap.EncounterId;
                    Initiative.ApplySnapshot(snap);
                }
            }
            catch (Exception ex) { ErrorLog.Log($"[Map] combat resync failed", ex); }
        }

        public async Task LoadCombatStateAsync()
        {
            if (CurrentRole != UserRole.Dm || App.PM == null) return;

            await Canvas.LoadPersistedTokensAsync();

            var loaded = await App.PM.LoadActiveEncounterAsync(MapId);
            if (loaded == null) return;

            var (enc, combatants) = loaded.Value;
            _encounterId = enc.Id;

            Initiative.ApplySnapshot(new CombatStateMessage(
                enc.Id, MapId, enc.IsActive && enc.Round > 0, enc.Round, enc.ActiveCombatantId,
                combatants.Select(c => new CombatantSnapshot(
                    c.Id, c.Name, c.Initiative, c.CurrentHp, c.MaxHp,
                    c.IsPlayerCharacter, c.RevealExactHp, c.TokenId,
                    c.MaxActions, c.ActionsRemaining, c.MaxBonusActions, c.BonusActionsRemaining, c.SpellSlotsJson, c.Concentration,
                    c.DeathSaveSuccesses, c.DeathSaveFailures, c.AttacksJson, c.IsFriendly, c.ExtrasJson)).ToList()));

            if (ReconcileCombatantTokenLinks())
                await PersistSnapshotAsync(Initiative.BuildSnapshot(_encounterId, MapId));

            Canvas.RefreshTokenSides();

            if (_com != null)
                await _com.SendCombatStateAsync(Initiative.BuildSnapshot(_encounterId, MapId));
        }

        private bool ReconcileCombatantTokenLinks()
        {
            var healed = false;
            foreach (var combatant in Initiative.Combatants)
            {
                TokenViewModel? token = null;
                if (!string.IsNullOrEmpty(combatant.TokenId))
                    token = Canvas.Tokens.FirstOrDefault(t => t.Id == combatant.TokenId);
                if (token == null && combatant.IsPlayerCharacter)
                    token = Canvas.Tokens.FirstOrDefault(t => t.CharacterId == combatant.Id);
                if (token == null) continue;

                token.CombatantId = combatant.Id;
                if (combatant.IsPlayerCharacter && string.IsNullOrEmpty(token.CharacterId))
                    token.CharacterId = combatant.Id;
                if (combatant.TokenId != token.Id)
                {
                    combatant.TokenId = token.Id;
                    healed = true;
                }
            }

            var active = Initiative.ActiveCombatant;
            foreach (var t in Canvas.Tokens)
                t.IsActiveCombatant = active != null
                    && (t.CombatantId == active.Id || (!string.IsNullOrEmpty(active.TokenId) && t.Id == active.TokenId));

            return healed;
        }

        private readonly Dictionary<string, string> _economyOwnerCache = new();

        private async Task ApplyPlayerEconomy(string combatantId, string kind, string targetId, string allyId, int level = 0, string itemId = "", string spellId = "", string senderUserId = "")
        {
            var c = Initiative.Combatants.FirstOrDefault(x => x.Id == combatantId);
            if (c == null || (!c.IsPlayerCharacter && !c.IsSummon)) return;
            var offTurnOk = kind == "reaction" || kind == "cast-reaction" || kind == "sheet-condition" || kind == "inspire";
            if (!offTurnOk && !ReferenceEquals(Initiative.ActiveCombatant, c)) return;
            var ownerCharId = c.IsSummon ? c.OwnerCharacterId : c.Id;
            if (!_economyOwnerCache.TryGetValue(ownerCharId, out var owner))
            {
                owner = (App.PM != null ? (await App.PM.LoadCharacterByIdAsync(ownerCharId))?.OwnerUserId : null) ?? "";
                if (owner.Length > 0) _economyOwnerCache[ownerCharId] = owner;
            }
            if (string.IsNullOrEmpty(senderUserId) || string.IsNullOrEmpty(owner)
                || !string.Equals(owner, senderUserId, StringComparison.OrdinalIgnoreCase)) return;

            if (kind == "sheet-condition")
            {
                Initiative.ApplyCharacterCondition(combatantId, itemId);
                return;
            }

            if (kind == "inspire")
            {
                Initiative.ApplyCharacterInspiration(targetId, itemId);
                return;
            }

            if (kind == "area-save")
            {
                RollOwedSave(c);
                return;
            }

            if (kind == "death-save")
            {
                if (RoleSidePanel is DmCombatPanelViewModel deathPanel) deathPanel.RollDeathSaveFor(c);
                return;
            }

            if (kind == "weapon-attack")
            {
                if (RoleSidePanel is DmCombatPanelViewModel weaponPanel)
                {
                    var swungAt = string.IsNullOrEmpty(targetId) ? null : Initiative.Combatants.FirstOrDefault(x => x.Id == targetId);
                    _ = weaponPanel.ResolvePlayerWeaponAttackAsync(c, swungAt, itemId);
                }
                return;
            }

            if (IsTacticalKind(kind))
            {
                var target = string.IsNullOrEmpty(targetId) ? null : Initiative.Combatants.FirstOrDefault(x => x.Id == targetId);
                var ally = string.IsNullOrEmpty(allyId) ? null : Initiative.Combatants.FirstOrDefault(x => x.Id == allyId);
                var line = Initiative.ResolveTacticalAction(c, kind, target, ally);
                if (!string.IsNullOrEmpty(line)) RollToChat?.Invoke(line, false);
                return;
            }

            bool changed;
            switch (kind)
            {
                case "action": changed = c.SpendAction(); break;
                case "bonus": changed = c.SpendBonusAction(); break;
                case "reaction": changed = c.SpendReaction(); break;
                case "surge": changed = c.UseActionSurge(); break;
                case "dash":
                {
                    var dashCost = EconomyCostFor(c, "dash");
                    changed = !c.Dashed && InitiativeTrackerViewModel.CanAfford(c, dashCost);
                    if (changed)
                    {
                        InitiativeTrackerViewModel.Pay(c, dashCost);
                        c.Dashed = true;
                        c.DashPaid = true;
                    }
                    break;
                }
                case "ready":
                {
                    var readyCost = EconomyCostFor(c, "ready");
                    changed = !c.Readied && InitiativeTrackerViewModel.CanAfford(c, readyCost);
                    if (changed)
                    {
                        InitiativeTrackerViewModel.Pay(c, readyCost);
                        c.Readied = true;
                    }
                    break;
                }
                case "delay":
                    Initiative.DelayTurn();
                    return;

                case "cast-action":
                case "cast-bonus":
                case "cast-reaction":
                case "cast-none":
                    _ = ApplyPlayerCastAsync(c, kind, level, spellId, targetId);
                    return;

                default: return;
            }

            if (changed) Initiative.NotifyStateChanged();
        }

        // The slot goes after the affordability check, otherwise a cast with no action left burns the slot anyway. DON'T CHANGE
        private async Task ApplyPlayerCastAsync(CombatantViewModel c, string kind, int level, string spellId, string targetId = "")
        {
            var cost = kind switch { "cast-action" => "action", "cast-bonus" => "bonus", "cast-reaction" => "reaction", _ => "none" };
            if (App.PM != null && cost != "none" && !string.IsNullOrEmpty(spellId))
            {
                var mapped = SpellCaster.CastCost(await App.PM.ReadSpellCastingTimeAsync(spellId));
                if (!string.IsNullOrEmpty(mapped)) cost = mapped;
            }
            if (kind == "cast-reaction" && !string.Equals(GameRules.CostPool(cost), "reaction", StringComparison.OrdinalIgnoreCase)) return;
            if (cost != "none" && !InitiativeTrackerViewModel.CanAfford(c, cost)) return;
            if (level > 0 && !c.SpendSlot(level)) return;
            if (cost != "none") InitiativeTrackerViewModel.Pay(c, cost);
            _ = SummonForSpellAsync(c, spellId, level);
            if (RoleSidePanel is DmCombatPanelViewModel castPanel)
            {
                var target = string.IsNullOrEmpty(targetId) ? null : Initiative.Combatants.FirstOrDefault(x => x.Id == targetId);
                await castPanel.ResolvePlayerSpellCastAsync(c, target, spellId, level);
            }
            Initiative.NotifyStateChanged();
        }

        private static bool IsTacticalKind(string kind) => App.PM?.Rules?.TacticalActions.ContainsKey(kind) ?? false;

        public static string EconomyCostFor(CombatantViewModel c, string key) =>
            c.CostForAction(key, App.PM?.Rules?.CostFor(key) ?? "action");

        private (ActionResolution Res, CombatantViewModel Caster, string SpellName, CombatantViewModel? Holder, SpellAoe? Area)? _pendingArea;

        internal void ArmAreaCast(ActionResolution res, CombatantViewModel caster, string spellName, CombatantViewModel? holder, SpellAoe? area = null)
            => _pendingArea = (res, caster, spellName, holder, area);

        private void ResolveAreaCastAt(AoeTemplateViewModel placed)
        {
            var pending = _pendingArea;
            _pendingArea = null;
            if (pending == null || placed == null) return;

            var rules = App.PM?.Rules ?? new GameRules();
            var feetPerSquare = rules.FeetPerSquare > 0 ? rules.FeetPerSquare : 5.0;
            var pxPerFoot = feetPerSquare > 0 ? Canvas.CellSize / feetPerSquare : 0;
            if (pxPerFoot <= 0) return;
            var defaultLineFt = rules.DefaultLineWidthFeet;

            var lasting = StampPersistence(placed, pending.Value.Area, pending.Value.SpellName, pending.Value.Res, pending.Value.Holder);

            var caught = new List<CombatantViewModel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var token in Canvas.Tokens)
            {
                if (string.IsNullOrEmpty(token.CombatantId) || !seen.Add(token.CombatantId)) continue;
                if (!placed.Contains(token.X, token.Y, pxPerFoot, defaultLineFt)) continue;
                if (Canvas.SightBlocked(new Point(placed.OriginX, placed.OriginY), new Point(token.X, token.Y))) continue;
                var combatant = Initiative.Combatants.FirstOrDefault(c => c.Id == token.CombatantId);
                if (combatant != null) caught.Add(combatant);
            }

            var res = pending.Value.Res;
            var bites = res.Kind == ActionOutcomeKind.Damage && !string.IsNullOrEmpty(res.SaveAbility) && res.HpDelta < 0;

            if (caught.Count == 0 || !bites)
            {
                if (caught.Count == 0 && !lasting) RollToChat?.Invoke(pending.Value.SpellName + " catches nobody.", false);
                Initiative.NotifyStateChanged();
                return;
            }

            var waiting = new List<string>();
            foreach (var victim in caught)
            {
                if (rules.PlayerRollsOwnSaves && victim.IsPlayerCharacter)
                {
                    victim.PendingSaveAbility = res.SaveAbility ?? "";
                    victim.PendingSaveDc = res.SaveDc;
                    victim.PendingSaveDamage = res.HpDelta;
                    victim.PendingSaveDamageType = res.DamageType ?? "";
                    victim.PendingSaveHalf = res.HalfOnSave;
                    victim.PendingSaveSource = pending.Value.SpellName;
                    _owedSaves[victim.Id] = (res, pending.Value.SpellName, pending.Value.Holder);
                    waiting.Add(victim.Name);
                    continue;
                }
                var note = ApplySaveDamageOn(res, victim, pending.Value.SpellName, pending.Value.Holder);
                RollToChat?.Invoke(pending.Value.SpellName + note, false);
            }

            if (waiting.Count > 0)
                RollToChat?.Invoke("Waiting on a save from " + string.Join(", ", waiting) + ".", false);

            Initiative.NotifyStateChanged();
        }

        private bool StampPersistence(AoeTemplateViewModel placed, SpellAoe? area, string spellName, ActionResolution res, CombatantViewModel? holder)
        {
            if (area == null || area.LastsRounds <= 0) return false;

            placed.RoundsLeft = area.LastsRounds;
            placed.Trigger = string.IsNullOrWhiteSpace(area.Trigger) ? "turn-start" : area.Trigger;
            placed.Damage = area.Damage;
            placed.DamageType = string.IsNullOrWhiteSpace(area.DamageType) ? res.DamageType ?? "" : area.DamageType;
            placed.Terrain = area.Terrain;
            placed.Condition = area.Condition;
            placed.ConditionRounds = area.ConditionRounds;
            placed.Label = string.IsNullOrWhiteSpace(area.Label) ? spellName : area.Label;
            placed.SaveAbility = area.SaveAbility;
            placed.SaveDc = res.SaveDc > 0 ? res.SaveDc : area.SaveDc;
            placed.OwnerId = holder?.Id ?? "";

            Canvas.RefreshAfterPersistenceStamp();

            var what = new List<string>();
            if (!string.IsNullOrWhiteSpace(placed.Damage)) what.Add(placed.Damage + " " + placed.DamageType + " on " + placed.Trigger);
            if (!string.IsNullOrWhiteSpace(placed.Terrain)) what.Add(placed.Terrain + " ground");
            if (!string.IsNullOrWhiteSpace(placed.Condition)) what.Add(placed.Condition);

            RollToChat?.Invoke(placed.Label + " settles for " + placed.RoundsLeft + " rounds"
                               + (what.Count > 0 ? ", " + string.Join(", ", what) : "") + ".", false);
            return true;
        }

        private async Task SweepAfterCombatAsync()
        {
            try
            {
                var gone = await Canvas.SweepTimedAreasAsync();
                if (gone.Count > 0) RollToChat?.Invoke("The fight ends, " + string.Join(" and ", gone) + " burns out.", false);
            }
            catch (Exception ex) { ErrorLog.Log("[Map] sweeping timed areas after combat failed", ex); }
        }

        private async Task DropConcentrationAreasAsync(CombatantViewModel caster)
        {
            try
            {
                var gone = await Canvas.DropAreasHeldByAsync(caster.Id);
                foreach (var label in gone) RollToChat?.Invoke(label + " goes out, " + caster.Name + " lost concentration.", false);
            }
            catch (Exception ex) { ErrorLog.Log("[Map] dropping a concentration area failed", ex); }
        }

        private async Task ExpireTimedAreasAsync()
        {
            try
            {
                var gone = await Canvas.TickTimedAreasAsync();
                foreach (var label in gone) RollToChat?.Invoke(label + " fades.", false);
            }
            catch (Exception ex) { ErrorLog.Log("[Map] expiring a placed area failed", ex); }
        }

        private readonly Dictionary<string, (ActionResolution Res, string SpellName, CombatantViewModel? Holder)> _owedSaves = new();

        public event Action<string>? SaveOwed;
        public event Action? SaveSettled;
        public Action? RollOwedSaveFromPrompt { get; private set; }

        internal void RollOwedSave(CombatantViewModel victim)
        {
            if (victim == null || !victim.HasPendingSave) return;

            ActionResolution res;
            string spellName;
            CombatantViewModel? holder;

            if (_owedSaves.TryGetValue(victim.Id, out var owed))
            {
                _owedSaves.Remove(victim.Id);
                res = owed.Res;
                spellName = owed.SpellName;
                holder = owed.Holder;
            }
            else
            {
                res = new ActionResolution
                {
                    Kind = ActionOutcomeKind.Damage,
                    SaveAbility = victim.PendingSaveAbility,
                    SaveDc = victim.PendingSaveDc,
                    HpDelta = victim.PendingSaveDamage,
                    DamageType = victim.PendingSaveDamageType,
                    HalfOnSave = victim.PendingSaveHalf
                };
                spellName = string.IsNullOrWhiteSpace(victim.PendingSaveSource) ? "The area" : victim.PendingSaveSource;
                holder = null;
            }

            ClearOwedSave(victim);
            var owedNote = ApplySaveDamageOn(res, victim, spellName, holder);
            RollToChat?.Invoke(spellName + owedNote, false);
            Initiative.NotifyStateChanged();
        }

        private void ClearOwedSave(CombatantViewModel victim)
        {
            _owedSaves.Remove(victim.Id);
            victim.PendingSaveAbility = "";
            victim.PendingSaveDc = 0;
            victim.PendingSaveDamage = 0;
            victim.PendingSaveDamageType = "";
            victim.PendingSaveHalf = false;
            victim.PendingSaveSource = "";
        }

        private string ApplySaveDamageOn(ActionResolution res, CombatantViewModel victim, string spellName, CombatantViewModel? holder)
        {
            if (RoleSidePanel is DmCombatPanelViewModel panel) return panel.ApplySaveDamage(res, victim, spellName, holder);
            return "";
        }

        private void ArmAoeTemplate(SpellAoe aoe)
        {
            if (aoe == null || string.IsNullOrWhiteSpace(aoe.Shape)) return;
            Canvas.AoeShape = aoe.Shape;
            if (aoe.SizeFt > 0) Canvas.AoeSizeFt = (decimal)aoe.SizeFt;
            if (aoe.WidthFt > 0) Canvas.AoeWidthFt = (decimal)aoe.WidthFt;
            Canvas.AoeFromToken = true;
            if (RoleSidePanel is DmCombatPanelViewModel panel)
            {
                if (aoe.SaveDc > 0) panel.SaveDc = aoe.SaveDc;
                var wanted = panel.AoeSaveAbilities.FirstOrDefault(a => string.Equals(a, aoe.SaveAbility, StringComparison.OrdinalIgnoreCase));
                if (wanted != null) panel.AoeSaveAbility = wanted;
            }
            Canvas.EnterTemplateMode();
        }

        // A player editing their own sheet only sends a character sync, so the live combatant needs pushing here.
        public void ApplyCharacterVitals(CharacterRuntime rt)
        {
            if (rt == null) return;
            var c = Initiative.Combatants.FirstOrDefault(x => x.IsPlayerCharacter && x.Id == rt.Id);
            if (c == null) return;
            c.MaxHp = rt.MaxHp;
            c.CurrentHp = rt.CurrentHp;
            c.TempHp = rt.TempHp;
            foreach (var row in c.SpellSlots)
                if (rt.SpellSlotsUsed.TryGetValue(row.Level, out var used))
                    row.Used = Math.Min(used, row.Max);
        }

        public void Detach()
        {
            if (_com != null && _combatStateHandler != null)
            {
                _com.OnCombatStateUpdated -= _combatStateHandler;
                _combatStateHandler = null;
            }

            if (_com != null && _economyHandler != null)
            {
                _com.OnCombatEconomyReceived -= _economyHandler;
                _economyHandler = null;
            }

            if (_localStateHandler != null)
            {
                Initiative.StateChanged -= _localStateHandler;
                _localStateHandler = null;
            }

            Canvas.TokenRemovedLocally -= OnTokenRemovedLocally;
            Initiative.Combatants.CollectionChanged -= OnCombatantsChanged;
            Initiative.ConcentrationEnded -= OnConcentrationEnded;
            Initiative.InitiativeCleared -= OnInitiativeCleared;
            if (App.PM != null) App.PM.OnGameDataChanged -= OnCharacterConcentrationChanged;
        }

        private async Task SpawnAndLinkAsync(CombatantViewModel combatant, PlayerOptionViewModel opt)
        {
            try
            {
                var existing = Canvas.Tokens.Where(t =>
                        (!string.IsNullOrEmpty(t.CharacterId) && t.CharacterId == combatant.Id)
                        || (!string.IsNullOrEmpty(t.CombatantId) && t.CombatantId == combatant.Id)
                        || (!string.IsNullOrEmpty(combatant.TokenId) && t.Id == combatant.TokenId))
                    .ToList();
                var token = existing.FirstOrDefault();
                for (var i = existing.Count - 1; i >= 1; i--)
                    _ = Canvas.RemoveToken(existing[i]);

                token ??= await Canvas.SpawnCharacterTokenAsync(combatant.Id, combatant.Name, opt.ColorHex, opt.TokenImagePath, combatant.Id);
                if (token == null) return;
                token.CombatantId = combatant.Id;
                token.CharacterId = combatant.IsPlayerCharacter ? combatant.Id : null;
                combatant.TokenId = token.Id;
                if (Initiative.ActiveCombatant == combatant) token.IsActiveCombatant = true;

                await Canvas.PersistTokenAsync(token);
                Initiative.NotifyStateChanged();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Session] pull-in token spawn failed", ex);
            }
        }

        // Set for the length of a single summon spawn, so the monster placement path can tag what it just made without growing a parameter through three layers.
        private string? _pendingSummonOwner;
        private int? _pendingSummonInitiative;
        private bool _pendingSummonConcentration;

        // A summoned creature acts on the turn of whoever called it, hmmmm that is what keeps a familiar from wandering off into its own slot in the order
        public async Task<int> SummonForSpellAsync(CombatantViewModel caster, string spellId, int slotLevel)
        {
            if (CurrentRole != UserRole.Dm || caster == null) return 0;
            var rule = App.PM?.Rules.SummonFor(spellId);
            if (rule == null) return 0;

            var monster = Canvas.Monsters.FirstOrDefault(m => m.Id == rule.MonsterId);
            if (monster == null)
            {
                (RoleSidePanel as DmCombatPanelViewModel)?.LogLine(caster.Name + " tried to summon " + rule.MonsterId + " and this template does not have it.");
                return 0;
            }

            var concentrates = App.PM != null && await App.PM.ReadSpellConcentrationAsync(spellId);
            var wanted = rule.CountForSlot(slotLevel);
            var placed = 0;
            _pendingSummonOwner = caster.Id;
            _pendingSummonInitiative = caster.Initiative;
            _pendingSummonConcentration = concentrates;
            try
            {
                for (var i = 0; i < wanted; i++)
                    if (await Canvas.SpawnMonsterTokenAsync(monster) != null) placed++;
            }
            finally
            {
                _pendingSummonOwner = null;
                _pendingSummonInitiative = null;
                _pendingSummonConcentration = false;
            }

            if (placed > 0)
            {
                if (concentrates) caster.Concentration = true;
                Initiative.NotifyStateChanged();
                RollToChat?.Invoke(caster.Name + " summons " + placed + " " + monster.Name + (placed == 1 ? "" : "s") + ", acting on their initiative"
                    + (concentrates ? ", held by concentration." : "."), false);
            }
            if (placed < wanted) (RoleSidePanel as DmCombatPanelViewModel)?.LogLine((wanted - placed) + " of the summoned creatures had no free cell.");
            return placed;
        }

        private void OnConcentrationEnded(CombatantViewModel caster)
        {
            if (CurrentRole != UserRole.Dm || caster == null) return;
            var gone = DismissSummonsOf(caster.Id, true);
            if (gone > 0)
                RollToChat?.Invoke(caster.Name + "'s concentration ends, " + gone + " summoned creature" + (gone == 1 ? " vanishes." : "s vanish."), false);
        }

        // Everything the caster called goes away at once, which is the only sane way to end a concentration summon
        public int DismissSummonsOf(string casterId, bool concentrationOnly = false)
        {
            var mine = Initiative.Combatants.Where(c => c.IsDrivenBy(casterId) && c.IsSummon && (!concentrationOnly || c.ConcentrationSummon)).ToList();
            foreach (var c in mine)
            {
                var token = Canvas.Tokens.FirstOrDefault(t => t.Id == c.TokenId);
                if (token != null) _ = Canvas.RemoveToken(token);
                Initiative.Combatants.Remove(c);
            }
            if (mine.Count > 0) Initiative.NotifyStateChanged();
            return mine.Count;
        }

        private void OnMonsterTokenPlaced(MonsterPlacement p)
        {
            if (CurrentRole != UserRole.Dm) return;

            var init = p.InitiativeOverride ?? (DiceManager.RollInitiativeDie() + p.Monster.DexMod);

            var combatant = new CombatantViewModel(Guid.NewGuid().ToString("N"), UniqueCombatantName(p.Monster.Name), isPlayerCharacter: false)
            {
                Initiative = init,
                MaxHp = p.Monster.HitPoints,
                CurrentHp = p.Monster.HitPoints,
                ArmorClass = p.Monster.ArmorClass,
                DexMod = p.Monster.DexMod,
                RevealExactHpToPlayers = false,
                TokenId = p.Token.Id
            };
            combatant.SpeedFeet = p.Monster.Speed > 0 ? p.Monster.Speed : (App.PM?.CombatBaseSpeedFeet ?? 30);
            combatant.AttacksPerAction = p.Monster.AttacksPerAction;
            combatant.LegendaryPerRound = p.Monster.LegendaryPerRound;
            combatant.LegendaryRemaining = p.Monster.LegendaryPerRound;
            foreach (var la in p.Monster.LegendaryActions) combatant.LegendaryActions.Add(la);
            combatant.LairInitiative = p.Monster.LairInitiative;
            foreach (var la in p.Monster.LairActions) combatant.LairActions.Add(la);
            combatant.OwnerCharacterId = _pendingSummonOwner ?? "";
            combatant.ConcentrationSummon = _pendingSummonConcentration;
            if (_pendingSummonInitiative.HasValue) combatant.Initiative = _pendingSummonInitiative.Value;
            combatant.Resistances.AddRange(p.Monster.Resistances);
            combatant.Immunities.AddRange(p.Monster.Immunities);
            combatant.Vulnerabilities.AddRange(p.Monster.Vulnerabilities);
            foreach (var kv in p.Monster.Saves) combatant.SetSave(kv.Key, kv.Value);
            combatant.ConSaveBonus = combatant.SaveBonusFor(App.PM?.Rules?.ConcentrationAbility ?? "con");
            foreach (var a in p.Monster.Attacks)
                combatant.Attacks.Add(new CombatantAttackViewModel(a.Name, a.ToHit, a.Damage, a.DamageType, a.RangeFeet, "", 0,
                    a.AreaShape, a.AreaSizeFt, a.AreaWidthFt, a.SaveAbility, a.SaveDc));

            p.Token.CombatantId = combatant.Id;

            Initiative.AddInInitiativeOrder(combatant);
            Initiative.NotifyStateChanged();
        }

        private async Task SpawnEncounterAsync(EncounterPreset preset)
        {
            if (preset == null || CurrentRole != UserRole.Dm) return;

            int placed = 0, noRoom = 0, missing = 0;
            foreach (var entry in preset.Monsters)
            {
                var monster = Canvas.Monsters.FirstOrDefault(m => m.Id == entry.MonsterId);
                var copies = Math.Max(1, entry.Count);
                if (monster == null) { missing += copies; continue; }

                if (entry.Attacks != null && entry.Attacks.Count > 0)
                    monster = new MonsterOption
                    {
                        Id = monster.Id,
                        Name = monster.Name,
                        Size = monster.Size,
                        Description = monster.Description,
                        DefaultColor = monster.DefaultColor,
                        ChallengeRating = monster.ChallengeRating,
                        ArmorClass = monster.ArmorClass,
                        HitPoints = monster.HitPoints,
                        DexMod = monster.DexMod,
                        Speed = monster.Speed,
                        AttacksPerAction = monster.AttacksPerAction,
                        LegendaryPerRound = monster.LegendaryPerRound,
                        LegendaryActions = monster.LegendaryActions.ToList(),
                        LairInitiative = monster.LairInitiative,
                        LairActions = monster.LairActions.ToList(),
                        Saves = new Dictionary<string, int>(monster.Saves, StringComparer.OrdinalIgnoreCase),
                        Attacks = entry.Attacks.ToList(),
                        Resistances = monster.Resistances.ToList(),
                        Immunities = monster.Immunities.ToList(),
                        Vulnerabilities = monster.Vulnerabilities.ToList()
                    };

                for (var i = 0; i < copies; i++)
                {
                    var token = await Canvas.SpawnMonsterTokenAsync(monster);
                    if (token == null) noRoom++;
                    else placed++;
                }
            }

            var parts = new List<string> { "Dropped " + preset.Name + ": " + placed + " placed" };
            if (noRoom > 0) parts.Add(noRoom + " had no free cell");
            if (missing > 0) parts.Add(missing + " not in the current template");
            (RoleSidePanel as DmCombatPanelViewModel)?.LogLine(string.Join(", ", parts));
        }

        private void OnTokenRemovedLocally(string tokenId)
        {
            if (CurrentRole != UserRole.Dm) return;
            var combatant = Initiative.Combatants.FirstOrDefault(c => c.TokenId == tokenId);
            if (combatant == null) return;

            if (ReferenceEquals(Initiative.ActiveCombatant, combatant) && Initiative.Combatants.Count > 1)
                Initiative.NextTurn();
            Initiative.Combatants.Remove(combatant);
            if (ReferenceEquals(Initiative.ActiveCombatant, combatant)) Initiative.ActiveCombatant = null;
            Initiative.NotifyStateChanged();
        }

        private void OnCombatantsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (CurrentRole != UserRole.Dm) return;
            if (Initiative.IsApplyingRemote) return;
            if (e.Action == NotifyCollectionChangedAction.Move) return; // A reorder carries the same combatant in Old and New, a Move is not a spawn or a despawn.

            if (e.OldItems != null)
            {
                foreach (CombatantViewModel c in e.OldItems)
                {
                    var token = Canvas.Tokens.FirstOrDefault(t =>
                        t.CombatantId == c.Id || (!string.IsNullOrEmpty(c.TokenId) && t.Id == c.TokenId));
                    if (token != null) _ = Canvas.RemoveToken(token);
                }
            }

            if (e.NewItems == null) return;
            foreach (CombatantViewModel c in e.NewItems)
                _ = InitCombatantEconomyAsync(c);
        }

        private static void ApplyPlayerSaveBonuses(CombatantViewModel c, CharacterRuntime rt)
        {
            if (App.PM == null) return;
            var rules = App.PM.Rules;
            var prof = App.PM.ProficiencyBonusForLevel(rt.Level);
            var a = rt.AbilityScores;

            int ScoreFor(string sht) => a.Get(rules.AbilityIdForShort(sht));
            int ModFor(string sht) => App.PM.AbilityMod(ScoreFor(sht));

            c.Saves.Clear();
            foreach (var def in rules.Abilities)
                c.SetSave(def.Short, ModFor(def.Short) + rules.RankBonus(GameRules.RankIdFor(rt.ProficientSaves.Contains(def.Short.ToLowerInvariant())), prof));

            c.ConSaveBonus = c.SaveBonusFor(rules.ConcentrationAbility);
            c.ContestBonus = rules.RankBonus(GameRules.RankIdFor(true), prof) + rules.ContestAbilities.Select(ModFor).DefaultIfEmpty(0).Max();
        }

        private async Task InitCombatantEconomyAsync(CombatantViewModel c)
        {
            if (App.PM == null) return;
            try
            {
                var (baseA, baseB) = await App.PM.GetCombatSettingsAsync();
                int addA = 0, addB = 0, surgeA = 0, surgeB = 0, surgeUses = 0;
                if (c.IsPlayerCharacter && !string.IsNullOrEmpty(c.Id))
                {
                    var econ = await App.PM.ResolveActionEconomyAsync(c.Id);
                    addA = econ.AddActions; addB = econ.AddBonusActions;
                    surgeA = econ.SurgeActions; surgeB = econ.SurgeBonusActions; surgeUses = econ.SurgeUses;
                    c.ActionCostOverrides.Clear();
                    foreach (var kv in econ.CostOverrides) c.ActionCostOverrides[kv.Key] = kv.Value;
                    var rt = await App.PM.LoadCharacterByIdAsync(c.Id);
                    if (rt != null && rt.Speed > 0) c.SpeedFeet = rt.Speed;
                    if (rt != null) ApplyPlayerSaveBonuses(c, rt);

                    var bonuses = await App.PM.ResolveCharacterBonusesAsync(c.Id);
                    c.AttacksPerAction = 1 + bonuses.ExtraAttacks;
                    c.OffHandAbilityMod = bonuses.OffHandAbilityMod;
                    if (rt != null) c.CharacterLevel = rt.Level;
                    if (rt != null) c.ExhaustionLevel = rt.ExhaustionLevel;
                    if (rt != null) c.HasInspiration = rt.Inspiration;
                    if (rt != null) { c.Senses.Clear(); c.Senses.AddRange(rt.Senses); }
                    foreach (var res in bonuses.Resistances)
                        if (!c.Resistances.Contains(res)) c.Resistances.Add(res);
                    c.Riders.Clear();
                    c.Riders.AddRange(bonuses.Riders);
                    c.AdvantageOn.Clear();
                    foreach (var adv in bonuses.AdvantageOn) c.AdvantageOn.Add(adv);
                    c.Conditional.Clear();
                    c.Conditional.AddRange(bonuses.Conditional);
                    c.GrantedReactions.Clear();
                    c.GrantedReactions.AddRange(bonuses.Reactions);
                    if (rt != null && bonuses.MaxHpPerLevel != 0) c.MaxHp = rt.MaxHp + bonuses.MaxHpPerLevel * rt.Level;

                    // A pc has no statblock attacks, so an opportunity attack needs something to roll.
                    if (rt != null && c.Attacks.Count == 0)
                    {
                        int Mod(string sht) => App.PM.AbilityMod(rt.AbilityScores.Get(App.PM.Rules.AbilityIdForShort(sht)));
                        var best = App.PM.Rules.FallbackAttackAbilities.Select(Mod).DefaultIfEmpty(0).Max();
                        var dmg = App.PM.Rules.FallbackAttackDamage + (best >= 0 ? "+" + best : best.ToString());
                        c.Attacks.Add(new CombatantAttackViewModel("Weapon", best + App.PM.Rules.RankBonus(GameRules.RankIdFor(true), App.PM.ProficiencyBonusForLevel(rt.Level)), dmg, "", 0));
                    }
                }
                c.BaseMaxActions = Math.Max(0, baseA + addA);
                c.BaseMaxBonusActions = Math.Max(0, baseB + addB);
                c.BaseMaxReactions = Math.Max(0, App.PM.CombatBaseReactions);
                c.SurgeActionGrant = surgeA;
                c.SurgeBonusGrant = surgeB;
                c.SurgeUsesMax = surgeUses;
                c.ResetTurnEconomy();

                if (c.IsPlayerCharacter && !string.IsNullOrEmpty(c.Id))
                {
                    var slots = await App.PM.ResolveSpellSlotsAsync(c.Id);
                    if (slots.Count > 0) c.SetSpellSlots(slots);
                }
                Initiative.NotifyStateChanged();
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Session] economy init failed", ex);
            }
        }

        private string UniqueCombatantName(string baseName)
        {
            if (Initiative.Combatants.All(c => c.Name != baseName)) return baseName;
            for (var n = 2; ; n++)
            {
                var candidate = baseName + " " + n;
                if (Initiative.Combatants.All(c => c.Name != candidate)) return candidate;
            }
        }

        private async void OnInitiativeCleared()
        {
            try
            {
                if (CurrentRole != UserRole.Dm || App.PM == null || string.IsNullOrEmpty(_encounterId)) return;
                await PersistSnapshotAsync(Initiative.BuildSnapshot(_encounterId, MapId));
                await App.PM.EndEncounterAsync(_encounterId);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[Map] clearing the initiative did not reach the db", ex);
                NavItem.NavError?.Invoke("Couldn't clear the saved fight, it may come back when you reopen the map.");
            }
        }

        private async Task PersistSnapshotAsync(CombatStateMessage snapshot)
        {
            if (App.PM == null) return;

            var enc = new Encounter
            {
                Id = snapshot.EncounterId,
                CampaignId = App.PM.GetCampaignId(),
                MapId = MapId,
                Round = snapshot.Round,
                ActiveCombatantId = snapshot.ActiveCombatantId,
                IsActive = true
            };

            var combatants = snapshot.Combatants.Select((c, i) => new EncounterCombatant
            {
                Id = c.Id,
                EncounterId = snapshot.EncounterId,
                CharacterId = c.IsPlayerCharacter ? c.Id : null,
                TokenId = c.TokenId,
                Name = c.Name,
                Initiative = c.Initiative,
                CurrentHp = c.CurrentHp,
                MaxHp = c.MaxHp,
                IsPlayerCharacter = c.IsPlayerCharacter,
                RevealExactHp = c.RevealExactHp,
                SortOrder = i,
                MaxActions = c.MaxActions,
                ActionsRemaining = c.ActionsRemaining,
                MaxBonusActions = c.MaxBonusActions,
                BonusActionsRemaining = c.BonusActionsRemaining,
                SpellSlotsJson = c.SpellSlots ?? "",
                Concentration = c.Concentration,
                DeathSaveSuccesses = c.DeathSaveSuccesses,
                DeathSaveFailures = c.DeathSaveFailures,
                AttacksJson = c.AttacksJson ?? "",
                IsFriendly = c.IsFriendly,
                ExtrasJson = c.ExtrasJson ?? ""
            }).ToList();

            try
            {
                await App.PM.SaveEncounterAsync(enc, combatants);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Combat snapshot persist failed", ex);
                NavItem.NavError?.Invoke("Couldn't save the combat state, the fight may come back stale after a reopen.");
            }
        }
    }

    public enum CanvasToolMode { Draw, Token, Ping, Fog, Wall, Door, Template, Terrain, MapObject, Ruler }

    public enum SightLine { Clear, Cover, Blocked }

    public class WallViewModel
    {
        public string Id { get; set; } = "";
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public bool IsDoor { get; set; }
        public bool DoorOpen { get; set; }
        public bool BlocksSight { get; set; } = true;

        public WallSegment ToMessage() => new(Id, X1, Y1, X2, Y2, IsDoor, DoorOpen, BlocksSight);

        public static WallViewModel FromMessage(WallSegment s) => new()
        {
            Id = s.Id,
            X1 = s.X1,
            Y1 = s.Y1,
            X2 = s.X2,
            Y2 = s.Y2,
            IsDoor = s.IsDoor,
            DoorOpen = s.DoorOpen,
            BlocksSight = s.BlocksSight
        };
    }

    public class AoeTemplateViewModel
    {
        public string Id { get; set; } = "";
        public string Shape { get; set; } = "cone";
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double DirectionDeg { get; set; }
        public double SizeFt { get; set; }
        public double WidthFt { get; set; }
        public string Color { get; set; } = "#4F81BD";

        public string Damage { get; set; } = "";
        public string DamageType { get; set; } = "";
        public string SaveAbility { get; set; } = "";
        public int SaveDc { get; set; }
        public string Trigger { get; set; } = "";
        public string Label { get; set; } = "";
        public int RoundsLeft { get; set; }
        public string Terrain { get; set; } = "";
        public string Condition { get; set; } = "";
        public int ConditionRounds { get; set; }
        public string OwnerId { get; set; } = "";

        public bool IsHazard => !string.IsNullOrWhiteSpace(Damage) || !string.IsNullOrWhiteSpace(Condition);

        public bool Persists => RoundsLeft > 0;

        public bool ShapesGround => !string.IsNullOrWhiteSpace(Terrain);

        public AoeTemplateMessage ToMessage(string mapId) =>
            new(Id, mapId, Shape, OriginX, OriginY, DirectionDeg, SizeFt, WidthFt, Color, Damage, DamageType, SaveAbility, SaveDc, Trigger, Label, RoundsLeft, Terrain, Condition, ConditionRounds, OwnerId);

        public static AoeTemplateViewModel FromMessage(AoeTemplateMessage m) => new()
        {
            Id = m.Id,
            Shape = m.Shape,
            OriginX = m.OriginX,
            OriginY = m.OriginY,
            DirectionDeg = m.DirectionDeg,
            SizeFt = m.SizeFt,
            WidthFt = m.WidthFt,
            Color = m.Color,
            Damage = m.Damage,
            DamageType = m.DamageType,
            SaveAbility = m.SaveAbility,
            SaveDc = m.SaveDc,
            Trigger = m.Trigger,
            Label = m.Label,
            RoundsLeft = m.RoundsLeft,
            Terrain = m.Terrain,
            Condition = m.Condition,
            ConditionRounds = m.ConditionRounds,
            OwnerId = m.OwnerId
        };

        // Same per cell hit test the map view paints with, so combat can ask which token points a placed template catches, all in world pixels
        public bool Contains(double px, double py, double pxPerFoot, double defaultLineFt)
        {
            if (pxPerFoot <= 0 || SizeFt <= 0) return false;
            var sizePx = SizeFt * pxPerFoot;
            var rad = DirectionDeg * Math.PI / 180.0;
            double ux = Math.Cos(rad), uy = Math.Sin(rad);
            double perpx = -uy, perpy = ux;
            double dx = px - OriginX, dy = py - OriginY;
            if (Shape == "circle")
                return dx * dx + dy * dy <= sizePx * sizePx;
            if (Shape == "cube")
            {
                var along = dx * ux + dy * uy;
                var side = dx * perpx + dy * perpy;
                if (Math.Abs(side) > sizePx / 2) return false;
                return (App.PM?.Rules?.CubeOriginOnFace ?? true)
                    ? along >= 0 && along <= sizePx
                    : Math.Abs(along) <= sizePx / 2;
            }
            if (Shape == "line")
            {
                var widthPx = (WidthFt > 0 ? WidthFt : defaultLineFt) * pxPerFoot;
                var along = dx * ux + dy * uy;
                var side = dx * perpx + dy * perpy;
                return along >= 0 && along <= sizePx && Math.Abs(side) <= widthPx / 2;
            }
            var half = sizePx * (App.PM?.Rules?.ConeWidthRatio ?? 0.5);
            var b1x = OriginX + ux * sizePx + perpx * half;
            var b1y = OriginY + uy * sizePx + perpy * half;
            var b2x = OriginX + ux * sizePx - perpx * half;
            var b2y = OriginY + uy * sizePx - perpy * half;
            return PointInTriangle(px, py, OriginX, OriginY, b1x, b1y, b2x, b2y);
        }

        private static bool PointInTriangle(double px, double py, double ax, double ay, double bx, double by, double cx, double cy)
        {
            var d1 = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
            var d2 = (cx - bx) * (py - by) - (cy - by) * (px - bx);
            var d3 = (ax - cx) * (py - cy) - (ay - cy) * (px - cx);
            var neg = d1 < 0 || d2 < 0 || d3 < 0;
            var pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }

    public record MonsterPlacement(TokenViewModel Token, MonsterOption Monster, int? InitiativeOverride);

    public class MapCanvasViewModel : ViewModelBase
    {
        public string MapId { get; }
        private readonly CommunicationController? _com;
        private readonly IDisposable? _gridSub;

        public ObservableCollection<StrokeViewModel> Strokes { get; } = new();
        public ObservableCollection<TokenViewModel> Tokens { get; } = new();
        public ObservableCollection<PingViewModel> ActivePings { get; } = new();
        public ObservableCollection<Bitmap> TokenLibrary { get; } = new();
        public ObservableCollection<TokenLibraryEntryViewModel> Library { get; } = new();
        public ObservableCollection<MonsterOption> Monsters { get; } = new();
        private readonly Dictionary<string, CampaignTokenAsset> _assetById = new();

        private readonly Dictionary<string, Bitmap> _imageById = new();

        private static readonly Random _rng = new();

        private int _selectedTokenIndex;
        public int SelectedTokenIndex
        {
            get => _selectedTokenIndex;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedTokenIndex, value);
                this.RaisePropertyChanged(nameof(SelectedTokenPreview));
                ResolveSelectedImageId();
            }
        }
        private TokenViewModel? _selectedToken;
        public TokenViewModel? SelectedToken
        {
            get => _selectedToken;
            set
            {
                if (_selectedToken != null) _selectedToken.IsSelected = false;
                this.RaiseAndSetIfChanged(ref _selectedToken, value);
                if (_selectedToken != null) _selectedToken.IsSelected = true;
                this.RaisePropertyChanged(nameof(SelectedTokenSize));
                this.RaisePropertyChanged(nameof(CanOpenSelectedSheet));
                this.RaisePropertyChanged(nameof(SelectedIsProp));
                this.RaisePropertyChanged(nameof(SelectedPropBlocks));
                this.RaisePropertyChanged(nameof(SelectedPropBlocksSight));
                this.RaisePropertyChanged(nameof(SelectedPropSquares));
            }
        }

        public bool CanOpenSelectedSheet => !string.IsNullOrEmpty(SelectedToken?.CharacterId);
        public event Action<string>? OpenSheetRequested;

        public CreatureSize[] CreatureSizes { get; } = Enum.GetValues<CreatureSize>();

        public CreatureSize? SelectedTokenSize
        {
            get => SelectedToken?.Size;
            set
            {
                if (SelectedToken == null || value == null) return;
                _ = SetTokenSize(SelectedToken, value.Value);
                this.RaisePropertyChanged();
            }
        }


        private Bitmap? _backgroundImage;
        public Bitmap? BackgroundImage
        {
            get => _backgroundImage;
            private set
            {
                this.RaiseAndSetIfChanged(ref _backgroundImage, value);
                this.RaisePropertyChanged(nameof(MapPixelWidth));
                this.RaisePropertyChanged(nameof(MapPixelHeight));
                this.RaisePropertyChanged(nameof(FogCols));
                this.RaisePropertyChanged(nameof(FogRows));
            }
        }


        private string? _selectedTokenImageId;
        private Bitmap? _selectedEntryPreview;

        public Bitmap? SelectedTokenPreview =>
            _selectedEntryPreview
            ?? (TokenLibrary.Count > 0 && SelectedTokenIndex < TokenLibrary.Count
                ? TokenLibrary[SelectedTokenIndex]
                : null);

        private TokenLibraryEntryViewModel? _selectedLibraryEntry;
        public TokenLibraryEntryViewModel? SelectedLibraryEntry
        {
            get => _selectedLibraryEntry;
            set => this.RaiseAndSetIfChanged(ref _selectedLibraryEntry, value);
        }

        private string _newTokenColor = "#FFD700";
        public string NewTokenColor { get => _newTokenColor; set => this.RaiseAndSetIfChanged(ref _newTokenColor, value); }

        private string _newTokenGlyph = "";
        public string NewTokenGlyph { get => _newTokenGlyph; set => this.RaiseAndSetIfChanged(ref _newTokenGlyph, value ?? ""); }

        private string _newTokenName = "";
        public string NewTokenName { get => _newTokenName; set => this.RaiseAndSetIfChanged(ref _newTokenName, value ?? ""); }

        private decimal? _newTokenInitiative;
        public decimal? NewTokenInitiative { get => _newTokenInitiative; set => this.RaiseAndSetIfChanged(ref _newTokenInitiative, value); }

        private MonsterOption? _selectedMonster;
        public MonsterOption? SelectedMonster
        {
            get => _selectedMonster;
            set => this.RaiseAndSetIfChanged(ref _selectedMonster, value);
        }

        private CanvasToolMode _mode = CanvasToolMode.Draw;
        public CanvasToolMode Mode
        {
            get => _mode;
            set
            {
                this.RaiseAndSetIfChanged(ref _mode, value);
                this.RaisePropertyChanged(nameof(IsDrawMode));
                this.RaisePropertyChanged(nameof(IsTokenMode));
                this.RaisePropertyChanged(nameof(IsPingMode));
                this.RaisePropertyChanged(nameof(IsFogMode));
                this.RaisePropertyChanged(nameof(IsWallMode));
                this.RaisePropertyChanged(nameof(IsDoorMode));
                this.RaisePropertyChanged(nameof(IsTemplateMode));
                this.RaisePropertyChanged(nameof(IsTerrainMode));
                this.RaisePropertyChanged(nameof(IsMapObjectMode));
                this.RaisePropertyChanged(nameof(IsRulerMode));
                this.RaisePropertyChanged(nameof(ShowColorTools));
            }
        }

        public bool IsDrawMode => Mode == CanvasToolMode.Draw;
        public bool IsTokenMode => Mode == CanvasToolMode.Token;
        public bool IsPingMode => Mode == CanvasToolMode.Ping;
        public bool IsFogMode => Mode == CanvasToolMode.Fog;
        public bool IsWallMode => Mode == CanvasToolMode.Wall;
        public bool IsDoorMode => Mode == CanvasToolMode.Door;
        public bool IsTemplateMode => Mode == CanvasToolMode.Template;
        public bool IsTerrainMode => Mode == CanvasToolMode.Terrain;
        public bool IsMapObjectMode => Mode == CanvasToolMode.MapObject;
        public bool IsRulerMode => Mode == CanvasToolMode.Ruler;

        public bool ShowColorTools => Mode == CanvasToolMode.Draw || Mode == CanvasToolMode.Ping;

        private bool _toolsVisible = true;
        public bool ToolsVisible
        {
            get => _toolsVisible;
            set => this.RaiseAndSetIfChanged(ref _toolsVisible, value);
        }

        private bool _showGrid = true;
        public bool ShowGrid
        {
            get => _showGrid;
            set => this.RaiseAndSetIfChanged(ref _showGrid, value);
        }

        private bool _tokensEnabled = true;
        public bool TokensEnabled
        {
            get => _tokensEnabled;
            set => this.RaiseAndSetIfChanged(ref _tokensEnabled, value);
        }

        private GridKind _gridKind = GridKind.Squares;
        public GridKind GridKind
        {
            get => _gridKind;
            set => this.RaiseAndSetIfChanged(ref _gridKind, value);
        }

        public GridKind[] GridKinds { get; } = Enum.GetValues<GridKind>();

        private bool _snapToGrid = true;
        public bool SnapToGrid
        {
            get => _snapToGrid;
            set => this.RaiseAndSetIfChanged(ref _snapToGrid, value);
        }

        private double _mapScale = 1.0;
        public double MapScale
        {
            get => _mapScale;
            set
            {
                this.RaiseAndSetIfChanged(ref _mapScale, value);
                this.RaisePropertyChanged(nameof(CellSize));
                this.RaisePropertyChanged(nameof(CellPixels));
                this.RaisePropertyChanged(nameof(GridSummary));
                foreach (var t in Tokens) t.CellSize = CellSize;
            }
        }

        public double CellSize => GridOverlay.CellFor(MapScale);

        public decimal CellPixels
        {
            get => (decimal)CellSize;
            set
            {
                var px = (double)value;
                if (px <= 0) return;
                MapScale = px / GridOverlay.BaseCellPx;
            }
        }

        public string GridSummary
        {
            get
            {
                if (MapPixelWidth <= 0 || MapPixelHeight <= 0) return "No map size yet.";
                var cols = MapPixelWidth / CellSize;
                var rows = MapPixelHeight / CellSize;
                var line = cols.ToString("0.#", CultureInfo.InvariantCulture) + " x " + rows.ToString("0.#", CultureInfo.InvariantCulture) + " squares";
                if (cols < 4 || rows < 4) line += ", that cell size is almost certainly wrong";
                return line;
            }
        }

        private (GridKind Kind, double Scale) _loadedGrid = (GridKind.Squares, 1.0);

        public void LoadGrid(GridKind kind, double scale)
        {
            GridKind = kind;
            MapScale = scale;
            _loadedGrid = (kind, scale);
        }

        private async Task PersistGridAsync()
        {
            if (!IsHost || App.PM == null) return;
            if (GridKind == _loadedGrid.Kind && Math.Abs(MapScale - _loadedGrid.Scale) < 0.0001) return;
            try
            {
                await App.PM.SetMapGridAsync(MapId, MapScale, GridKind);
                _loadedGrid = (GridKind, MapScale);
                if (_com != null && IsBroadcasting)
                    await _com.ActivateMapAsync(MapId, GridKind.ToString(), MapScale,
                        (int)Math.Round(MapPixelWidth), (int)Math.Round(MapPixelHeight));
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[Canvas] grid save failed", ex);
                NavItem.NavError?.Invoke("Couldn't save the grid, the map will come back on the old one.");
            }
        }

        public Point SnapPoint(double x, double y, double footprintPx) =>
            SnapToGrid ? GridOverlay.SnapCenter(x, y, footprintPx, CellSize) : new Point(x, y);

        public Point SnapPointFor(TokenViewModel token, double x, double y, double footprintPx) =>
            token.IsProp && !NewPropSnap ? new Point(x, y) : SnapPoint(x, y, footprintPx);

        private double _tokenRotation;
        public double TokenRotation
        {
            get => _tokenRotation;
            set => this.RaiseAndSetIfChanged(ref _tokenRotation, value);
        }

        private double _tokenScale = 1.0;
        public double TokenScale
        {
            get => _tokenScale;
            set => this.RaiseAndSetIfChanged(ref _tokenScale, value);
        }

        private bool _isBroadcasting;
        public bool IsBroadcasting
        {
            get => _isBroadcasting;
            set => this.RaiseAndSetIfChanged(ref _isBroadcasting, value);
        }

        private bool _isHost;
        public bool IsHost
        {
            get => _isHost;
            set
            {
                this.RaiseAndSetIfChanged(ref _isHost, value);
                this.RaisePropertyChanged(nameof(CanUploadTokens));
                this.RaisePropertyChanged(nameof(CanEditFog));
                this.RaisePropertyChanged(nameof(CanEditWalls));
            }
        }

        public bool CanUploadTokens => IsHost;

        public bool CanEditFog => IsHost;

        public bool CanEditWalls => IsHost;

        private bool _isDungeonMaster;
        public bool IsDungeonMaster
        {
            get => _isDungeonMaster;
            set => this.RaiseAndSetIfChanged(ref _isDungeonMaster, value);
        }

        private bool _fogEnabled;
        public bool FogEnabled
        {
            get => _fogEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _fogEnabled, value);
                FogBulkChanged?.Invoke();
            }
        }

        private bool _dynamicVisionEnabled;
        public bool DynamicVisionEnabled
        {
            get => _dynamicVisionEnabled;
            set => this.RaiseAndSetIfChanged(ref _dynamicVisionEnabled, value);
        }
        public double VisionRadiusFeet { get; set; } = 60;

        private bool _fogHide = true;
        public bool FogHide
        {
            get => _fogHide;
            set => this.RaiseAndSetIfChanged(ref _fogHide, value);
        }

        private readonly HashSet<(int Col, int Row)> _fogHidden = new();
        public IReadOnlyCollection<(int Col, int Row)> FogHiddenCells => _fogHidden;

        public int FogCols => CellSize > 0 ? (int)Math.Ceiling(MapPixelWidth / CellSize) : 0;
        public int FogRows => CellSize > 0 ? (int)Math.Ceiling(MapPixelHeight / CellSize) : 0;

        private double _mapWidthOverride;
        private double _mapHeightOverride;

        // Blank map has no art to measure, so the row's own size is the edge. The template says how big a fresh one starts.
        public double MapPixelWidth
        {
            get
            {
                if (BackgroundImage != null && BackgroundImage.PixelSize.Width > 0) return BackgroundImage.PixelSize.Width;
                return _mapWidthOverride > 0 ? _mapWidthOverride : (App.PM?.Rules?.BlankMapWidthPx ?? 2560);
            }
        }

        public double MapPixelHeight
        {
            get
            {
                if (BackgroundImage != null && BackgroundImage.PixelSize.Height > 0) return BackgroundImage.PixelSize.Height;
                return _mapHeightOverride > 0 ? _mapHeightOverride : (App.PM?.Rules?.BlankMapHeightPx ?? 1600);
            }
        }

        public void SetMapSize(double width, double height)
        {
            _mapWidthOverride = width;
            _mapHeightOverride = height;
            this.RaisePropertyChanged(nameof(MapPixelWidth));
            this.RaisePropertyChanged(nameof(MapPixelHeight));
            this.RaisePropertyChanged(nameof(FogCols));
            this.RaisePropertyChanged(nameof(FogRows));
            this.RaisePropertyChanged(nameof(GridSummary));
        }

        public bool IsInsideMap(double centerX, double centerY, double footprintPx)
        {
            if (!(App.PM?.Rules?.ConfineToMapBounds ?? true)) return true;
            var half = footprintPx / 2.0;
            return centerX - half >= -0.01
                   && centerY - half >= -0.01
                   && centerX + half <= MapPixelWidth + 0.01
                   && centerY + half <= MapPixelHeight + 0.01;
        }

        public (double X, double Y)? NearestFreeSquare(TokenViewModel token)
        {
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            var cols = Math.Max(1, (int)(MapPixelWidth / cell));
            var rows = Math.Max(1, (int)(MapPixelHeight / cell));
            var footprint = token.PixelSize;

            (double X, double Y)? best = null;
            var bestDist = double.MaxValue;
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var cx = c * cell + cell / 2.0;
                    var cy = r * cell + cell / 2.0;
                    if (!IsInsideMap(cx, cy, footprint)) continue;
                    if (IsAreaOccupied(cx, cy, footprint, token)) continue;
                    var dx = cx - token.X;
                    var dy = cy - token.Y;
                    var d = dx * dx + dy * dy;
                    if (d >= bestDist) continue;
                    bestDist = d;
                    best = (cx, cy);
                }
            return best;
        }

        public async Task NudgeIntoBounds(TokenViewModel token)
        {
            if (token == null) return;
            var spot = NearestFreeSquare(token);
            if (spot == null) return;
            token.X = spot.Value.X;
            token.Y = spot.Value.Y;
            token.FeetAnchorX = token.X;
            token.FeetAnchorY = token.Y;
            UpdateReachableForActive();
            RemeasureRuler();
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.MoveTokenAsync(new TokenMovedMessage(token.Id, new SerializablePoint(token.X, token.Y)));
                if (IsHost) await PersistTokenAsync(token);
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] nudge back onto the map failed", ex); }
        }

        public bool CellInsideMap(int col, int row)
        {
            if (!(App.PM?.Rules?.ConfineToMapBounds ?? true)) return true;
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            return col >= 0 && row >= 0 && (col + 1) * cell <= MapPixelWidth + 0.01 && (row + 1) * cell <= MapPixelHeight + 0.01;
        }

        private readonly List<FogCellPoint> _pendingFogPaint = new();
        private bool _pendingFogHide;

        public event Action<int, int, bool>? FogCellChanged;
        public event Action? FogBulkChanged;

        public string? MyCharacterId { get; set; }

        public bool CanMoveToken(TokenViewModel token)
        {
            if (IsHost || IsDungeonMaster) return true;
            if (string.IsNullOrEmpty(MyCharacterId)) return false;
            if (token.CharacterId == MyCharacterId) return true;
            return string.IsNullOrEmpty(token.CharacterId) && token.CombatantId == MyCharacterId;
        }

        private string _myColor = "#FFD700";
        public string MyColor
        {
            get => _myColor;
            set
            {
                this.RaiseAndSetIfChanged(ref _myColor, value);
                CurrentStrokeColor = value;
            }
        }

        private string _colorWarning = "";
        public string ColorWarning
        {
            get => _colorWarning;
            set => this.RaiseAndSetIfChanged(ref _colorWarning, value);
        }

        public string CurrentStrokeColor { get; set; } = "#FFD700";
        public double CurrentStrokeThickness { get; set; } = 2;

        public ReactiveCommand<Unit, Unit> SetDrawModeCommand { get; }
        public ReactiveCommand<Unit, Unit> SetTokenModeCommand { get; }
        public ReactiveCommand<Unit, Unit> SetPingModeCommand { get; }
        public ReactiveCommand<Unit, Unit> UploadTokenCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenLibraryCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenSheetCommand { get; }
        public ReactiveCommand<Unit, Unit> RotateLeftCommand { get; }
        public ReactiveCommand<Unit, Unit> RotateRightCommand { get; }
        public ReactiveCommand<string, Unit> RequestColorCommand { get; }
        public ReactiveCommand<Unit, Unit> CreateColorTokenCommand { get; }
        public ReactiveCommand<string, Unit> SetNewTokenColorCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleToolsCommand { get; }
        public ReactiveCommand<Unit, Unit> SetFogModeCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleFogEnabledCommand { get; }
        public ReactiveCommand<Unit, Unit> FogHideBrushCommand { get; }
        public ReactiveCommand<Unit, Unit> FogRevealBrushCommand { get; }
        public ReactiveCommand<Unit, Unit> HideAllFogCommand { get; }
        public ReactiveCommand<Unit, Unit> RevealAllFogCommand { get; }
        public ReactiveCommand<Unit, Unit> SetWallModeCommand { get; }
        public ReactiveCommand<Unit, Unit> SetDoorModeCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleWallsEnabledCommand { get; }
        public ReactiveCommand<Unit, Unit> SetTemplateModeCommand { get; }
        public ReactiveCommand<Unit, Unit> SetTerrainModeCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearDifficultTerrainCommand { get; }
        public ReactiveCommand<Unit, Unit> SetMapObjectModeCommand { get; }
        public ReactiveCommand<Unit, Unit> SetRulerModeCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearRulerCommand { get; }
        public ReactiveCommand<Unit, Unit> UploadPropCommand { get; }
        public ReactiveCommand<Unit, Unit> DisarmPropCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteSelectedPropCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearMapObjectsCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearTemplatesCommand { get; }
        public ReactiveCommand<Unit, Unit> ArmClearTemplatesCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelClearTemplatesCommand { get; }

        private bool _confirmingClearTemplates;
        public bool ConfirmingClearTemplates
        {
            get => _confirmingClearTemplates;
            set => this.RaiseAndSetIfChanged(ref _confirmingClearTemplates, value);
        }

        public event Action? UploadTokenRequested;
        public event Action? LibraryRequested;
        public event Action<MonsterPlacement>? MonsterTokenPlaced;

        public MapHubViewModel? Hub { get; set; }

        private readonly Stack<(Action Undo, Action Redo)> _undoStack = new();
        private readonly Stack<(Action Undo, Action Redo)> _redoStack = new();
        public bool CanUndo => _undoStack.Count > 0 || MyLastStroke() != null;
        public bool CanRedo => _redoStack.Count > 0;

        private StrokeViewModel? MyLastStroke()
        {
            var me = App.PM?.GetUID();
            if (string.IsNullOrEmpty(me)) return null;
            for (int i = Strokes.Count - 1; i >= 0; i--)
                if (string.Equals(Strokes[i].OwnerId, me, StringComparison.OrdinalIgnoreCase)) return Strokes[i];
            return null;
        }

        private async Task RemoveMyLastStrokeAsync()
        {
            var mine = MyLastStroke();
            if (mine == null) return;
            Strokes.Remove(mine);
            this.RaisePropertyChanged(nameof(CanUndo));
            try
            {
                if (IsHost) await App.PM.GameDataRepo.DeleteStrokeAsync(mine.Id);
                if (_com != null && IsBroadcasting) await _com.UndoStrokeAsync(mine.Id);
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] stroke undo failed", ex); }
        }
        public ReactiveCommand<Unit, Unit> UndoCommand { get; }
        public ReactiveCommand<Unit, Unit> RedoCommand { get; }

        private void PushEdit(Action undo, Action redo)
        {
            _undoStack.Push((undo, redo));
            _redoStack.Clear();
            this.RaisePropertyChanged(nameof(CanUndo));
            this.RaisePropertyChanged(nameof(CanRedo));
        }

        private void DoUndo()
        {
            if (_undoStack.Count == 0) { _ = RemoveMyLastStrokeAsync(); return; }
            var e = _undoStack.Pop();
            e.Undo();
            _redoStack.Push(e);
            this.RaisePropertyChanged(nameof(CanUndo));
            this.RaisePropertyChanged(nameof(CanRedo));
        }

        private void DoRedo()
        {
            if (_redoStack.Count == 0) return;
            var e = _redoStack.Pop();
            e.Redo();
            _undoStack.Push(e);
            this.RaisePropertyChanged(nameof(CanUndo));
            this.RaisePropertyChanged(nameof(CanRedo));
        }

        public MapCanvasViewModel() : this(string.Empty, null) { }

        public MapCanvasViewModel(string mapId, CommunicationController? com)
        {
            UndoCommand = ReactiveCommand.Create(DoUndo);
            RedoCommand = ReactiveCommand.Create(DoRedo);
            MapId = mapId;
            _com = com;

            _gridSub = this.WhenAnyValue(x => x.MapScale, x => x.GridKind, (_, _) => Unit.Default)
                .Skip(1)
                .Throttle(TimeSpan.FromMilliseconds(400))
                .ObserveOn(RxApp.MainThreadScheduler)
                .SelectMany(_ => Observable.FromAsync(PersistGridAsync))
                .Subscribe();

            Strokes.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(CanUndo));
            WallsChanged += RemeasureRuler;
            ObjectsChanged += RemeasureRuler;
            ObjectCellChanged += (_, _, _) => RemeasureRuler();

            SetDrawModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Draw; });
            SetTokenModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Token; });
            SetPingModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Ping; });
            UploadTokenCommand = ReactiveCommand.Create(() => UploadTokenRequested?.Invoke());
            OpenLibraryCommand = ReactiveCommand.Create(() => LibraryRequested?.Invoke());
            OpenSheetCommand = ReactiveCommand.Create(() =>
            {
                var id = SelectedToken?.CharacterId;
                if (!string.IsNullOrEmpty(id)) OpenSheetRequested?.Invoke(id);
            });
            RotateLeftCommand = ReactiveCommand.Create(() => { _ = RotateSelection(-15); });
            RotateRightCommand = ReactiveCommand.Create(() => { _ = RotateSelection(15); });
            RequestColorCommand = ReactiveCommand.CreateFromTask<string>(RequestColorAsync);
            CreateColorTokenCommand = ReactiveCommand.CreateFromTask(CreateColorTokenAsync);
            SetNewTokenColorCommand = ReactiveCommand.Create<string>(c => { NewTokenColor = c; });
            ToggleToolsCommand = ReactiveCommand.Create(() => { ToolsVisible = !ToolsVisible; });
            SetFogModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Fog; });
            ToggleFogEnabledCommand = ReactiveCommand.CreateFromTask(ToggleFogEnabledAsync);
            FogHideBrushCommand = ReactiveCommand.Create(() => { FogHide = true; Mode = CanvasToolMode.Fog; });
            FogRevealBrushCommand = ReactiveCommand.Create(() => { FogHide = false; Mode = CanvasToolMode.Fog; });
            HideAllFogCommand = ReactiveCommand.CreateFromTask(HideAllFogAsync);
            RevealAllFogCommand = ReactiveCommand.CreateFromTask(RevealAllFogAsync);
            SetWallModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Wall; });
            SetDoorModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Door; });
            ToggleWallsEnabledCommand = ReactiveCommand.CreateFromTask(ToggleWallsEnabledAsync);
            SetTemplateModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Template; });
            SetTerrainModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Terrain; });
            ClearDifficultTerrainCommand = ReactiveCommand.CreateFromTask(ClearDifficultTerrainAsync);
            SetMapObjectModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.MapObject; });
            SetRulerModeCommand = ReactiveCommand.Create(() => { Mode = CanvasToolMode.Ruler; });
            ClearRulerCommand = ReactiveCommand.Create(ClearRuler);
            UploadPropCommand = ReactiveCommand.Create(() => PropUploadRequested?.Invoke());
            DisarmPropCommand = ReactiveCommand.Create(DisarmProp);
            DeleteSelectedPropCommand = ReactiveCommand.Create(() =>
            {
                var doomed = SelectedToken;
                if (doomed == null || !doomed.IsProp) return;
                SelectedToken = null;
                _ = RemoveToken(doomed);
            });
            ClearMapObjectsCommand = ReactiveCommand.CreateFromTask(ClearMapObjectsAsync);
            ClearTemplatesCommand = ReactiveCommand.CreateFromTask(ClearTemplatesAsync);
            ArmClearTemplatesCommand = ReactiveCommand.Create(() => { ConfirmingClearTemplates = true; });
            CancelClearTemplatesCommand = ReactiveCommand.Create(() => { ConfirmingClearTemplates = false; });
            if (_com != null)
            {
                _com.OnStrokeReceived += HandleStrokeReceived;
                _com.OnTokenAdded += HandleTokenAdded;
                _com.OnTokenMoved += HandleTokenMoved;
                _com.OnTokenRemoved += HandleTokenRemoved;
                _com.OnStrokeUndone += HandleStrokeUndone;
                _com.OnTokenResized += HandleTokenResized;
                _com.OnPingReceived += HandlePingReceived;
                _com.OnTokenRotated += HandleTokenRotated;
                _com.OnPlayerColorChanged += HandleColorChanged;
                _com.OnFogPainted += HandleFogPainted;
                _com.OnFogUpdated += HandleFogUpdated;
                _com.OnWallsUpdated += HandleWallsUpdated;
                _com.OnTerrainUpdated += HandleTerrainUpdated;
                _com.OnMapObjectsUpdated += HandleMapObjectsUpdated;
                _com.OnDoorToggled += HandleDoorToggled;
                _com.OnAoeTemplatePlaced += HandleAoeTemplatePlaced;
                _com.OnAoeTemplatesCleared += HandleAoeTemplatesCleared;
            }
        }

        public void SetBackgroundImage(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                BackgroundImage = null;
                return;
            }
            try
            {
                BackgroundImage = new Bitmap(path);
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Canvas] Failed to load map image", ex);
                BackgroundImage = null;
            }
        }


        public async Task AddStroke(StrokeViewModel stroke)
        {
            Strokes.Add(stroke);
            try
            {
                var msg = new StrokeMessage(
                    stroke.Id,
                    stroke.Points.Select(p => new SerializablePoint(p.X, p.Y)).ToList(),
                    stroke.Color,
                    stroke.Thickness,
                    stroke.OwnerId);
                if (IsHost) await App.PM.GameDataRepo.SaveStrokeAsync(MapId, msg);
                if (_com != null && IsBroadcasting) await _com.SendStrokeAsync(msg);
                PushEdit(
                    () => { Strokes.Remove(stroke); if (IsHost) _ = App.PM.GameDataRepo.DeleteStrokeAsync(stroke.Id); if (_com != null && IsBroadcasting) _ = _com.UndoStrokeAsync(stroke.Id); },
                    () => { if (!Strokes.Contains(stroke)) Strokes.Add(stroke); if (IsHost) _ = App.PM.GameDataRepo.SaveStrokeAsync(MapId, msg); if (_com != null && IsBroadcasting) _ = _com.SendStrokeAsync(msg); });
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] stroke send failed", ex); }
        }

        public async Task AddTokenBitmap(Bitmap bmp) => await AddTokenBitmap(bmp, null);

        public async Task AddTokenBitmap(Bitmap bmp, string? fileName)
        {
            var id = Guid.NewGuid().ToString("N");
            TokenLibrary.Add(bmp);
            _imageById[id] = bmp;

            var name = !string.IsNullOrWhiteSpace(fileName) ? fileName
                : string.IsNullOrWhiteSpace(NewTokenName) ? "Token " + (Library.Count + 1) : NewTokenName.Trim();
            var asset = new CampaignTokenAsset
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                Name = name,
                Kind = "image",
                MonsterKey = SelectedMonster?.Id,
                SizeName = "Medium",
                InitiativeOverride = ParseInitiativeOverride()
            };
            var entry = new TokenLibraryEntryViewModel(asset, bmp, id, SelectLibraryEntry, RemoveLibraryEntryAsync) { MonsterName = SelectedMonster?.Name };
            Library.Add(entry);
            _assetById[asset.Id] = asset;

            if (IsHost)
            {
                try
                {
                    asset.ImagePath = TokenImageGuard.SaveForCampaign(App.PM.GetCampaignId(), BitmapToPng(bmp));
                    await App.PM.GameDataRepo.SaveTokenLibraryAsync(asset);
                }
                catch (Exception ex) { ErrorLog.Log($"[Canvas] library save failed", ex); }
            }

            NewTokenName = "";
            NewTokenInitiative = null;
            SelectLibraryEntry(entry);
        }

        public async Task<int> ImportTokenFilesAsync(IEnumerable<IStorageItem> picked)
        {
            var files = await ExpandToFilesAsync(picked);
            if (files.Count == 0) return 0;

            var landed = 0;
            var skipped = new List<string>();
            var useFileNames = files.Count > 1;

            foreach (var f in files)
            {
                var raw = await ReadPickedFileAsync(f);
                if (raw == null)
                {
                    skipped.Add(f.Name);
                    continue;
                }

                var clean = await Task.Run(() => TokenImageGuard.Sanitize(raw));
                if (clean == null)
                {
                    skipped.Add(f.Name);
                    continue;
                }

                using var ms = new MemoryStream(clean);
                await AddTokenBitmap(new Bitmap(ms), useFileNames ? Path.GetFileNameWithoutExtension(f.Name) : null);
                landed++;
            }

            ReportSkippedFiles(skipped, files.Count);
            return landed;
        }

        private static async Task<List<IStorageFile>> ExpandToFilesAsync(IEnumerable<IStorageItem> picked)
        {
            var files = new List<IStorageFile>();
            foreach (var item in picked)
            {
                if (item is IStorageFile file) { files.Add(file); continue; }
                if (item is not IStorageFolder folder) continue;

                try
                {
                    await foreach (var child in folder.GetItemsAsync())
                        if (child is IStorageFile inside) files.Add(inside);
                }
                catch (Exception ex) { ErrorLog.Log("[Canvas] dropped folder could not be listed", ex); }
            }

            if (files.Count == 0) NavItem.NavError?.Invoke("Nothing in that drop was a file I could read.");
            return files;
        }

        private static async Task<byte[]?> ReadPickedFileAsync(IStorageFile file)
        {
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[Canvas] picked file could not be read", ex);
                return null;
            }
        }

        private static void ReportSkippedFiles(List<string> skipped, int total)
        {
            if (skipped.Count == 0) return;
            var named = string.Join(", ", skipped.Take(3));
            if (skipped.Count > 3) named += " and " + (skipped.Count - 3) + " more";

            if (skipped.Count == total) NavItem.NavError?.Invoke("Nothing went in, too big or not a picture, " + named);
            else NavItem.NavError?.Invoke("Kept " + (total - skipped.Count) + " of " + total + ", the rest were too big or not pictures, " + named);
        }

        public async Task CreateColorTokenAsync()
        {
            var color = string.IsNullOrWhiteSpace(NewTokenColor) ? "#FFD700" : NewTokenColor.Trim();
            var name = string.IsNullOrWhiteSpace(NewTokenName) ? (SelectedMonster?.Name ?? "Color token") : NewTokenName.Trim();
            var glyph = string.IsNullOrWhiteSpace(NewTokenGlyph)
                ? (string.IsNullOrEmpty(name) ? "" : name.Substring(0, 1).ToUpperInvariant())
                : NewTokenGlyph.Trim();

            var bmp = RenderColorToken(color, glyph);
            if (bmp == null) return;

            var id = Guid.NewGuid().ToString("N");
            TokenLibrary.Add(bmp);
            _imageById[id] = bmp;

            var asset = new CampaignTokenAsset
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                Name = name,
                Kind = "color",
                ColorHex = color,
                Glyph = glyph,
                MonsterKey = SelectedMonster?.Id,
                SizeName = "Medium",
                InitiativeOverride = ParseInitiativeOverride()
            };
            var entry = new TokenLibraryEntryViewModel(asset, bmp, id, SelectLibraryEntry, RemoveLibraryEntryAsync) { MonsterName = SelectedMonster?.Name };
            Library.Add(entry);
            _assetById[asset.Id] = asset;

            if (IsHost)
            {
                try
                {
                    asset.ImagePath = TokenImageGuard.SaveForCampaign(App.PM.GetCampaignId(), BitmapToPng(bmp));
                    await App.PM.GameDataRepo.SaveTokenLibraryAsync(asset);
                }
                catch (Exception ex) { ErrorLog.Log($"[Canvas] color token save failed", ex); }
            }

            NewTokenName = "";
            NewTokenGlyph = "";
            NewTokenInitiative = null;
            SelectLibraryEntry(entry);
        }

        public void SelectLibraryEntry(TokenLibraryEntryViewModel? entry)
        {
            if (entry == null) return;
            foreach (var e in Library) e.IsSelected = ReferenceEquals(e, entry);
            _selectedLibraryEntry = entry;
            this.RaisePropertyChanged(nameof(SelectedLibraryEntry));

            if (entry.Preview != null) _imageById[entry.ImageId] = entry.Preview;
            _selectedTokenImageId = entry.ImageId;
            _selectedEntryPreview = entry.Preview;
            this.RaisePropertyChanged(nameof(SelectedTokenPreview));
            Mode = CanvasToolMode.Token;
        }

        public async Task RemoveLibraryEntryAsync(TokenLibraryEntryViewModel? entry)
        {
            if (entry == null) return;
            Library.Remove(entry);
            if (entry.Preview != null) TokenLibrary.Remove(entry.Preview);
            _imageById.Remove(entry.ImageId);
            _assetById.Remove(entry.Asset.Id);
            if (ReferenceEquals(_selectedLibraryEntry, entry))
            {
                _selectedLibraryEntry = null;
                _selectedEntryPreview = null;
                _selectedTokenImageId = null;
                this.RaisePropertyChanged(nameof(SelectedTokenPreview));
            }
            if (IsHost)
            {
                try { await App.PM.GameDataRepo.DeleteTokenLibraryAsync(entry.Asset.Id); }
                catch (Exception ex) { ErrorLog.Log($"[Canvas] library delete failed", ex); }
            }
        }

        private static Bitmap? RenderColorToken(string colorHex, string glyph)
        {
            try
            {
                var col = Color.Parse(colorHex);
                const int s = 64;
                var rtb = new RenderTargetBitmap(new PixelSize(s, s), new Vector(96, 96));
                using (var ctx = rtb.CreateDrawingContext())
                {
                    var fill = new SolidColorBrush(col);
                    var ring = new Pen(new SolidColorBrush(Color.FromArgb(255, 20, 20, 30)), 3);
                    ctx.DrawEllipse(fill, ring, new Point(s / 2.0, s / 2.0), s / 2.0 - 3, s / 2.0 - 3);

                    if (!string.IsNullOrEmpty(glyph))
                    {
                        var lum = 0.299 * col.R + 0.587 * col.G + 0.114 * col.B;
                        var ink = lum > 140 ? Colors.Black : Colors.White;
                        var ft = new FormattedText(
                            glyph.Length > 2 ? glyph.Substring(0, 2) : glyph,
                            CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight,
                            Typeface.Default,
                            28,
                            new SolidColorBrush(ink));
                        ctx.DrawText(ft, new Point((s - ft.Width) / 2.0, (s - ft.Height) / 2.0));
                    }
                }
                return rtb;
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Canvas] color token render failed", ex);
                return null;
            }
        }

        public async Task LoadTokenLibraryAsync()
        {
            if (!IsHost) return;
            try
            {
                Monsters.Clear();
                var monsters = await App.PM.MonsterReader.ReadAsync(App.PM.GetActiveTemplateId());
                foreach (var m in monsters) Monsters.Add(m);

                var rows = await App.PM.GameDataRepo.LoadTokenLibraryAsync(App.PM.GetCampaignId());
                foreach (var a in rows)
                {
                    if (string.Equals(a.Kind, "prop", StringComparison.OrdinalIgnoreCase))
                    {
                        if (PropLibrary.Any(e => e.Asset.Id == a.Id)) continue;
                        if (string.IsNullOrEmpty(a.ImagePath) || !File.Exists(a.ImagePath)) continue;
                        try
                        {
                            PropLibrary.Add(new TokenLibraryEntryViewModel(a, new Bitmap(a.ImagePath), Guid.NewGuid().ToString("N"), e => _ = SelectPropEntry(e), RemovePropEntryAsync));
                        }
                        catch (Exception ex) { ErrorLog.Log("[Canvas] map object library image load failed", ex); }
                        continue;
                    }

                    if (Library.Any(e => e.Asset.Id == a.Id)) continue;

                    Bitmap? bmp = null;
                    if (!string.IsNullOrEmpty(a.ImagePath) && File.Exists(a.ImagePath))
                    {
                        try { bmp = new Bitmap(a.ImagePath); }
                        catch (Exception ex) { ErrorLog.Log($"[Canvas] library image load failed", ex); }
                    }
                    if (bmp == null && string.Equals(a.Kind, "color", StringComparison.OrdinalIgnoreCase))
                        bmp = RenderColorToken(a.ColorHex ?? "#FFD700", a.Glyph ?? "");
                    if (bmp == null) continue;

                    var id = Guid.NewGuid().ToString("N");
                    _imageById[id] = bmp;
                    TokenLibrary.Add(bmp);

                    var mname = a.MonsterKey == null ? null : Monsters.FirstOrDefault(m => m.Id == a.MonsterKey)?.Name;
                    Library.Add(new TokenLibraryEntryViewModel(a, bmp, id, SelectLibraryEntry, RemoveLibraryEntryAsync) { MonsterName = mname });
                    _assetById[a.Id] = a;
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Canvas] token library load failed", ex);
            }
        }

        public TokenLibraryEntryViewModel? FindBoundEntryForMonster(string monsterId) =>
            Library.FirstOrDefault(e => string.Equals(e.Asset.MonsterKey, monsterId, StringComparison.OrdinalIgnoreCase));

        public async Task LoadPersistedTokensAsync()
        {
            if (!IsHost) return;
            try
            {
                var rows = await App.PM.GameDataRepo.LoadTokensAsync(MapId);
                foreach (var row in rows)
                {
                    if (Tokens.Any(t => t.Id == row.Id)) continue;

                    Bitmap? bmp = null;
                    if (!string.IsNullOrEmpty(row.TokenImagePath) && File.Exists(row.TokenImagePath))
                    {
                        try { bmp = new Bitmap(row.TokenImagePath); }
                        catch (Exception ex) { ErrorLog.Log($"[Canvas] token image load failed", ex); }
                    }

                    var size = Enum.TryParse<CreatureSize>(row.SizeName, out var parsed) ? parsed : CreatureSize.Medium;
                    var imageId = Guid.NewGuid().ToString("N");
                    if (bmp != null) _imageById[imageId] = bmp;

                    var token = new TokenViewModel(row.Id, bmp, row.X, row.Y)
                    {
                        Scale = row.Scale,
                        Rotation = row.Rotation,
                        Size = size,
                        CellSize = CellSize,
                        ImageId = imageId,
                        ImagePath = row.TokenImagePath,
                        CharacterId = string.IsNullOrEmpty(row.OwnerCharacterId) ? null : row.OwnerCharacterId,
                        IsProp = row.IsProp,
                        Blocks = row.Blocks,
                        BlocksSight = row.BlocksSight
                    };
                    Tokens.Add(token);
                }

                var strokes = await App.PM.GameDataRepo.LoadStrokesAsync(MapId);
                foreach (var s in strokes)
                {
                    if (Strokes.Any(x => x.Id == s.StrokeId)) continue;
                    Strokes.Add(new StrokeViewModel(s.StrokeId, s.Points.Select(p => new Avalonia.Point(p.X, p.Y)).ToList(), s.Color, s.Thickness, s.OwnerId));
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Canvas] token load failed", ex);
            }
        }

        public async Task PersistTokenAsync(TokenViewModel token)
        {
            if (DynamicVisionEnabled) _ = RevealVisionAroundAsync(token.X, token.Y);
            if (!IsHost || string.IsNullOrEmpty(token.ImagePath)) return;
            var row = new MapToken
            {
                Id = token.Id,
                MapId = MapId,
                CampaignId = App.PM.GetCampaignId(),
                OwnerCharacterId = token.CharacterId,
                X = (int)Math.Round(token.X),
                Y = (int)Math.Round(token.Y),
                TokenImagePath = token.ImagePath,
                Label = null,
                Scale = token.Scale,
                Rotation = token.Rotation,
                SizeName = token.Size.ToString(),
                IsProp = token.IsProp,
                Blocks = token.Blocks,
                BlocksSight = token.BlocksSight
            };
            try { await App.PM.GameDataRepo.SaveTokenAsync(row); }
            catch (Exception ex)
            {
                ErrorLog.Log("Token persist failed", ex);
                NavItem.NavError?.Invoke("Couldn't save the token, it may snap back to where it was after a reopen.");
            }
        }

        private static byte[]? BitmapToPng(Bitmap? bmp)
        {
            if (bmp == null) return null;
            using var ms = new MemoryStream();
            bmp.Save(ms);
            return ms.ToArray();
        }

        private static string? EncodeBitmap(Bitmap? bmp)
        {
            if (bmp == null) return null;
            using var ms = new MemoryStream();
            bmp.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }

        private static Bitmap? DecodeBitmap(string? base64, bool isProp = false)
        {
            if (string.IsNullOrEmpty(base64)) return null;
            try
            {
                var clean = TokenImageGuard.Sanitize(Convert.FromBase64String(base64), isProp);
                if (clean == null) return null;
                using var ms = new MemoryStream(clean);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        public void CycleTokenSelection()
        {
            if (Library.Count == 0) return;
            var cur = _selectedLibraryEntry == null ? -1 : Library.IndexOf(_selectedLibraryEntry);
            SelectLibraryEntry(Library[(cur + 1) % Library.Count]);
        }

        public bool IsAreaOccupied(double x, double y, double footprintPx, TokenViewModel? mover = null)
        {
            var halfA = footprintPx / 2.0;
            foreach (var t in Tokens)
            {
                if (ReferenceEquals(t, mover)) continue;
                if (t.IsProp && !t.Blocks) continue;
                if (mover != null
                    && ((!string.IsNullOrEmpty(mover.CharacterId) && t.CharacterId == mover.CharacterId)
                        || (!string.IsNullOrEmpty(mover.CombatantId) && t.CombatantId == mover.CombatantId)))
                    continue;
                var reach = halfA + t.PixelSize / 2.0 - 0.5;
                if (Math.Abs(t.X - x) < reach && Math.Abs(t.Y - y) < reach) return true;
            }
            return false;
        }

        private (double X, double Y) FindFreeCell(double footprintPx)
        {
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            var cols = Math.Max(1, (int)(MapPixelWidth / cell));
            var rows = Math.Max(1, (int)(MapPixelHeight / cell));

            var free = new List<(double, double)>();
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var cx = c * cell + cell / 2.0;
                    var cy = r * cell + cell / 2.0;
                    if (!IsAreaOccupied(cx, cy, footprintPx, null)) free.Add((cx, cy));
                }

            if (free.Count == 0) return (cell / 2.0, cell / 2.0);
            return free[_rng.Next(free.Count)];
        }

        public async Task<TokenViewModel?> SpawnCharacterTokenAsync(string characterId, string name, string? colorHex, string? imagePath, string? combatantId = null)
        {
            var bmp = CharacterTokenRenderer.Resolve(name, colorHex, imagePath)
                      ?? RenderColorToken(string.IsNullOrWhiteSpace(colorHex) ? "#90A4AE" : colorHex, CharacterTokenRenderer.Initials(name));
            if (bmp == null) return null;

            var (x, y) = FindFreeCell(CellSize);
            var imageId = Guid.NewGuid().ToString("N");
            _imageById[imageId] = bmp;

            var token = new TokenViewModel(Guid.NewGuid().ToString("N"), bmp, x, y)
            {
                CellSize = CellSize,
                ImageId = imageId,
                CharacterId = characterId,
                CombatantId = combatantId
            };
            token.FeetAnchorX = x;
            token.FeetAnchorY = y;
            token.Size = await App.PM.ResolveCharacterSizeAsync(characterId);
            Tokens.Add(token);

            if (IsHost)
            {
                token.ImagePath = TokenImageGuard.SaveForCampaign(App.PM.GetCampaignId(), BitmapToPng(bmp));
                await PersistTokenAsync(token);
            }

            if (_com != null && IsBroadcasting)
            {
                var msg = new TokenAddedMessage(
                    token.Id, imageId,
                    new SerializablePoint(x, y),
                    1.0, 0,
                    token.Size,
                    EncodeBitmap(bmp),
                    token.CharacterId);
                await _com.AddTokenAsync(msg);
            }

            return token;
        }

        public async Task<TokenViewModel?> SpawnMonsterTokenAsync(MonsterOption monster)
        {
            if (monster == null) return null;
            if (!TryFindFreeCell(CellSize, out var x, out var y)) return null;

            var color = string.IsNullOrWhiteSpace(monster.DefaultColor) ? "#BB4444" : monster.DefaultColor!;
            var bmp = RenderColorToken(color, CharacterTokenRenderer.Initials(monster.Name));
            if (bmp == null) return null;

            var imageId = Guid.NewGuid().ToString("N");
            _imageById[imageId] = bmp;

            var token = new TokenViewModel(Guid.NewGuid().ToString("N"), bmp, x, y)
            {
                CellSize = CellSize,
                ImageId = imageId
            };
            token.FeetAnchorX = x;
            token.FeetAnchorY = y;
            token.Size = Enum.TryParse<CreatureSize>(monster.Size, true, out var monSize) ? monSize : CreatureSize.Medium;
            Tokens.Add(token);

            if (IsHost)
            {
                token.ImagePath = TokenImageGuard.SaveForCampaign(App.PM.GetCampaignId(), BitmapToPng(bmp));
                await PersistTokenAsync(token);
            }

            if (_com != null && IsBroadcasting)
            {
                var msg = new TokenAddedMessage(
                    token.Id, imageId,
                    new SerializablePoint(x, y),
                    1.0, 0,
                    token.Size,
                    EncodeBitmap(bmp));
                await _com.AddTokenAsync(msg);
            }

            if (IsHost)
                MonsterTokenPlaced?.Invoke(new MonsterPlacement(token, monster, null));

            return token;
        }

        private bool TryFindFreeCell(double footprintPx, out double x, out double y)
        {
            x = 0; y = 0;
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            double w = 0, h = 0;
            if (BackgroundImage != null && BackgroundImage.PixelSize.Width > 0)
            {
                w = BackgroundImage.PixelSize.Width;
                h = BackgroundImage.PixelSize.Height;
            }
            var cols = w > 0 ? Math.Max(1, (int)(w / cell)) : 12;
            var rows = h > 0 ? Math.Max(1, (int)(h / cell)) : 12;

            var free = new List<(double, double)>();
            for (var r = 0; r < rows; r++)
                for (var c = 0; c < cols; c++)
                {
                    var cx = c * cell + cell / 2.0;
                    var cy = r * cell + cell / 2.0;
                    if (!IsAreaOccupied(cx, cy, footprintPx, null)) free.Add((cx, cy));
                }

            if (free.Count == 0) return false;
            var pick = free[_rng.Next(free.Count)];
            x = pick.Item1; y = pick.Item2;
            return true;
        }

        public async Task PlaceTokenAt(double x, double y)
        {
            if (!TokensEnabled) return;
            if (SelectedTokenPreview == null || _selectedTokenImageId == null) return;

            var snapped = SnapPoint(x, y, CellSize * TokenScale);
            x = snapped.X;
            y = snapped.Y;
            if (!IsInsideMap(x, y, CellSize * TokenScale)) return;

            var token = new TokenViewModel(
                Guid.NewGuid().ToString("N"),
                SelectedTokenPreview,
                x, y)
            {
                Scale = TokenScale,
                Rotation = TokenRotation,
                CellSize = CellSize,
                ImageId = _selectedTokenImageId
            };
            Tokens.Add(token);

            try
            {
                if (IsHost)
                {
                    token.ImagePath = TokenImageGuard.SaveForCampaign(App.PM.GetCampaignId(), BitmapToPng(SelectedTokenPreview));
                    await PersistTokenAsync(token);
                }

                if (_com != null && IsBroadcasting)
                {
                    var msg = new TokenAddedMessage(
                        token.Id, _selectedTokenImageId,
                        new SerializablePoint(x, y),
                        TokenScale, TokenRotation,
                        token.Size,
                        EncodeBitmap(SelectedTokenPreview));
                    await _com.AddTokenAsync(msg);
                }
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] token place sync failed", ex); }

            var srcEntry = _selectedLibraryEntry;
            if (IsHost && srcEntry?.Asset?.MonsterKey is string mk && !string.IsNullOrEmpty(mk))
            {
                var monster = Monsters.FirstOrDefault(m => m.Id == mk);
                if (monster != null)
                    MonsterTokenPlaced?.Invoke(new MonsterPlacement(token, monster, srcEntry.Asset.InitiativeOverride));
            }
        }

        private Bitmap? _propPreview;
        public Bitmap? PropPreview
        {
            get => _propPreview;
            private set { this.RaiseAndSetIfChanged(ref _propPreview, value); this.RaisePropertyChanged(nameof(HasPropArmed)); }
        }

        public bool HasPropArmed => _propPreview != null;

        private bool _newPropBlocks = true;
        public bool NewPropBlocks
        {
            get => _newPropBlocks;
            set => this.RaiseAndSetIfChanged(ref _newPropBlocks, value);
        }

        private bool _newPropBlocksSight;
        public bool NewPropBlocksSight
        {
            get => _newPropBlocksSight;
            set => this.RaiseAndSetIfChanged(ref _newPropBlocksSight, value);
        }

        private bool _newPropSnap = true;
        public bool NewPropSnap
        {
            get => _newPropSnap;
            set
            {
                this.RaiseAndSetIfChanged(ref _newPropSnap, value);
                PropGhost = null;
                PropGhostChanged?.Invoke();
            }
        }

        private double _newPropSquares = 1.0;
        public double NewPropSquares
        {
            get => _newPropSquares;
            set => this.RaiseAndSetIfChanged(ref _newPropSquares, Math.Max(0.25, value));
        }

        public event Action? PropUploadRequested;

        public bool SelectedIsProp => SelectedToken?.IsProp ?? false;

        public bool SelectedPropBlocks
        {
            get => SelectedToken?.Blocks ?? true;
            set
            {
                if (SelectedToken == null || SelectedToken.Blocks == value) return;
                SelectedToken.Blocks = value;
                _ = SaveSelectedProp();
                this.RaisePropertyChanged(nameof(SelectedPropBlocks));
            }
        }

        public bool SelectedPropBlocksSight
        {
            get => SelectedToken?.BlocksSight ?? false;
            set
            {
                if (SelectedToken == null || SelectedToken.BlocksSight == value) return;
                SelectedToken.BlocksSight = value;
                _ = SaveSelectedProp();
                this.RaisePropertyChanged(nameof(SelectedPropBlocksSight));
            }
        }

        public double SelectedPropSquares
        {
            get => SelectedToken?.Scale ?? 1.0;
            set
            {
                if (SelectedToken == null || Math.Abs(SelectedToken.Scale - value) < 0.001) return;
                SelectedToken.Scale = Math.Max(0.25, value);
                _ = SaveSelectedProp();
                this.RaisePropertyChanged(nameof(SelectedPropSquares));
            }
        }

        public event Action? PropGhostChanged;
        public Rect? PropGhost { get; private set; }

        private byte[]? _propBytes;

        public ObservableCollection<TokenLibraryEntryViewModel> PropLibrary { get; } = new();

        private string _newPropName = "";
        public string NewPropName
        {
            get => _newPropName;
            set => this.RaiseAndSetIfChanged(ref _newPropName, value);
        }

        public async Task<bool> ArmAndRememberPropAsync(byte[]? raw) => await ArmAndRememberPropAsync(raw, null);

        public async Task<bool> ArmAndRememberPropAsync(byte[]? raw, string? fileName)
        {
            if (!ArmPropFromBytes(raw)) return false;
            if (!IsHost || _propBytes == null) return true;

            var name = !string.IsNullOrWhiteSpace(fileName) ? fileName
                : string.IsNullOrWhiteSpace(NewPropName) ? "Object " + (PropLibrary.Count + 1) : NewPropName.Trim();
            var asset = new CampaignTokenAsset
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                Name = name,
                Kind = "prop"
            };
            try
            {
                asset.ImagePath = TokenImageGuard.SaveForCampaign(asset.CampaignId, _propBytes, true);
                if (string.IsNullOrEmpty(asset.ImagePath)) return true;

                var already = PropLibrary.FirstOrDefault(e => string.Equals(e.Asset.ImagePath, asset.ImagePath, StringComparison.OrdinalIgnoreCase));
                if (already != null)
                {
                    foreach (var e in PropLibrary) e.IsSelected = ReferenceEquals(e, already);
                    NewPropName = "";
                    return true;
                }

                await App.PM.GameDataRepo.SaveTokenLibraryAsync(asset);
                var entry = new TokenLibraryEntryViewModel(asset, PropPreview, Guid.NewGuid().ToString("N"), e => _ = SelectPropEntry(e), RemovePropEntryAsync);
                PropLibrary.Add(entry);
                foreach (var e in PropLibrary) e.IsSelected = ReferenceEquals(e, entry);
                NewPropName = "";
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] map object library save failed", ex); }
            return true;
        }

        public async Task<int> ImportPropFilesAsync(IEnumerable<IStorageItem> picked)
        {
            var files = await ExpandToFilesAsync(picked);
            if (files.Count == 0) return 0;

            var landed = 0;
            var skipped = new List<string>();
            var useFileNames = files.Count > 1;

            foreach (var f in files)
            {
                var raw = await ReadPickedFileAsync(f);
                if (raw != null && await ArmAndRememberPropAsync(raw, useFileNames ? Path.GetFileNameWithoutExtension(f.Name) : null)) landed++;
                else skipped.Add(f.Name);
            }

            ReportSkippedFiles(skipped, files.Count);
            return landed;
        }

        public async Task SelectPropEntry(TokenLibraryEntryViewModel? entry)
        {
            if (entry?.Asset.ImagePath == null || !File.Exists(entry.Asset.ImagePath)) return;
            try
            {
                if (ArmPropFromBytes(await File.ReadAllBytesAsync(entry.Asset.ImagePath)))
                    foreach (var e in PropLibrary) e.IsSelected = ReferenceEquals(e, entry);
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] map object library arm failed", ex); }
        }

        public async Task RemovePropEntryAsync(TokenLibraryEntryViewModel? entry)
        {
            if (entry == null) return;
            PropLibrary.Remove(entry);
            if (!IsHost) return;
            try { await App.PM.GameDataRepo.DeleteTokenLibraryAsync(entry.Asset.Id); }
            catch (Exception ex) { ErrorLog.Log("[Canvas] map object library delete failed", ex); }
        }

        // The picture is locked in up front so what is armed, what lands on disk and what the players receive are all the same bytes.
        public bool ArmPropFromBytes(byte[]? raw)
        {
            var clean = TokenImageGuard.Sanitize(raw, true);
            if (clean == null) return false;
            try
            {
                using var ms = new MemoryStream(clean);
                PropPreview = new Bitmap(ms);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[Canvas] map object image could not be read", ex);
                return false;
            }
            _propBytes = clean;
            Mode = CanvasToolMode.MapObject;
            return true;
        }

        public void DisarmProp()
        {
            PropPreview = null;
            _propBytes = null;
            PropGhost = null;
            PropGhostChanged?.Invoke();
        }

        public void TrackPropGhost(double x, double y)
        {
            if (PropPreview == null || Mode != CanvasToolMode.MapObject)
            {
                if (PropGhost == null) return;
                PropGhost = null;
                PropGhostChanged?.Invoke();
                return;
            }
            var footprint = CellSize * NewPropSquares;
            var snapped = NewPropSnap ? SnapPoint(x, y, footprint) : new Point(x, y);
            var next = new Rect(snapped.X - footprint / 2.0, snapped.Y - footprint / 2.0, footprint, footprint);
            if (PropGhost is { } cur && Math.Abs(cur.X - next.X) < 0.01 && Math.Abs(cur.Y - next.Y) < 0.01 && Math.Abs(cur.Width - next.Width) < 0.01) return;
            PropGhost = next;
            PropGhostChanged?.Invoke();
        }

        public async Task PlacePropAt(double x, double y)
        {
            if (!IsHost || PropPreview == null) return;
            var footprint = CellSize * NewPropSquares;
            var snapped = NewPropSnap ? SnapPoint(x, y, footprint) : new Point(x, y);
            if (!IsInsideMap(snapped.X, snapped.Y, footprint)) return;

            var imageId = Guid.NewGuid().ToString("N");
            _imageById[imageId] = PropPreview;
            var token = new TokenViewModel(Guid.NewGuid().ToString("N"), PropPreview, snapped.X, snapped.Y)
            {
                Scale = NewPropSquares,
                Rotation = 0,
                Size = CreatureSize.Medium,
                CellSize = CellSize,
                ImageId = imageId,
                IsProp = true,
                Blocks = NewPropBlocks,
                BlocksSight = NewPropBlocksSight
            };
            Tokens.Add(token);
            RemeasureRuler();
            UpdateReachableForActive();

            var bytes = _propBytes;
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.AddTokenAsync(new TokenAddedMessage(
                        token.Id, imageId, new SerializablePoint(token.X, token.Y),
                        token.Scale, token.Rotation, token.Size,
                        bytes == null ? null : Convert.ToBase64String(bytes), null, true, token.Blocks, token.BlocksSight));
                if (IsHost)
                {
                    token.ImagePath = TokenImageGuard.SaveForCampaign(App.PM.GetCampaignId(), bytes, true);
                    if (string.IsNullOrEmpty(token.ImagePath))
                        NavItem.NavError?.Invoke("That map object is on the board but could not be saved, it will be gone when the map reopens.");
                    else
                        await PersistTokenAsync(token);
                }
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] prop place failed", ex); }
        }

        private async Task SaveSelectedProp()
        {
            var token = SelectedToken;
            if (token == null || !token.IsProp || !IsHost) return;
            RemeasureRuler();
            UpdateReachableForActive();
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.ResizeTokenAsync(new TokenResizedMessage(token.Id, token.Size, token.Scale, true, token.Blocks, token.BlocksSight));
                await PersistTokenAsync(token);
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] map object save failed", ex); }
        }

        public event Action<string>? TokenRemovedLocally;

        public async Task RemoveToken(TokenViewModel token)
        {
            Tokens.Remove(token);
            TokenRemovedLocally?.Invoke(token.Id);
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.RemoveTokenAsync(new TokenRemovedMessage(token.Id));
                if (IsHost) await App.PM.GameDataRepo.DeleteTokenAsync(token.Id);
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] token remove sync failed", ex); }
        }

        public async Task NotifyTokenMoved(TokenViewModel token)
        {
            AccountFeet(token);
            SettleDashCost(token);
            UpdateReachableForActive();
            RemeasureRuler();
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.MoveTokenAsync(new TokenMovedMessage(
                        token.Id,
                        new SerializablePoint(token.X, token.Y)));
                if (IsHost) await PersistTokenAsync(token);
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] token move sync failed", ex); }
        }

        public event Action<TokenViewModel, TokenViewModel>? OpportunityAttackProvoked;

        public void CheckOpportunityAttacks(TokenViewModel moved, double fromX, double fromY)
        {
            if (moved == null || string.IsNullOrEmpty(moved.CombatantId)) return;
            var reach = CellSize * (App.PM?.CombatMeleeReachCells ?? 1.5);
            if (reach <= 0) return;
            var movedSide = TokenOnPlayerSide(moved);
            foreach (var other in Tokens)
            {
                if (ReferenceEquals(other, moved) || string.IsNullOrEmpty(other.CombatantId)) continue;
                if (TokenOnPlayerSide(other) == movedSide) continue;
                if (Dist(fromX, fromY, other.X, other.Y) <= reach && Dist(moved.X, moved.Y, other.X, other.Y) > reach)
                    OpportunityAttackProvoked?.Invoke(moved, other);
            }
        }

        private static double Dist(double ax, double ay, double bx, double by)
        {
            var dx = ax - bx;
            var dy = ay - by;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        public Func<string, bool>? CombatantOnPlayerSide { get; set; }

        private bool TokenOnPlayerSide(TokenViewModel t)
        {
            if (!string.IsNullOrEmpty(t.CombatantId) && CombatantOnPlayerSide != null) return CombatantOnPlayerSide(t.CombatantId);
            return !string.IsNullOrEmpty(t.CharacterId);
        }

        public Func<TokenViewModel, TokenSide>? ResolveTokenSide { get; set; }
        public Func<TokenViewModel, CombatantViewModel?>? CombatantForToken { get; set; }

        public event Action? ReachableChanged;
        public List<Rect> ReachableCells { get; } = new();
        private HashSet<(int, int)>? _reachableSet;
        private Dictionary<(int, int), double>? _reachableCost;
        private Dictionary<(int, int), double>? _lastReachableCost;
        private TokenViewModel? _reachableToken;

        private static readonly (int, int)[] _neighbors8 =
        {
            (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1)
        };

        public void UpdateReachableForActive()
        {
            var token = Tokens.FirstOrDefault(t => t.IsActiveCombatant);
            ReachableCells.Clear();
            _reachableSet = null;
            _reachableCost = null;
            _reachableToken = null;
            if (token != null)
            {
                var set = ComputeReachable(token, token.X, token.Y);
                _reachableSet = set;
                _reachableCost = _lastReachableCost;
                _reachableToken = token;
                if (set.Count > 1)
                {
                    var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
                    foreach (var (c, r) in set)
                        ReachableCells.Add(new Rect(c * cell, r * cell, cell, cell));
                }
            }
            ReachableChanged?.Invoke();
        }

        public bool IsCellReachable(TokenViewModel token, double worldX, double worldY)
        {
            if (_reachableSet == null || !ReferenceEquals(_reachableToken, token)) return true;
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            return _reachableSet.Contains(((int)Math.Floor(worldX / cell), (int)Math.Floor(worldY / cell)));
        }

        public bool MoveNeedsDash(TokenViewModel token, double worldX, double worldY)
        {
            var combatant = CombatantForToken?.Invoke(token);
            if (combatant == null || !combatant.Dashed || combatant.DashPaid) return false;
            if (_reachableCost == null || !ReferenceEquals(_reachableToken, token)) return false;
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            var key = ((int)Math.Floor(worldX / cell), (int)Math.Floor(worldY / cell));
            if (!_reachableCost.TryGetValue(key, out var squares)) return false;
            var feetPer = App.PM?.Rules?.FeetPerSquare ?? 5.0;
            return token.FeetMoved + squares * feetPer > combatant.EffectiveSpeedFeet + 0.01;
        }

        private bool _rulerIgnoresWalls = App.PM?.Rules?.RulerIgnoresWalls ?? true;
        public bool RulerIgnoresWalls
        {
            get => _rulerIgnoresWalls;
            set { this.RaiseAndSetIfChanged(ref _rulerIgnoresWalls, value); RecomputeRuler(); }
        }

        private bool _rulerIgnoresOccupied = App.PM?.Rules?.RulerIgnoresOccupied ?? true;
        public bool RulerIgnoresOccupied
        {
            get => _rulerIgnoresOccupied;
            set { this.RaiseAndSetIfChanged(ref _rulerIgnoresOccupied, value); RecomputeRuler(); }
        }

        public event Action? RulerChanged;
        public Point? RulerStart { get; private set; }
        public Point? RulerEnd { get; private set; }
        public List<Point> RulerPath { get; } = new();

        private string _rulerLabel = "";
        public string RulerLabel
        {
            get => _rulerLabel;
            private set => this.RaiseAndSetIfChanged(ref _rulerLabel, value);
        }

        private string _rulerHint = "";
        public string RulerHint
        {
            get => _rulerHint;
            private set => this.RaiseAndSetIfChanged(ref _rulerHint, value);
        }

        public bool HasRulerHint => _rulerHint.Length > 0;

        public void RulerClick(double worldX, double worldY)
        {
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            var centred = new Point(Math.Floor(worldX / cell) * cell + cell / 2.0, Math.Floor(worldY / cell) * cell + cell / 2.0);
            if (RulerStart == null || RulerEnd != null)
            {
                RulerStart = centred;
                RulerEnd = null;
                RulerLabel = "";
            }
            else
            {
                RulerEnd = centred;
            }
            RecomputeRuler();
        }

        public void ClearRuler()
        {
            RulerStart = null;
            RulerEnd = null;
            RulerPath.Clear();
            RulerLabel = "";
            RulerHint = "";
            this.RaisePropertyChanged(nameof(HasRulerHint));
            RulerChanged?.Invoke();
        }

        private void RemeasureRuler()
        {
            if (RulerStart != null && RulerEnd != null) RecomputeRuler();
        }

        public void RecomputeRuler()
        {
            RulerPath.Clear();
            if (RulerStart is { } a && RulerEnd is { } b)
            {
                var (feet, reachable, path) = MeasureRuler(a, b);
                var ft = App.PM?.Rules?.FeetPerSquare ?? 5.0;
                if (reachable)
                {
                    RulerLabel = ((int)(Math.Round(feet / ft) * ft)) + " ft";
                    RulerHint = "";
                    RulerPath.AddRange(path);
                }
                else
                {
                    RulerLabel = "No way there";
                    RulerHint = !RulerIgnoresWalls && !RulerIgnoresOccupied
                        ? "Nothing gets there around the walls and the creatures in the way. Tick either box to measure straight through."
                        : !RulerIgnoresWalls
                            ? "The walls close it off. Tick ignore walls to measure straight through."
                            : "The creatures in the way close it off. Tick ignore occupied squares to measure straight through.";
                }
            }
            this.RaisePropertyChanged(nameof(HasRulerHint));
            RulerChanged?.Invoke();
        }

        private static double OctileHeuristic(int dc, int dr, double diagCost)
        {
            var a = Math.Abs(dc);
            var b = Math.Abs(dr);
            var lo = Math.Min(a, b);
            var hi = Math.Max(a, b);
            return hi + (Math.Min(diagCost, 2.0) - 1.0) * lo;
        }

        private (double Feet, bool Reachable, List<Point> Path) MeasureRuler(Point a, Point b)
        {
            var rules = App.PM?.Rules;
            var feetPer = rules?.FeetPerSquare ?? 5.0;
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            Point Center((int C, int R) k) => new(k.C * cell + cell / 2.0, k.R * cell + cell / 2.0);

            var from = ((int)Math.Floor(a.X / cell), (int)Math.Floor(a.Y / cell));
            var to = ((int)Math.Floor(b.X / cell), (int)Math.Floor(b.Y / cell));
            var straight = rules?.PathCostSquares(to.Item1 - from.Item1, to.Item2 - from.Item2)
                           ?? Math.Max(Math.Abs(to.Item1 - from.Item1), Math.Abs(to.Item2 - from.Item2));

            if (from == to) return (0, true, new List<Point> { Center(from) });
            if (RulerIgnoresWalls && RulerIgnoresOccupied)
                return (straight * feetPer, true, new List<Point> { Center(from), Center(to) });

            var pad = Math.Max(Math.Abs(to.Item1 - from.Item1), Math.Abs(to.Item2 - from.Item2)) + 2;
            var minC = Math.Min(from.Item1, to.Item1) - pad;
            var maxC = Math.Max(from.Item1, to.Item1) + pad;
            var minR = Math.Min(from.Item2, to.Item2) - pad;
            var maxR = Math.Max(from.Item2, to.Item2) + pad;

            var diagCost = rules?.DiagonalCostSquares ?? 1.0;
            var best = new Dictionary<(int, int), double> { [from] = 0 };
            var cameFrom = new Dictionary<(int, int), (int, int)>();
            var closed = new HashSet<(int, int)>();
            var open = new PriorityQueue<(int, int), double>();
            open.Enqueue(from, OctileHeuristic(to.Item1 - from.Item1, to.Item2 - from.Item2, diagCost));

            while (open.Count > 0)
            {
                var cur = open.Dequeue();
                if (cur == to)
                {
                    var path = new List<Point>();
                    var walk = cur;
                    path.Add(Center(walk));
                    while (cameFrom.TryGetValue(walk, out var prev))
                    {
                        walk = prev;
                        path.Add(Center(walk));
                    }
                    path.Reverse();
                    return (best[cur] * feetPer, true, path);
                }
                if (!closed.Add(cur)) continue;

                var d = best[cur];
                var curCenter = Center(cur);
                foreach (var (dc, dr) in _neighbors8)
                {
                    var key = (cur.Item1 + dc, cur.Item2 + dr);
                    if (key.Item1 < minC || key.Item1 > maxC || key.Item2 < minR || key.Item2 > maxR) continue;
                    if (closed.Contains(key)) continue;
                    var nCenter = Center(key);
                    if (!IsInsideMap(nCenter.X, nCenter.Y, cell)) continue;
                    if (!RulerIgnoresWalls && (MovementBlockedByWall(curCenter, nCenter) || ObjectBlocksMovement(key.Item1, key.Item2))) continue;
                    if (!RulerIgnoresOccupied && key != to && IsAreaOccupied(nCenter.X, nCenter.Y, cell)) continue;
                    var nd = d + (dc != 0 && dr != 0 ? diagCost : 1.0);
                    if (best.TryGetValue(key, out var prev) && nd >= prev) continue;
                    best[key] = nd;
                    cameFrom[key] = cur;
                    open.Enqueue(key, nd + OctileHeuristic(to.Item1 - key.Item1, to.Item2 - key.Item2, diagCost));
                }
            }
            return (straight * feetPer, false, new List<Point>());
        }

        public bool MoveBlockedByBudget(TokenViewModel token, double worldX, double worldY) =>
            MoveBlockedByBudget(token, worldX, worldY, token.X, token.Y);

        public Func<bool>? IsCombatRunning;

        public bool MoveIsOffTurn(TokenViewModel token)
        {
            if (token == null || token.IsActiveCombatant) return false;
            if (IsCombatRunning?.Invoke() != true) return false;
            return CombatantForToken?.Invoke(token) != null;
        }

        public bool MoveBlockedByBudget(TokenViewModel token, double worldX, double worldY, double fromX, double fromY)
        {
            var rules = App.PM?.Rules;
            if (rules == null || !rules.EnforceMovementBudget) return false;
            if (IsHost && rules.DmIgnoresMovementBudget) return false;
            if (MoveIsOffTurn(token)) return true;
            if (!token.IsActiveCombatant) return false;
            if (!IsInsideMap(fromX, fromY, token.PixelSize)) return false;
            if (!IsCellReachable(token, worldX, worldY)) return true;
            if (!MoveNeedsDash(token, worldX, worldY)) return false;
            var combatant = CombatantForToken?.Invoke(token);
            return combatant != null
                   && !InitiativeTrackerViewModel.CanAfford(combatant, MapSessionViewModel.EconomyCostFor(combatant, "dash"));
        }

        public string MoveRefusedReason(TokenViewModel token, double worldX, double worldY, double fromX, double fromY)
        {
            if (IsAreaOccupied(worldX, worldY, token.PixelSize, token)) return "Something is already standing there.";
            if (MoveIsOffTurn(token) && !(IsHost && (App.PM?.Rules?.DmIgnoresMovementBudget ?? false))
                && (App.PM?.Rules?.EnforceMovementBudget ?? false))
                return "It is not their turn.";
            if (MoveBlockedByBudget(token, worldX, worldY, fromX, fromY)) return "No movement left for that this turn.";
            if (!IsInsideMap(worldX, worldY, token.PixelSize)) return "That is off the edge of the map.";
            return "";
        }

        public void SettleDashCost(TokenViewModel token)
        {
            var combatant = CombatantForToken?.Invoke(token);
            if (combatant == null || !combatant.Dashed || combatant.DashPaid) return;
            if (token.FeetMoved <= combatant.EffectiveSpeedFeet + 0.01) return;
            var cost = MapSessionViewModel.EconomyCostFor(combatant, "dash");
            if (!InitiativeTrackerViewModel.CanAfford(combatant, cost)) return;
            InitiativeTrackerViewModel.Pay(combatant, cost);
            combatant.DashPaid = true;
            DashSpent?.Invoke(combatant, cost);
        }

        public event Action<CombatantViewModel, string>? DashSpent;

        private HashSet<(int, int)> ComputeReachable(TokenViewModel token, double startX, double startY)
        {
            var result = new HashSet<(int, int)>();
            var feetPer = App.PM?.Rules.FeetPerSquare ?? 5.0;
            if (feetPer <= 0) return result;
            var combatant = CombatantForToken?.Invoke(token);
            var speed = combatant?.EffectiveSpeedFeet ?? (App.PM?.CombatBaseSpeedFeet ?? 30);
            if (combatant != null && combatant.Dashed) speed = (int)Math.Round(speed * (App.PM?.CombatDashMultiplier ?? 2.0));
            if (combatant != null && ConditionEffects.StopsMovement(combatant.Conditions)) speed = 0;
            var remainingFeet = Math.Max(0, speed - token.FeetMoved);
            var maxCells = remainingFeet / feetPer;
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            var start = ((int)Math.Floor(startX / cell), (int)Math.Floor(startY / cell));
            result.Add(start);
            if (maxCells <= 0) return result;

            // Weighted flood fill so a difficult cell costs the extra to enter, that is what makes the reachable overlay bend around painted terrain.
            var diagCost = App.PM?.Rules.DiagonalCostSquares ?? 1.0;
            var best = new Dictionary<(int, int), double> { [start] = 0 };
            _lastReachableCost = best;
            var pq = new PriorityQueue<(int, int), double>();
            pq.Enqueue(start, 0);
            var footprint = token.PixelSize;
            while (pq.Count > 0)
            {
                var cur = pq.Dequeue();
                var d = best[cur];
                if (d >= maxCells) continue;
                var curCenter = new Point(cur.Item1 * cell + cell / 2.0, cur.Item2 * cell + cell / 2.0);
                foreach (var (dc, dr) in _neighbors8)
                {
                    var key = (cur.Item1 + dc, cur.Item2 + dr);
                    var nCenter = new Point(key.Item1 * cell + cell / 2.0, key.Item2 * cell + cell / 2.0);
                    if (!IsInsideMap(nCenter.X, nCenter.Y, footprint)) continue;
                    if (MovementBlockedByWall(curCenter, nCenter)) continue;
                    if (ObjectBlocksMovement(key.Item1, key.Item2)) continue;
                    if (IsAreaOccupied(nCenter.X, nCenter.Y, footprint, token)) continue;
                    var step = (dc != 0 && dr != 0 ? diagCost : 1.0) * MoveCostMultiplierFor(key.Item1, key.Item2);
                    var nd = d + step;
                    if (nd > maxCells + 1e-9) continue;
                    if (!best.TryGetValue(key, out var prev) || nd < prev)
                    {
                        best[key] = nd;
                        result.Add(key);
                        pq.Enqueue(key, nd);
                    }
                }
            }
            return result;
        }

        private bool MovementBlockedByWall(Point a, Point b)
        {
            if (!WallsEnabled) return false;
            if (CrossesWall(a, b)) return true;
            if (Math.Abs(a.X - b.X) < 0.01 || Math.Abs(a.Y - b.Y) < 0.01) return false;

            var viaX = new Point(b.X, a.Y);
            var viaY = new Point(a.X, b.Y);
            var roundX = CrossesWall(a, viaX) || CrossesWall(viaX, b);
            var roundY = CrossesWall(a, viaY) || CrossesWall(viaY, b);
            return roundX && roundY;
        }

        private bool CrossesWall(Point a, Point b)
        {
            foreach (var w in _walls)
            {
                if (w.IsDoor && w.DoorOpen) continue;
                if (MoveSegmentsIntersect(a, b, new Point(w.X1, w.Y1), new Point(w.X2, w.Y2))) return true;
            }
            return false;
        }

        private static bool MoveSegmentsIntersect(Point p1, Point p2, Point p3, Point p4)
        {
            double d1 = MoveCross(p3, p4, p1);
            double d2 = MoveCross(p3, p4, p2);
            double d3 = MoveCross(p1, p2, p3);
            double d4 = MoveCross(p1, p2, p4);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double MoveCross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        // The target moves off in small steps so it stops at the first wall instead of sliding through one, and what comes back is the feet it managed.
        public double PushCombatant(string attackerCombatantId, string targetCombatantId, double feet)
        {
            if (feet <= 0 || string.IsNullOrEmpty(attackerCombatantId) || string.IsNullOrEmpty(targetCombatantId)) return 0;
            var attacker = Tokens.FirstOrDefault(t => t.CombatantId == attackerCombatantId);
            var target = Tokens.FirstOrDefault(t => t.CombatantId == targetCombatantId);
            if (attacker == null || target == null) return 0;

            double dx = target.X - attacker.X, dy = target.Y - attacker.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001) return 0;
            dx /= len;
            dy /= len;

            var feetPerCell = App.PM?.Rules?.FeetPerSquare ?? 5.0;
            if (feetPerCell <= 0 || CellSize <= 0) return 0;
            var wantedPx = feet * (CellSize / feetPerCell);
            var stepPx = Math.Max(1.0, CellSize / 4.0);

            double movedPx = 0;
            double x = target.X, y = target.Y;
            while (movedPx < wantedPx)
            {
                var d = Math.Min(stepPx, wantedPx - movedPx);
                var nx = x + dx * d;
                var ny = y + dy * d;
                if (MovementBlockedByWall(new Point(x, y), new Point(nx, ny))) break;
                if (!IsInsideMap(nx, ny, target.PixelSize)) break;
                x = nx;
                y = ny;
                movedPx += d;
            }
            if (movedPx <= 0) return 0;

            target.X = x;
            target.Y = y;
            UpdateReachableForActive();
            _ = SyncPushedTokenAsync(target);
            return movedPx / (CellSize / feetPerCell);
        }

        private async Task SyncPushedTokenAsync(TokenViewModel token)
        {
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.MoveTokenAsync(new TokenMovedMessage(token.Id, new SerializablePoint(token.X, token.Y)));
                if (IsHost) await PersistTokenAsync(token);
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] push sync failed", ex); }
        }

        public async Task RevertTokenToTurnStart(TokenViewModel token)
        {
            if (token == null) return;
            token.X = token.TurnStartX;
            token.Y = token.TurnStartY;
            token.FeetAnchorX = token.X;
            token.FeetAnchorY = token.Y;
            token.FeetMoved = 0;
            UpdateReachableForActive();
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.MoveTokenAsync(new TokenMovedMessage(token.Id, new SerializablePoint(token.X, token.Y)));
                if (IsHost) await PersistTokenAsync(token);
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] revert move sync failed", ex); }
        }

        public void RefreshTokenSides()
        {
            foreach (var t in Tokens)
                t.Side = ResolveTokenSide?.Invoke(t)
                    ?? (string.IsNullOrEmpty(t.CombatantId) ? TokenSide.None : (TokenOnPlayerSide(t) ? TokenSide.Friendly : TokenSide.Enemy));
        }

        public bool IsFlanking(string attackerCombatantId, string targetCombatantId)
        {
            if (string.IsNullOrEmpty(attackerCombatantId) || string.IsNullOrEmpty(targetCombatantId)) return false;
            var attacker = Tokens.FirstOrDefault(t => t.CombatantId == attackerCombatantId);
            var target = Tokens.FirstOrDefault(t => t.CombatantId == targetCombatantId);
            if (attacker == null || target == null) return false;
            var reach = CellSize * (App.PM?.CombatMeleeReachCells ?? 1.5);
            if (reach <= 0 || Dist(attacker.X, attacker.Y, target.X, target.Y) > reach) return false;
            var attackerSide = TokenOnPlayerSide(attacker);
            if (TokenOnPlayerSide(target) == attackerSide) return false;
            var cosThreshold = Math.Cos((App.PM?.CombatFlankAngleDegrees ?? 120) * Math.PI / 180.0);
            foreach (var ally in Tokens)
            {
                if (ReferenceEquals(ally, attacker) || ReferenceEquals(ally, target)) continue;
                if (string.IsNullOrEmpty(ally.CombatantId)) continue;
                if (TokenOnPlayerSide(ally) != attackerSide) continue;
                if (Dist(ally.X, ally.Y, target.X, target.Y) > reach) continue;
                if (OnOppositeSides(attacker, ally, target, cosThreshold)) return true;
            }
            return false;
        }

        public double CombatantDistanceCells(string attackerCombatantId, string targetCombatantId)
        {
            if (string.IsNullOrEmpty(attackerCombatantId) || string.IsNullOrEmpty(targetCombatantId)) return -1;
            var attacker = Tokens.FirstOrDefault(t => t.CombatantId == attackerCombatantId);
            var target = Tokens.FirstOrDefault(t => t.CombatantId == targetCombatantId);
            if (attacker == null || target == null) return -1;
            var cell = CellSize;
            if (cell <= 0) return -1;
            return Dist(attacker.X, attacker.Y, target.X, target.Y) / cell;
        }

        // A low wall that only breaks the line without blocking sight is cover.
        public SightLine LineToTarget(string attackerCombatantId, string targetCombatantId)
        {
            if (string.IsNullOrEmpty(attackerCombatantId) || string.IsNullOrEmpty(targetCombatantId)) return SightLine.Clear;
            var attacker = Tokens.FirstOrDefault(t => t.CombatantId == attackerCombatantId);
            var target = Tokens.FirstOrDefault(t => t.CombatantId == targetCombatantId);
            if (attacker == null || target == null) return SightLine.Clear;
            var a = new Point(attacker.X, attacker.Y);
            var b = new Point(target.X, target.Y);

            if (SightBlockedByObject(a, b)) return SightLine.Blocked;

            if (!WallsEnabled) return SightLine.Clear;
            var cover = false;
            foreach (var w in _walls)
            {
                if (w.IsDoor && w.DoorOpen) continue;
                if (!MoveSegmentsIntersect(a, b, new Point(w.X1, w.Y1), new Point(w.X2, w.Y2))) continue;
                if (w.BlocksSight) return SightLine.Blocked;
                cover = true;
            }
            return cover ? SightLine.Cover : SightLine.Clear;
        }

        public bool SightBlocked(Point a, Point b) => SightBlockedByObject(a, b) || SightBlockedByWall(a, b);

        public bool HasAnySightBlocker =>
            (WallsEnabled && _walls.Any(w => w.BlocksSight && !(w.IsDoor && w.DoorOpen)))
            || _objectCells.Keys.Any(k => ObjectBlocksSight(k.Col, k.Row))
            || Tokens.Any(t => t.IsProp && t.BlocksSight);

        private bool SightBlockedByObject(Point a, Point b)
        {
            if (_objectCells.Count == 0 && !Tokens.Any(t => t.IsProp && t.BlocksSight)) return false;
            var sightProps = Tokens.Where(t => t.IsProp && t.BlocksSight).ToList();
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            if (cell <= 0) return false;

            var from = ((int)Math.Floor(a.X / cell), (int)Math.Floor(a.Y / cell));
            var to = ((int)Math.Floor(b.X / cell), (int)Math.Floor(b.Y / cell));

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (length <= 0) return false;

            var steps = (int)Math.Ceiling(length / (cell / 4.0));
            for (int i = 1; i < steps; i++)
            {
                var t = i / (double)steps;
                var px = a.X + dx * t;
                var py = a.Y + dy * t;
                var key = ((int)Math.Floor(px / cell), (int)Math.Floor(py / cell));
                if (key == from || key == to) continue;
                if (ObjectBlocksSight(key.Item1, key.Item2)) return true;
                foreach (var p in sightProps)
                {
                    var half = p.PixelSize / 2.0;
                    if (Math.Abs(px - p.X) <= half && Math.Abs(py - p.Y) <= half) return true;
                }
            }
            return false;
        }

        private static bool OnOppositeSides(TokenViewModel a, TokenViewModel b, TokenViewModel target, double cosThreshold)
        {
            var ax = a.X - target.X;
            var ay = a.Y - target.Y;
            var bx = b.X - target.X;
            var by = b.Y - target.Y;
            var la = Math.Sqrt(ax * ax + ay * ay);
            var lb = Math.Sqrt(bx * bx + by * by);
            if (la <= 0 || lb <= 0) return false;
            // Tokens are freeform pixels, not snapped, so opposite sides is an angle test around the target, the threshold cosine comes from the configured flank angle.
            return (ax * bx + ay * by) / (la * lb) <= cosThreshold;
        }

        public async Task RotateSelection(double degrees)
        {
            if (SelectedToken != null)
            {
                SelectedToken.Rotation = (SelectedToken.Rotation + degrees) % 360;
                try
                {
                    if (_com != null && IsBroadcasting)
                        await _com.RotateTokenAsync(new TokenRotatedMessage(SelectedToken.Id, SelectedToken.Rotation));
                    if (IsHost) await PersistTokenAsync(SelectedToken);
                }
                catch (Exception ex) { ErrorLog.Log($"[Canvas] token rotate sync failed", ex); }
                return;
            }
            TokenRotation = (TokenRotation + degrees) % 360;
        }

        private int? ParseInitiativeOverride() =>
            NewTokenInitiative is decimal d ? (int)decimal.Round(d) : (int?)null;

        private void AccountFeet(TokenViewModel token)
        {
            var dx = token.X - token.FeetAnchorX;
            var dy = token.Y - token.FeetAnchorY;
            var cell = token.CellSize > 0 ? token.CellSize : GridOverlay.BaseCellPx;
            var rules = App.PM?.Rules;
            // Has to price a step the same way the reachable overlay does or the diagonal corner of the highlight costs more feet than the speed that drew it.
            var squares = rules?.PathCostSquares(dx / cell, dy / cell) ?? Math.Sqrt(dx * dx + dy * dy) / cell;
            token.FeetMoved += squares * (rules?.FeetPerSquare ?? 5.0);
            token.FeetAnchorX = token.X;
            token.FeetAnchorY = token.Y;
        }

        private void ResolveSelectedImageId()
        {
            if (_selectedTokenIndex < 0 || _selectedTokenIndex >= TokenLibrary.Count)
            {
                _selectedTokenImageId = null;
                return;
            }
            _selectedTokenImageId = _imageById.FirstOrDefault(kv => kv.Value == TokenLibrary[_selectedTokenIndex]).Key;
            Mode = CanvasToolMode.Token;
        }

        public void AdjustScale(double delta) =>
            TokenScale = Math.Max(0.2, TokenScale + delta);

        public async Task SetTokenSize(TokenViewModel token, CreatureSize size)
        {
            token.Size = size;
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.ResizeTokenAsync(new TokenResizedMessage(token.Id, size, token.Scale));
                if (IsHost) await PersistTokenAsync(token);
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] token resize sync failed", ex); }
        }

        internal void HandleTokenResized(TokenResizedMessage msg)
        {
            var token = Tokens.FirstOrDefault(t => t.Id == msg.TokenId);
            if (token == null) return;
            token.Size = msg.Size;
            token.Scale = msg.Scale;
            if (msg.IsProp)
            {
                token.Blocks = msg.Blocks;
                token.BlocksSight = msg.BlocksSight;
                RemeasureRuler();
                UpdateReachableForActive();
            }
            if (IsHost) _ = PersistTokenAsync(token);
        }

        public async Task Ping(double x, double y)
        {
            var ping = new PingViewModel(Guid.NewGuid().ToString("N"), x, y, MyColor);
            ActivePings.Add(ping);
            try
            {
                if (_com != null && IsBroadcasting)
                    await _com.SendPingAsync(new PingMessage(_com.UserId, new SerializablePoint(x, y), MyColor));
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[Canvas] ping send failed", ex);
            }
            await ExpirePingAsync(ping);
        }

        private void HandlePingReceived(PingMessage msg)
        {
            var ping = new PingViewModel(Guid.NewGuid().ToString("N"), msg.Position.X, msg.Position.Y, msg.Color);
            ActivePings.Add(ping);
            _ = ExpirePingAsync(ping);
        }

        private async Task ExpirePingAsync(PingViewModel ping)
        {
            await Task.Delay(2000);
            ActivePings.Remove(ping);
        }

        private async Task RequestColorAsync(string hex)
        {
            if (_com == null) { MyColor = hex; return; }
            var ok = await _com.RequestColorAsync(hex);
            if (!ok)
            {
                ColorWarning = "That colour is taken, pick another.";
                _ = ClearColorWarningAsync();
            }
        }

        private async Task ClearColorWarningAsync()
        {
            await Task.Delay(2500);
            ColorWarning = "";
        }

        private void HandleColorChanged(string userId, string hex)
        {
            if (string.Equals(userId, _com?.UserId, StringComparison.OrdinalIgnoreCase))
                MyColor = hex;
        }

        public void PaintFogCell(int col, int row, bool hide)
        {
            if (!IsHost) return;
            var key = (col, row);
            bool changed = hide ? _fogHidden.Add(key) : _fogHidden.Remove(key);
            if (!changed) return;
            _pendingFogHide = hide;
            _pendingFogPaint.Add(new FogCellPoint(col, row));
            FogCellChanged?.Invoke(col, row, hide);
        }

        public async Task FlushFogPaintAsync()
        {
            if (!IsHost || _pendingFogPaint.Count == 0) return;
            var cells = _pendingFogPaint.ToList();
            var hide = _pendingFogHide;
            _pendingFogPaint.Clear();
            if (_com != null && IsBroadcasting)
                await _com.SendFogPaintAsync(new FogPaintMessage(MapId, cells, hide));
            await PersistFogAsync();
        }

        public async Task RevealVisionAroundAsync(double px, double py)
        {
            if (!IsHost || !FogEnabled || !DynamicVisionEnabled) return;
            var cs = CellSize;
            if (cs <= 0) return;
            var feetPerSquare = App.PM?.Rules?.FeetPerSquare ?? 5.0;
            var radiusPx = feetPerSquare > 0 ? VisionRadiusFeet / feetPerSquare * cs : cs * 12;
            var radiusSq = radiusPx * radiusPx;
            var origin = new Point(px, py);
            var any = false;
            foreach (var key in _fogHidden.ToList())
            {
                var cx = key.Col * cs + cs / 2;
                var cy = key.Row * cs + cs / 2;
                var dx = cx - px;
                var dy = cy - py;
                if (dx * dx + dy * dy > radiusSq) continue;
                if (SightBlocked(origin, new Point(cx, cy))) continue;
                _fogHidden.Remove(key);
                _pendingFogPaint.Add(new FogCellPoint(key.Col, key.Row));
                FogCellChanged?.Invoke(key.Col, key.Row, false);
                any = true;
            }
            if (any) { _pendingFogHide = false; await FlushFogPaintAsync(); }
        }

        public bool SightBlockedByWall(Point a, Point b)
        {
            if (!WallsEnabled) return false;
            foreach (var w in _walls)
            {
                if (!w.BlocksSight) continue;
                if (w.IsDoor && w.DoorOpen) continue;
                if (MoveSegmentsIntersect(a, b, new Point(w.X1, w.Y1), new Point(w.X2, w.Y2))) return true;
            }
            return false;
        }

        private async Task ToggleFogEnabledAsync()
        {
            if (!IsHost) return;
            FogEnabled = !FogEnabled;
            await BroadcastFogStateAsync();
        }

        private async Task HideAllFogAsync()
        {
            if (!IsHost) return;
            _fogHidden.Clear();
            var cols = FogCols;
            var rows = FogRows;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    _fogHidden.Add((c, r));
            FogEnabled = true;
            await BroadcastFogStateAsync();
        }

        private async Task RevealAllFogAsync()
        {
            if (!IsHost) return;
            _fogHidden.Clear();
            FogBulkChanged?.Invoke();
            await BroadcastFogStateAsync();
        }

        private async Task BroadcastFogStateAsync()
        {
            if (!IsHost) return;
            if (_com != null && IsBroadcasting)
                await _com.SendFogStateAsync(BuildFogSnapshot());
            await PersistFogAsync();
        }

        private FogStateMessage BuildFogSnapshot() =>
            new FogStateMessage(MapId, FogEnabled, FogCols, FogRows,
                _fogHidden.Select(c => new FogCellPoint(c.Col, c.Row)).ToList());

        private async Task PersistFogAsync()
        {
            if (!IsHost) return;
            try
            {
                await App.PM.GameDataRepo.SaveFogAsync(new MapFogState
                {
                    MapId = MapId,
                    CampaignId = App.PM.GetCampaignId(),
                    Enabled = FogEnabled,
                    Cols = FogCols,
                    Rows = FogRows,
                    Hidden = new HashSet<(int, int)>(_fogHidden)
                });
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Fog persist failed", ex);
                NavItem.NavError?.Invoke("Couldn't save the fog for this map, it may come back wrong after a reopen.");
            }
        }

        public async Task InitTokensAsync()
        {
            if (IsHost || _com == null) return;
            try
            {
                var fresh = await _com.FetchTokensAsync(MapId);
                Tokens.Clear();
                foreach (var t in fresh) HandleTokenAdded(t);
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] token resync failed", ex); }
        }

        public async Task InitFogAsync()
        {
            try
            {
                if (IsHost)
                {
                    var state = await App.PM.GameDataRepo.LoadFogAsync(MapId);
                    ApplyFogState(state.Enabled, state.Hidden);
                }
                else if (_com != null)
                {
                    var snap = await _com.FetchFogAsync(MapId);
                    if (snap != null)
                        ApplyFogState(snap.Enabled, snap.Hidden.Select(c => (c.Col, c.Row)));
                }
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] fog init failed", ex); }
        }

        private void ApplyFogState(bool enabled, IEnumerable<(int Col, int Row)> hidden)
        {
            _fogHidden.Clear();
            foreach (var c in hidden) _fogHidden.Add(c);
            _fogEnabled = enabled;
            this.RaisePropertyChanged(nameof(FogEnabled));
            FogBulkChanged?.Invoke();
        }

        private void HandleFogPainted(FogPaintMessage msg)
        {
            if (msg.MapId != MapId) return;
            foreach (var cell in msg.Cells)
            {
                var key = (cell.Col, cell.Row);
                bool changed = msg.Hidden ? _fogHidden.Add(key) : _fogHidden.Remove(key);
                if (changed) FogCellChanged?.Invoke(cell.Col, cell.Row, msg.Hidden);
            }
        }

        private void HandleFogUpdated(FogStateMessage msg)
        {
            if (msg.MapId != MapId) return;
            ApplyFogState(msg.Enabled, msg.Hidden.Select(c => (c.Col, c.Row)));
        }

        private readonly HashSet<(int Col, int Row)> _difficultCells = new();
        public IReadOnlyCollection<(int Col, int Row)> DifficultCells => _difficultCells;
        public bool IsDifficultCell(int col, int row) => _difficultCells.Contains((col, row)) || TerrainFromAreas(col, row).Length > 0;

        public double MoveCostMultiplierFor(int col, int row)
        {
            var rules = App.PM?.Rules;
            var painted = _difficultCells.Contains((col, row)) ? (rules?.DifficultTerrainMultiplier ?? 2.0) : 1.0;
            var fromArea = rules?.TerrainMultiplier(TerrainFromAreas(col, row)) ?? 1.0;
            return Math.Max(painted, fromArea);
        }

        public double PxPerFoot
        {
            get
            {
                var feetPerSquare = App.PM?.Rules?.FeetPerSquare ?? 5.0;
                return feetPerSquare > 0 ? CellSize / feetPerSquare : 0;
            }
        }

        public string TerrainFromAreas(int col, int row)
        {
            if (AoeTemplates.Count == 0) return "";
            var pxPerFoot = PxPerFoot;
            if (pxPerFoot <= 0) return "";
            var defaultLineFt = App.PM?.Rules?.DefaultLineWidthFeet ?? 5.0;
            var x = (col + 0.5) * CellSize;
            var y = (row + 0.5) * CellSize;
            foreach (var t in AoeTemplates)
                if (t.ShapesGround && t.Contains(x, y, pxPerFoot, defaultLineFt)) return t.Terrain;
            return "";
        }

        public event Action<int, int, bool>? TerrainCellChanged;
        public event Action? TerrainChanged;

        // Host only. The persist and the broadcast happen on the flush at pointer release.
        public void PaintDifficultCell(int col, int row, bool add)
        {
            if (!IsHost) return;
            var key = (col, row);
            bool changed = add ? _difficultCells.Add(key) : _difficultCells.Remove(key);
            if (!changed) return;
            TerrainCellChanged?.Invoke(col, row, add);
            UpdateReachableForActive();
        }

        public async Task FlushDifficultTerrainAsync()
        {
            if (!IsHost) return;
            if (_com != null && IsBroadcasting)
                await _com.SendTerrainStateAsync(BuildTerrainSnapshot());
            await PersistDifficultTerrainAsync();
        }

        private TerrainStateMessage BuildTerrainSnapshot() =>
            new TerrainStateMessage(MapId, _difficultCells.Select(c => new FogCellPoint(c.Col, c.Row)).ToList());

        private async Task PersistDifficultTerrainAsync()
        {
            if (!IsHost) return;
            try
            {
                var json = JsonSerializer.Serialize(_difficultCells.Select(c => new FogCellPoint(c.Col, c.Row)).ToList());
                await App.PM.GameDataRepo.SaveDifficultTerrainAsync(MapId, json);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Difficult terrain persist failed", ex);
                NavItem.NavError?.Invoke("Couldn't save the difficult terrain, it may come back wrong after a reopen.");
            }
        }

        public async Task ClearDifficultTerrainAsync()
        {
            if (!IsHost) return;
            _difficultCells.Clear();
            TerrainChanged?.Invoke();
            UpdateReachableForActive();
            await FlushDifficultTerrainAsync();
        }

        public async Task InitTerrainAsync()
        {
            try
            {
                if (IsHost)
                {
                    var cells = await App.PM.GameDataRepo.LoadDifficultTerrainAsync(MapId);
                    ApplyTerrainState(cells);
                }
                else if (_com != null)
                {
                    var snap = await _com.FetchTerrainAsync(MapId);
                    if (snap != null) ApplyTerrainState(snap.Cells);
                }
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] terrain init failed", ex); }
        }

        private void HandleTerrainUpdated(TerrainStateMessage msg)
        {
            if (msg.MapId != MapId) return;
            ApplyTerrainState(msg.Cells);
        }

        private void ApplyTerrainState(IEnumerable<FogCellPoint> cells)
        {
            _difficultCells.Clear();
            foreach (var c in cells) _difficultCells.Add((c.Col, c.Row));
            TerrainChanged?.Invoke();
            UpdateReachableForActive();
        }

        private readonly Dictionary<(int Col, int Row), string> _objectCells = new();
        public IReadOnlyDictionary<(int Col, int Row), string> ObjectCells => _objectCells;
        public string? ObjectAt(int col, int row) => _objectCells.TryGetValue((col, row), out var id) ? id : null;
        public bool ObjectBlocksMovement(int col, int row) => App.PM?.Rules?.GridItemBlocksMovement(ObjectAt(col, row)) ?? false;
        public bool ObjectBlocksSight(int col, int row) => App.PM?.Rules?.GridItemBlocksSight(ObjectAt(col, row)) ?? false;

        public ObservableCollection<GridItemRule> GridItemPalette { get; } = new();

        private GridItemRule? _selectedGridItem;
        public GridItemRule? SelectedGridItem { get => _selectedGridItem; set => this.RaiseAndSetIfChanged(ref _selectedGridItem, value); }

        public event Action<int, int, string?>? ObjectCellChanged;
        public event Action? ObjectsChanged;

        public void PaintObjectCell(int col, int row, bool add)
        {
            if (!IsHost) return;
            var key = (col, row);
            if (add)
            {
                var id = SelectedGridItem?.Id;
                if (string.IsNullOrWhiteSpace(id)) return;
                if (_objectCells.TryGetValue(key, out var existing) && existing == id) return;
                _objectCells[key] = id!;
                ObjectCellChanged?.Invoke(col, row, id);
            }
            else
            {
                if (!_objectCells.Remove(key)) return;
                ObjectCellChanged?.Invoke(col, row, null);
            }
            UpdateReachableForActive();
        }

        public async Task FlushMapObjectsAsync()
        {
            if (!IsHost) return;
            if (_com != null && IsBroadcasting)
                await _com.SendMapObjectsStateAsync(BuildObjectSnapshot());
            await PersistMapObjectsAsync();
        }

        private MapObjectStateMessage BuildObjectSnapshot() =>
            new MapObjectStateMessage(MapId, _objectCells.Select(c => new MapObjectPoint(c.Key.Col, c.Key.Row, c.Value)).ToList());

        private async Task PersistMapObjectsAsync()
        {
            if (!IsHost) return;
            try
            {
                var json = JsonSerializer.Serialize(_objectCells.Select(c => new MapObjectPoint(c.Key.Col, c.Key.Row, c.Value)).ToList());
                await App.PM.GameDataRepo.SaveMapObjectsAsync(MapId, json);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Map objects persist failed", ex);
                NavItem.NavError?.Invoke("Couldn't save the map objects, they may come back wrong after a reopen.");
            }
        }

        public async Task ClearMapObjectsAsync()
        {
            if (!IsHost) return;
            _objectCells.Clear();
            ObjectsChanged?.Invoke();
            UpdateReachableForActive();
            await FlushMapObjectsAsync();
        }

        public async Task InitMapObjectsAsync()
        {
            LoadGridItemPalette();
            try
            {
                if (IsHost)
                {
                    var cells = await App.PM.GameDataRepo.LoadMapObjectsAsync(MapId);
                    ApplyObjectState(cells);
                }
                else if (_com != null)
                {
                    var snap = await _com.FetchMapObjectsAsync(MapId);
                    if (snap != null) ApplyObjectState(snap.Cells);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Map objects init failed", ex);
            }
        }

        private void LoadGridItemPalette()
        {
            var items = App.PM?.Rules?.GridItems;
            if (items == null || items.Count == 0) return;
            // Rules might not have loaded the first time this ran, so the count is the check.
            if (GridItemPalette.Count == items.Count) return;
            GridItemPalette.Clear();
            foreach (var g in items.Values) GridItemPalette.Add(g);
            SelectedGridItem ??= GridItemPalette.FirstOrDefault();
        }

        private void HandleMapObjectsUpdated(MapObjectStateMessage msg)
        {
            if (msg.MapId != MapId) return;
            ApplyObjectState(msg.Cells);
        }

        private void ApplyObjectState(IEnumerable<MapObjectPoint> cells)
        {
            _objectCells.Clear();
            foreach (var c in cells)
                if (!string.IsNullOrWhiteSpace(c.ItemId)) _objectCells[(c.Col, c.Row)] = c.ItemId;
            ObjectsChanged?.Invoke();
            UpdateReachableForActive();
        }

        private readonly List<WallViewModel> _walls = new();
        public IReadOnlyList<WallViewModel> Walls => _walls;

        private bool _wallsEnabled;
        public bool WallsEnabled
        {
            get => _wallsEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _wallsEnabled, value);
                WallsChanged?.Invoke();
            }
        }

        public event Action? WallsChanged;

        private const double WallStraightenRatio = 0.4;

        // A wall lives on the corners either way, a drag that is nearly straight gets straightened and a real diagonal is left alone as one clean line.
        public (Point A, Point B) SnapWallEnds(double x1, double y1, double x2, double y2)
        {
            if (!(App.PM?.Rules?.WallsSnapToGrid ?? true)) return (new Point(x1, y1), new Point(x2, y2));
            var cell = CellSize > 0 ? CellSize : GridOverlay.BaseCellPx;
            double Corner(double v) => Math.Round(v / cell) * cell;
            var ax = Corner(x1);
            var ay = Corner(y1);
            var bx = Corner(x2);
            var by = Corner(y2);
            var runX = Math.Abs(bx - ax);
            var runY = Math.Abs(by - ay);
            var major = Math.Max(runX, runY);
            var minor = Math.Min(runX, runY);
            if (major > 0 && minor / major <= WallStraightenRatio)
            {
                if (runX >= runY) by = ay;
                else bx = ax;
            }
            return (new Point(ax, ay), new Point(bx, by));
        }

        public void AddWall(double x1, double y1, double x2, double y2, bool isDoor = false)
        {
            if (!IsHost) return;
            var (a, b) = SnapWallEnds(x1, y1, x2, y2);
            if (Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01) return;
            var wall = new WallViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                X1 = a.X,
                Y1 = a.Y,
                X2 = b.X,
                Y2 = b.Y,
                IsDoor = isDoor,
                DoorOpen = false,
                BlocksSight = true
            };
            _walls.Add(wall);
            WallsChanged?.Invoke();
            _ = BroadcastWallStateAsync();
            PushEdit(
                () => { _walls.Remove(wall); WallsChanged?.Invoke(); _ = BroadcastWallStateAsync(); },
                () => { if (!_walls.Contains(wall)) _walls.Add(wall); WallsChanged?.Invoke(); _ = BroadcastWallStateAsync(); });
        }

        public void DeleteWall(WallViewModel wall)
        {
            if (!IsHost || wall == null) return;
            if (!_walls.Remove(wall)) return;
            WallsChanged?.Invoke();
            _ = BroadcastWallStateAsync();
        }

        public async Task MarkOrToggleDoor(WallViewModel wall)
        {
            if (!IsHost || wall == null) return;
            if (!wall.IsDoor)
            {
                wall.IsDoor = true;
                wall.DoorOpen = false;
                WallsChanged?.Invoke();
                await BroadcastWallStateAsync();
            }
            else
            {
                wall.DoorOpen = !wall.DoorOpen;
                WallsChanged?.Invoke();
                await BroadcastDoorToggleAsync(wall);
            }
        }

        private async Task ToggleWallsEnabledAsync()
        {
            if (!IsHost) return;
            WallsEnabled = !WallsEnabled;
            await BroadcastWallStateAsync();
        }

        private WallStateMessage BuildWallSnapshot() =>
            new WallStateMessage(MapId, WallsEnabled, _walls.Select(w => w.ToMessage()).ToList());

        private async Task BroadcastWallStateAsync()
        {
            if (!IsHost) return;
            if (_com != null && IsBroadcasting)
                await _com.SendWallStateAsync(BuildWallSnapshot());
            await PersistWallsAsync();
        }

        private async Task BroadcastDoorToggleAsync(WallViewModel wall)
        {
            if (!IsHost) return;
            if (_com != null && IsBroadcasting)
                await _com.SendDoorToggleAsync(new DoorToggleMessage(MapId, wall.Id, wall.DoorOpen));
            await PersistWallsAsync();
        }

        private async Task PersistWallsAsync()
        {
            if (!IsHost) return;
            try
            {
                var json = JsonSerializer.Serialize(_walls.Select(w => w.ToMessage()).ToList());
                await App.PM.GameDataRepo.SaveWallsAsync(MapId, WallsEnabled, json);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Wall persist failed", ex);
                NavItem.NavError?.Invoke("Couldn't save the walls for this map, they may come back wrong after a reopen.");
            }
        }

        public async Task InitWallsAsync()
        {
            try
            {
                if (IsHost)
                {
                    var (enabled, walls) = await App.PM.GameDataRepo.LoadWallsAsync(MapId);
                    ApplyWallState(enabled, walls);
                }
                else if (_com != null)
                {
                    var snap = await _com.FetchWallsAsync(MapId);
                    if (snap != null) ApplyWallState(snap.Enabled, snap.Walls);
                }
            }
            catch (Exception ex) { ErrorLog.Log($"[Canvas] wall init failed", ex); }
        }

        private void ApplyWallState(bool enabled, IEnumerable<WallSegment> walls)
        {
            _walls.Clear();
            foreach (var w in walls) _walls.Add(WallViewModel.FromMessage(w));
            _wallsEnabled = enabled;
            this.RaisePropertyChanged(nameof(WallsEnabled));
            WallsChanged?.Invoke();
            UpdateReachableForActive();
        }

        private void HandleWallsUpdated(WallStateMessage msg)
        {
            if (msg.MapId != MapId) return;
            ApplyWallState(msg.Enabled, msg.Walls);
        }

        private void HandleDoorToggled(DoorToggleMessage msg)
        {
            if (msg.MapId != MapId) return;
            var wall = _walls.FirstOrDefault(w => w.Id == msg.WallId);
            if (wall == null) return;
            wall.DoorOpen = msg.Open;
            WallsChanged?.Invoke();
        }

        private readonly List<AoeTemplateViewModel> _aoeTemplates = new();
        public IReadOnlyList<AoeTemplateViewModel> AoeTemplates => _aoeTemplates;
        public event Action? AoeTemplatesChanged;
        public event Action<AoeTemplateViewModel>? AoeTemplatePlacedHere;

        public ObservableCollection<AoeShapeDef> AoeShapes { get; } = new((App.PM?.Rules ?? new GameRules()).AoeShapes);

        private string _aoeShape = "cone";
        public string AoeShape
        {
            get => _aoeShape;
            set { this.RaiseAndSetIfChanged(ref _aoeShape, value); this.RaisePropertyChanged(nameof(AoeUsesWidth)); }
        }

        private decimal _aoeSizeFt = (decimal)(App.PM?.Rules?.DefaultAoeSizeFeet ?? 15.0);
        public decimal AoeSizeFt
        {
            get => _aoeSizeFt;
            set => this.RaiseAndSetIfChanged(ref _aoeSizeFt, value);
        }

        private decimal _aoeWidthFt = (decimal)(App.PM?.Rules?.DefaultAoeWidthFeet ?? 5.0);
        public decimal AoeWidthFt
        {
            get => _aoeWidthFt;
            set => this.RaiseAndSetIfChanged(ref _aoeWidthFt, value);
        }

        private string _aoeColor = "#4F81BD";
        public string AoeColor
        {
            get => _aoeColor;
            set => this.RaiseAndSetIfChanged(ref _aoeColor, value);
        }

        private bool _aoeFromToken = true;
        public bool AoeFromToken
        {
            get => _aoeFromToken;
            set => this.RaiseAndSetIfChanged(ref _aoeFromToken, value);
        }

        public bool AoeUsesWidth => (App.PM?.Rules ?? new GameRules()).AoeShapeUsesWidth(AoeShape);

        public void EnterTemplateMode() => Mode = CanvasToolMode.Template;

        public void PlaceAoeTemplate(double originX, double originY, double directionDeg)
        {
            var t = new AoeTemplateViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                Shape = AoeShape,
                OriginX = originX,
                OriginY = originY,
                DirectionDeg = directionDeg,
                SizeFt = (double)AoeSizeFt,
                WidthFt = (double)AoeWidthFt,
                Color = AoeColor
            };
            _aoeTemplates.Add(t);
            AoeTemplatesChanged?.Invoke();
            AoeTemplatePlacedHere?.Invoke(t);
            _ = PersistAoeTemplatesAsync();
            if (_com != null && IsBroadcasting)
                _ = _com.SendAoeTemplateAsync(t.ToMessage(MapId));
        }

        public void RefreshAfterPersistenceStamp()
        {
            AoeTemplatesChanged?.Invoke();
            TerrainChanged?.Invoke();
            UpdateReachableForActive();
        }

        public async Task<List<string>> SweepTimedAreasAsync()
        {
            if (!IsHost) return new List<string>();
            var gone = new List<string>();
            foreach (var t in _aoeTemplates.Where(t => t.Persists).ToList())
            {
                gone.Add(string.IsNullOrWhiteSpace(t.Label) ? "an area" : t.Label);
                _aoeTemplates.Remove(t);
            }
            if (gone.Count == 0) return gone;

            AoeTemplatesChanged?.Invoke();
            TerrainChanged?.Invoke();
            UpdateReachableForActive();
            await PersistAoeTemplatesAsync();
            if (_com != null && IsBroadcasting)
            {
                await _com.SendClearAoeTemplatesAsync(MapId);
                foreach (var t in _aoeTemplates.ToList())
                    await _com.SendAoeTemplateAsync(t.ToMessage(MapId));
            }
            return gone;
        }

        public async Task<List<string>> DropAreasHeldByAsync(string ownerId)
        {
            if (!IsHost || string.IsNullOrEmpty(ownerId)) return new List<string>();
            var gone = new List<string>();
            foreach (var t in _aoeTemplates.Where(t => t.OwnerId == ownerId).ToList())
            {
                gone.Add(string.IsNullOrWhiteSpace(t.Label) ? "an area" : t.Label);
                _aoeTemplates.Remove(t);
            }
            if (gone.Count == 0) return gone;

            AoeTemplatesChanged?.Invoke();
            TerrainChanged?.Invoke();
            UpdateReachableForActive();
            await PersistAoeTemplatesAsync();
            if (_com != null && IsBroadcasting)
            {
                await _com.SendClearAoeTemplatesAsync(MapId);
                foreach (var t in _aoeTemplates.ToList())
                    await _com.SendAoeTemplateAsync(t.ToMessage(MapId));
            }
            return gone;
        }

        public async Task<List<string>> TickTimedAreasAsync()
        {
            if (!IsHost) return new List<string>();
            var gone = new List<string>();
            foreach (var t in _aoeTemplates.Where(t => t.Persists).ToList())
            {
                t.RoundsLeft--;
                if (t.RoundsLeft > 0) continue;
                gone.Add(string.IsNullOrWhiteSpace(t.Label) ? "an area" : t.Label);
                _aoeTemplates.Remove(t);
            }
            if (gone.Count == 0) return gone;

            AoeTemplatesChanged?.Invoke();
            TerrainChanged?.Invoke();
            UpdateReachableForActive();
            await PersistAoeTemplatesAsync();
            if (_com != null && IsBroadcasting)
            {
                await _com.SendClearAoeTemplatesAsync(MapId);
                foreach (var t in _aoeTemplates.ToList())
                    await _com.SendAoeTemplateAsync(t.ToMessage(MapId));
            }
            return gone;
        }

        private async Task ClearTemplatesAsync()
        {
            if (!IsHost) return;
            ConfirmingClearTemplates = false;
            _aoeTemplates.Clear();
            AoeTemplatesChanged?.Invoke();
            await PersistAoeTemplatesAsync();
            if (_com != null && IsBroadcasting)
                await _com.SendClearAoeTemplatesAsync(MapId);
        }

        private async Task PersistAoeTemplatesAsync()
        {
            if (!IsHost || App.PM == null || string.IsNullOrEmpty(MapId)) return;
            try
            {
                var json = JsonSerializer.Serialize(_aoeTemplates.Select(t => t.ToMessage(MapId)).ToList());
                await App.PM.GameDataRepo.SaveAoeTemplatesAsync(MapId, json);
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] persisting placed templates failed", ex); }
        }

        public async Task InitAoeTemplatesAsync()
        {
            try
            {
                List<AoeTemplateMessage>? placed = null;
                if (IsHost) placed = await App.PM.GameDataRepo.LoadAoeTemplatesAsync(MapId);
                else if (_com != null) placed = (await _com.FetchAoeTemplatesAsync(MapId))?.Templates;
                if (placed == null) return;

                _aoeTemplates.Clear();
                foreach (var m in placed) _aoeTemplates.Add(AoeTemplateViewModel.FromMessage(m));
                AoeTemplatesChanged?.Invoke();
            }
            catch (Exception ex) { ErrorLog.Log("[Canvas] placed template init failed", ex); }
        }

        private void HandleAoeTemplatePlaced(AoeTemplateMessage msg) => ApplyIncomingAoeTemplate(msg);

        public void ApplyIncomingAoeTemplate(AoeTemplateMessage msg)
        {
            if (msg.MapId != MapId) return;
            var incoming = AoeTemplateViewModel.FromMessage(msg);
            var already = _aoeTemplates.FirstOrDefault(t => t.Id == incoming.Id);
            if (already != null) _aoeTemplates.Remove(already);
            _aoeTemplates.Add(incoming);
            AoeTemplatesChanged?.Invoke();
            TerrainChanged?.Invoke();
            _ = PersistAoeTemplatesAsync();
        }

        private void HandleAoeTemplatesCleared(string mapId)
        {
            if (mapId != MapId) return;
            _aoeTemplates.Clear();
            AoeTemplatesChanged?.Invoke();
            _ = PersistAoeTemplatesAsync();
        }

        private void HandleTokenRotated(TokenRotatedMessage msg)
        {
            var token = Tokens.FirstOrDefault(t => t.Id == msg.TokenId);
            if (token == null) return;
            token.Rotation = msg.Rotation;
            if (IsHost) _ = PersistTokenAsync(token);
        }


        private void HandleStrokeReceived(StrokeMessage msg)
        {
            if (Strokes.Any(s => s.Id == msg.StrokeId)) return;

            var stroke = new StrokeViewModel(
                msg.StrokeId,
                msg.Points.Select(p => new Avalonia.Point(p.X, p.Y)).ToList(),
                msg.Color,
                msg.Thickness,
                msg.OwnerId);
            Strokes.Add(stroke);
            if (IsHost) _ = App.PM.GameDataRepo.SaveStrokeAsync(MapId, msg);
        }

        private void HandleStrokeUndone(string strokeId)
        {
            var existing = Strokes.FirstOrDefault(s => s.Id == strokeId);
            if (existing != null) Strokes.Remove(existing);
            if (IsHost) _ = App.PM.GameDataRepo.DeleteStrokeAsync(strokeId);
        }

        private void HandleTokenAdded(TokenAddedMessage msg)
        {
            if (Tokens.Any(t => t.Id == msg.TokenId)) return;

            if (!_imageById.TryGetValue(msg.ImageId, out var bmp))
            {
                bmp = DecodeBitmap(msg.ImageBase64, msg.IsProp);
                if (bmp != null) _imageById[msg.ImageId] = bmp;
            }

            var token = new TokenViewModel(msg.TokenId, bmp, msg.Position.X, msg.Position.Y)
            {
                Scale = msg.Scale,
                Rotation = msg.Rotation,
                Size = msg.Size,
                CellSize = CellSize,
                ImageId = msg.ImageId,
                CharacterId = msg.CharacterId,
                IsProp = msg.IsProp,
                Blocks = msg.Blocks,
                BlocksSight = msg.BlocksSight
            };
            Tokens.Add(token);
            RefreshTokenSides();

            if (IsHost && !string.IsNullOrEmpty(msg.ImageBase64))
            {
                try
                {
                    token.ImagePath = TokenImageGuard.SaveForCampaign(App.PM.GetCampaignId(), Convert.FromBase64String(msg.ImageBase64), msg.IsProp);
                    _ = PersistTokenAsync(token);
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("Remote token persist failed", ex);
                    NavItem.NavError?.Invoke("Couldn't save the token art a player sent, the token may come back without it.");
                }
            }
        }

        private void HandleTokenMoved(TokenMovedMessage msg)
        {
            var token = Tokens.FirstOrDefault(t => t.Id == msg.TokenId);
            if (token == null) return;
            var fromX = token.X;
            var fromY = token.Y;
            token.X = msg.NewPosition.X;
            token.Y = msg.NewPosition.Y;
            AccountFeet(token);
            UpdateReachableForActive();
            if (IsHost)
            {
                // The dragger has no dm panel to resolve their own opportunity attack, so the host redoes the reach check.
                CheckOpportunityAttacks(token, fromX, fromY);
                _ = PersistTokenAsync(token);
            }
        }

        private void HandleTokenRemoved(TokenRemovedMessage msg)
        {
            var token = Tokens.FirstOrDefault(t => t.Id == msg.TokenId);
            if (token != null) Tokens.Remove(token);
            // Host owns the db, without this a token a player deleted comes back on the next open.
            if (IsHost) _ = App.PM.GameDataRepo.DeleteTokenAsync(msg.TokenId);
        }

        public void Detach()
        {
            _gridSub?.Dispose();
            if (_com == null) return;
            _com.OnStrokeReceived -= HandleStrokeReceived;
            _com.OnTokenAdded -= HandleTokenAdded;
            _com.OnTokenMoved -= HandleTokenMoved;
            _com.OnTokenRemoved -= HandleTokenRemoved;
            _com.OnStrokeUndone -= HandleStrokeUndone;
            _com.OnTokenResized -= HandleTokenResized;
            _com.OnPingReceived -= HandlePingReceived;
            _com.OnTokenRotated -= HandleTokenRotated;
            _com.OnPlayerColorChanged -= HandleColorChanged;
            _com.OnFogPainted -= HandleFogPainted;
            _com.OnFogUpdated -= HandleFogUpdated;
            _com.OnWallsUpdated -= HandleWallsUpdated;
            _com.OnTerrainUpdated -= HandleTerrainUpdated;
            _com.OnMapObjectsUpdated -= HandleMapObjectsUpdated;
            _com.OnDoorToggled -= HandleDoorToggled;
            _com.OnAoeTemplatePlaced -= HandleAoeTemplatePlaced;
            _com.OnAoeTemplatesCleared -= HandleAoeTemplatesCleared;
        }
    }

    public class TokenLibraryEntryViewModel : ViewModelBase
    {
        public CampaignTokenAsset Asset { get; }
        public Bitmap? Preview { get; }
        public string ImageId { get; }

        public string Name => Asset.Name;
        public bool IsColor => string.Equals(Asset.Kind, "color", StringComparison.OrdinalIgnoreCase);

        private string? _monsterName;
        public string? MonsterName
        {
            get => _monsterName;
            set
            {
                this.RaiseAndSetIfChanged(ref _monsterName, value);
                this.RaisePropertyChanged(nameof(HasMonster));
                this.RaisePropertyChanged(nameof(SubLabel));
            }
        }
        public bool HasMonster => !string.IsNullOrEmpty(MonsterName);
        public string SubLabel => HasMonster ? MonsterName! : (IsColor ? "color" : "image");

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

        public ReactiveCommand<Unit, Unit> SelectCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveCommand { get; }

        public TokenLibraryEntryViewModel(CampaignTokenAsset asset, Bitmap? preview, string imageId,
            Action<TokenLibraryEntryViewModel> onSelect, Func<TokenLibraryEntryViewModel, Task> onRemove)
        {
            Asset = asset;
            Preview = preview;
            ImageId = imageId;
            SelectCommand = ReactiveCommand.Create(() => onSelect(this));
            RemoveCommand = ReactiveCommand.CreateFromTask(() => onRemove(this));
        }
    }

    public class StrokeViewModel : ViewModelBase
    {
        public string Id { get; }
        public List<Point> Points { get; }
        public string Color { get; }
        public double Thickness { get; }
        public string OwnerId { get; }

        public StrokeViewModel(string id, List<Point> points, string color, double thickness, string ownerId = "")
        {
            Id = id;
            Points = points;
            Color = color;
            Thickness = thickness;
            OwnerId = ownerId;
        }
    }

    public enum TokenSide { None, Friendly, Enemy }

    public class TokenViewModel : ViewModelBase
    {
        public string Id { get; }
        public string? ImageId { get; set; }
        public string? ImagePath { get; set; }
        public Bitmap? Image { get; }

        private double _x;
        public double X { get => _x; set => this.RaiseAndSetIfChanged(ref _x, value); }

        private double _y;
        public double Y { get => _y; set => this.RaiseAndSetIfChanged(ref _y, value); }

        private double _scale = 1.0;
        public double Scale { get => _scale; set => this.RaiseAndSetIfChanged(ref _scale, value); }

        private double _rotation;
        public double Rotation { get => _rotation; set => this.RaiseAndSetIfChanged(ref _rotation, value); }

        private bool _isActiveCombatant;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }
        public bool IsActiveCombatant
        {
            get => _isActiveCombatant;
            set => this.RaiseAndSetIfChanged(ref _isActiveCombatant, value);
        }

        private CreatureSize _size = CreatureSize.Medium;
        public CreatureSize Size
        {
            get => _size;
            set
            {
                this.RaiseAndSetIfChanged(ref _size, value);
                this.RaisePropertyChanged(nameof(PixelSize));
            }
        }

        private double _cellSize = GridOverlay.BaseCellPx;
        public double CellSize
        {
            get => _cellSize;
            set
            {
                this.RaiseAndSetIfChanged(ref _cellSize, value);
                this.RaisePropertyChanged(nameof(PixelSize));
            }
        }

        public double PixelSize => (Size.ToPixels() / GridOverlay.BaseCellPx) * CellSize * Scale;

        public string? CombatantId { get; set; }

        public string? CharacterId { get; set; }

        private bool _isProp;
        public bool IsProp
        {
            get => _isProp;
            set => this.RaiseAndSetIfChanged(ref _isProp, value);
        }

        private bool _blocks = true;
        public bool Blocks
        {
            get => _blocks;
            set => this.RaiseAndSetIfChanged(ref _blocks, value);
        }

        private bool _blocksSight;
        public bool BlocksSight
        {
            get => _blocksSight;
            set => this.RaiseAndSetIfChanged(ref _blocksSight, value);
        }

        private TokenSide _side = TokenSide.None;
        public TokenSide Side
        {
            get => _side;
            set => this.RaiseAndSetIfChanged(ref _side, value);
        }

        private double _feetMoved;
        public double FeetMoved
        {
            get => _feetMoved;
            set
            {
                this.RaiseAndSetIfChanged(ref _feetMoved, value);
                this.RaisePropertyChanged(nameof(FeetLabel));
                this.RaisePropertyChanged(nameof(HasMoved));
            }
        }
        public double FeetAnchorX { get; set; }
        public double FeetAnchorY { get; set; }
        public double TurnStartX { get; set; }
        public double TurnStartY { get; set; }
        public string FeetLabel
        {
            get
            {
                double ft = App.PM?.Rules.FeetPerSquare ?? 5.0;
                return ((int)(Math.Round(FeetMoved / ft) * ft)).ToString() + " ft";
            }
        }
        public bool HasMoved => FeetMoved >= (App.PM?.Rules?.MovedThresholdFeet ?? 2.5);

        public TokenViewModel(string id, Bitmap? image, double x, double y)
        {
            Id = id;
            Image = image;
            _x = x;
            _y = y;
            FeetAnchorX = x;
            FeetAnchorY = y;
            TurnStartX = x;
            TurnStartY = y;
        }
    }
    public class PingViewModel : ViewModelBase
    {
        public string Id { get; }
        public double X { get; }
        public double Y { get; }
        public string Color { get; }
        public DateTime CreatedAt { get; }

        public PingViewModel(string id, double x, double y, string color)
        {
            Id = id;
            X = x;
            Y = y;
            Color = color;
            CreatedAt = DateTime.UtcNow;
        }
    }
    public class CombatantAttackViewModel : ViewModelBase
    {
        public string Name { get; }
        public int ToHit { get; }
        public string Damage { get; }
        public string DamageType { get; }
        public int RangeFeet { get; }
        public int RangeMaxFeet { get; }
        public string Mastery { get; }
        public string AreaShape { get; }
        public double AreaSizeFt { get; }
        public double AreaWidthFt { get; }
        public string SaveAbility { get; }
        public int SaveDc { get; }
        public int RechargeOn { get; }

        private bool _spent;
        public bool IsSpent
        {
            get => _spent;
            set => this.RaiseAndSetIfChanged(ref _spent, value);
        }
        public bool NeedsRecharge => RechargeOn > 0;
        public bool IsReady => !NeedsRecharge || !IsSpent;
        public string RechargeLabel => !NeedsRecharge ? "" : IsSpent ? ", spent" : ", recharge " + RechargeOn + "+";

        public bool IsArea => !string.IsNullOrWhiteSpace(AreaShape) && AreaSizeFt > 0;
        public bool AutoHit => ToHit >= (App.PM?.Rules?.AutoHitToHit ?? 99);
        public string ToHitLabel => AutoHit ? "auto" : (ToHit >= 0 ? "+" + ToHit : ToHit.ToString());
        public string RangeLabel => RangeFeet > 0 ? ", " + RangeFeet + " ft" : "";
        public string MasteryLabel => string.IsNullOrWhiteSpace(Mastery) ? "" : ", " + (App.PM?.Rules?.Masteries.TryGetValue(Mastery, out var m) == true ? m.Name : Mastery);
        public string AreaLabel => IsArea ? ", " + AreaSizeFt + " ft " + AreaShape + (SaveDc > 0 ? ", DC " + SaveDc + " " + SaveAbility : "") : "";
        public string Subtitle => (IsArea
            ? Damage + " " + DamageType + AreaLabel
            : (AutoHit ? "auto hit, " + Damage + " " + DamageType : ToHitLabel + " to hit, " + Damage + " " + DamageType) + RangeLabel) + MasteryLabel + RechargeLabel;

        public CombatantAttackViewModel(string name, int toHit, string damage, string damageType, int rangeFeet = 0, string mastery = "", int rangeMaxFeet = 0,
            string areaShape = "", double areaSizeFt = 0, double areaWidthFt = 0, string saveAbility = "", int saveDc = 0, int rechargeOn = 0)
        {
            RechargeOn = rechargeOn;
            AreaShape = areaShape ?? "";
            AreaSizeFt = areaSizeFt;
            AreaWidthFt = areaWidthFt;
            SaveAbility = saveAbility ?? "";
            SaveDc = saveDc;
            Name = name;
            ToHit = toHit;
            Damage = damage;
            DamageType = damageType;
            RangeFeet = rangeFeet;
            RangeMaxFeet = rangeMaxFeet;
            Mastery = mastery ?? "";
        }
    }

    public record AttackDto(string Name, int ToHit, string Damage, string DamageType, int RangeFeet = 0, string Mastery = "", int RangeMaxFeet = 0,
        string AreaShape = "", double AreaSizeFt = 0, double AreaWidthFt = 0, string SaveAbility = "", int SaveDc = 0, int RechargeOn = 0, bool IsSpent = false);
    public record ConcentrationLink(string TargetId, string Condition);
    public record TimedConditionDto(string Condition, int Rounds, string ExpiresAt, string SourceId);
    public record TimedBuffDto(string Target, int Value, int Rounds, string ExpiresAt, string Label, string Dice = "", string SourceId = "");
    public record CombatExtrasDto(int TempHp, int SpeedFeet, List<string> Resist, List<string> Immune, List<string> Vulnerable, List<string> Conditions = null!, List<int> Saves = null!, int BaseMaxReactions = 1, int MaxReactions = 1, int ReactionsRemaining = 1, List<ConcentrationLink> ConcEffects = null!, bool Dodging = false, bool Disengaged = false, bool Hidden = false, Dictionary<string, int> AbilitySaves = null!, int AttacksPerAction = 1, int LegendaryPerRound = 0, int LegendaryRemaining = 0, List<LegendaryActionOption> Legendary = null!, int LairInitiative = 0, int LairUsedInRound = 0, List<LairActionOption> Lair = null!, string OwnerCharacterId = "", int ArmorClass = 0, int DexMod = 0, int ConSaveBonus = 0, int CharacterLevel = 0, bool Dashed = false, bool Readied = false, int SurgeActionGrant = 0, int SurgeBonusGrant = 0, int SurgeUsesMax = 0, int SurgeUsesSpent = 0, int AttacksThisAction = 0, List<RiderRule> Riders = null!, bool OffHandAbilityMod = false, string InspirationDie = "", bool ConcentrationSummon = false, Dictionary<string, string> ActionCostOverrides = null!, bool DashPaid = false, int SlowPenaltyFeet = 0, string SlowSourceId = "", List<string> RidersUsed = null!, List<TimedConditionDto> TimedConditions = null!, List<TimedBuffDto> TimedBuffs = null!, List<ConditionalBonus> Conditional = null!, List<GrantedReaction> GrantedReactions = null!, int ExhaustionLevel = 0, bool HasInspiration = false, bool KilledOutright = false, int CoverBonus = 0, bool ManualAdvantage = false, bool ManualDisadvantage = false, List<string> AdvantageOn = null!, int ContestBonus = 0, int BaseMaxActions = 1, int BaseMaxBonusActions = 1, string ReadiedIntent = "", string ReadiedTrigger = "", bool Sapped = false, string VexTargetId = "", string HelpedTargetId = "", int BonusSwings = 0, bool CleavedThisTurn = false, bool NickedThisTurn = false, bool Surprised = false, List<string> Senses = null!, string PendingSaveAbility = "", int PendingSaveDc = 0, int PendingSaveDamage = 0, string PendingSaveDamageType = "", bool PendingSaveHalf = false, string PendingSaveSource = "");

    public class SpellSlotRowViewModel : ViewModelBase
    {
        public int Level { get; }

        private int _max;
        public int Max { get => _max; set { this.RaiseAndSetIfChanged(ref _max, value); RaiseDerived(); } }

        private int _used;
        public int Used { get => _used; set { this.RaiseAndSetIfChanged(ref _used, value); RaiseDerived(); } }

        public int Remaining => Max - Used;
        public bool CanSpend => Used < Max;
        public bool CanRestore => Used > 0;
        public string Label => "L" + Level + "  " + Remaining + "/" + Max;

        public SpellSlotRowViewModel(int level, int max, int used)
        {
            Level = level;
            _max = max;
            _used = used;
        }

        private void RaiseDerived()
        {
            this.RaisePropertyChanged(nameof(Remaining));
            this.RaisePropertyChanged(nameof(CanSpend));
            this.RaisePropertyChanged(nameof(CanRestore));
            this.RaisePropertyChanged(nameof(Label));
        }
    }

    public class CombatantViewModel : ViewModelBase
    {
        public string Id { get; }
        public bool IsPlayerCharacter { get; }
        public string? TokenId { get; set; }

        private Bitmap? _portrait;
        public Bitmap? Portrait
        {
            get => _portrait;
            set { this.RaiseAndSetIfChanged(ref _portrait, value); this.RaisePropertyChanged(nameof(HasPortrait)); }
        }
        public bool HasPortrait => _portrait != null;

        public int DexMod { get; set; }
        public int ConSaveBonus { get; set; }

        private int _armorClass;
        public int ArmorClass
        {
            get => _armorClass;
            set => this.RaiseAndSetIfChanged(ref _armorClass, value);
        }

        public List<ConditionalBonus> Conditional { get; } = new();
        public List<GrantedReaction> GrantedReactions { get; } = new();

        public int ConditionalBonusFor(string target)
        {
            var total = 0;
            foreach (var c in Conditional)
                if (string.Equals(c.Target, target, StringComparison.OrdinalIgnoreCase) && c.Applies(Conditions, CurrentHp, MaxHp))
                    total += c.Value;
            return total;
        }

        public int EffectiveArmorClass => _armorClass + BuffBonusFor("armor-class") + ConditionalBonusFor("armor-class");

        private string _readiedIntent = "";
        public string ReadiedIntent
        {
            get => _readiedIntent;
            set { this.RaiseAndSetIfChanged(ref _readiedIntent, value ?? ""); this.RaisePropertyChanged(nameof(ReadiedLabel)); }
        }

        private string _readiedTrigger = "";
        public string ReadiedTrigger
        {
            get => _readiedTrigger;
            set { this.RaiseAndSetIfChanged(ref _readiedTrigger, value ?? ""); this.RaisePropertyChanged(nameof(ReadiedLabel)); }
        }

        public string ReadiedLabel =>
            !Readied ? "" :
            string.IsNullOrWhiteSpace(_readiedIntent) && string.IsNullOrWhiteSpace(_readiedTrigger) ? "Readied"
            : "Readied: " + (_readiedIntent.Length > 0 ? _readiedIntent : "something")
              + (_readiedTrigger.Length > 0 ? " when " + _readiedTrigger : "");

        public ObservableCollection<CombatantAttackViewModel> Attacks { get; } = new();
        public bool HasAttacks => Attacks.Count > 0;

        public string SerializeAttacks() => JsonSerializer.Serialize(Attacks.Select(a => new AttackDto(a.Name, a.ToHit, a.Damage, a.DamageType, a.RangeFeet, a.Mastery, a.RangeMaxFeet,
            a.AreaShape, a.AreaSizeFt, a.AreaWidthFt, a.SaveAbility, a.SaveDc, a.RechargeOn, a.IsSpent)).ToList());

        // Parse into a temp list before I touch the live one, a blob that won't deserialize shouldn't wipe the attacks it already has.
        public void ApplyAttacks(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) { Attacks.Clear(); return; }
            List<AttackDto>? list;
            try
            {
                list = JsonSerializer.Deserialize<List<AttackDto>>(json);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Could not read the attacks for " + Name + ", keeping the ones already loaded", ex);
                return;
            }
            if (list == null) return;
            Attacks.Clear();
            foreach (var a in list) Attacks.Add(new CombatantAttackViewModel(a.Name, a.ToHit, a.Damage, a.DamageType, a.RangeFeet, a.Mastery, a.RangeMaxFeet,
                a.AreaShape, a.AreaSizeFt, a.AreaWidthFt, a.SaveAbility, a.SaveDc, a.RechargeOn) { IsSpent = a.IsSpent });
        }

        private int _baseMaxActions = 1;
        public int BaseMaxActions { get => _baseMaxActions; set => this.RaiseAndSetIfChanged(ref _baseMaxActions, value); }

        private int _baseMaxBonusActions = 1;
        public int BaseMaxBonusActions { get => _baseMaxBonusActions; set => this.RaiseAndSetIfChanged(ref _baseMaxBonusActions, value); }

        private int _maxActions = 1;
        public int MaxActions { get => _maxActions; set { this.RaiseAndSetIfChanged(ref _maxActions, value); RaiseEconomyDerived(); } }

        private int _actionsRemaining = 1;
        public int ActionsRemaining { get => _actionsRemaining; set { this.RaiseAndSetIfChanged(ref _actionsRemaining, value); RaiseEconomyDerived(); } }

        private int _maxBonusActions = 1;
        public int MaxBonusActions { get => _maxBonusActions; set { this.RaiseAndSetIfChanged(ref _maxBonusActions, value); RaiseEconomyDerived(); } }

        private int _bonusActionsRemaining = 1;
        public int BonusActionsRemaining { get => _bonusActionsRemaining; set { this.RaiseAndSetIfChanged(ref _bonusActionsRemaining, value); RaiseEconomyDerived(); } }

        private int _baseMaxReactions = 1;
        public int BaseMaxReactions { get => _baseMaxReactions; set => this.RaiseAndSetIfChanged(ref _baseMaxReactions, value); }

        private int _maxReactions = 1;
        public int MaxReactions { get => _maxReactions; set { this.RaiseAndSetIfChanged(ref _maxReactions, value); RaiseEconomyDerived(); } }

        private int _reactionsRemaining = 1;
        public int ReactionsRemaining { get => _reactionsRemaining; set { this.RaiseAndSetIfChanged(ref _reactionsRemaining, value); RaiseEconomyDerived(); } }

        private bool _dashed;
        public bool Dashed { get => _dashed; set => this.RaiseAndSetIfChanged(ref _dashed, value); }
        public bool DashPaid { get; set; }

        private int _surgeActionGrant;
        public int SurgeActionGrant { get => _surgeActionGrant; set { this.RaiseAndSetIfChanged(ref _surgeActionGrant, value); RaiseSurgeDerived(); } }

        private int _surgeBonusGrant;
        public int SurgeBonusGrant { get => _surgeBonusGrant; set => this.RaiseAndSetIfChanged(ref _surgeBonusGrant, value); }

        private int _surgeUsesMax;
        public int SurgeUsesMax { get => _surgeUsesMax; set { this.RaiseAndSetIfChanged(ref _surgeUsesMax, value); RaiseSurgeDerived(); } }

        private int _surgeUsesSpent;
        public int SurgeUsesSpent { get => _surgeUsesSpent; set { this.RaiseAndSetIfChanged(ref _surgeUsesSpent, value); RaiseSurgeDerived(); } }

        public bool HasSurge => (SurgeActionGrant > 0 || SurgeBonusGrant > 0) && SurgeUsesMax > 0;
        public bool CanActionSurge => HasSurge && SurgeUsesSpent < SurgeUsesMax;
        public string SurgeLabel => SurgeUsesMax > 0 ? "Surge (" + (SurgeUsesMax - SurgeUsesSpent) + "/" + SurgeUsesMax + ")" : "Surge";

        public bool IsIncapacitated => ConditionEffects.IsIncapacitated(Conditions);
        public bool CanSpendAction => ActionsRemaining > 0 && !IsIncapacitated;
        public bool CanSpendBonusAction => BonusActionsRemaining > 0 && !IsIncapacitated;
        public bool CanSpendReaction => ReactionsRemaining > 0 && !IsIncapacitated
            && !(App.PM?.Rules?.BlocksReactions(Conditions) ?? false);
        public string EconomyLabel => "Act " + ActionsRemaining + "/" + MaxActions + "   Bns " + BonusActionsRemaining + "/" + MaxBonusActions + "   Rea " + ReactionsRemaining + "/" + MaxReactions + (IsIncapacitated ? "   (incapacitated)" : "");

        private void RaiseEconomyDerived()
        {
            this.RaisePropertyChanged(nameof(CanSpendAction));
            this.RaisePropertyChanged(nameof(CanSpendBonusAction));
            this.RaisePropertyChanged(nameof(CanSpendReaction));
            this.RaisePropertyChanged(nameof(EconomyLabel));
        }

        private void RaiseSurgeDerived()
        {
            this.RaisePropertyChanged(nameof(HasSurge));
            this.RaisePropertyChanged(nameof(CanActionSurge));
            this.RaisePropertyChanged(nameof(SurgeLabel));
        }

        private int _attacksPerAction = 1;
        public int AttacksPerAction { get => _attacksPerAction; set => this.RaiseAndSetIfChanged(ref _attacksPerAction, Math.Max(1, value)); }

        public int AttacksThisAction { get; set; }

        // A cleave swing rides on top of the multiattack budget rather than out of it, and you still only get the one
        public int BonusSwings { get; set; }
        public bool CleavedThisTurn { get; set; }
        public bool NickedThisTurn { get; set; }

        public List<RiderRule> Riders { get; } = new();
        public Dictionary<string, string> ActionCostOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string CostForAction(string key, string templateCost) =>
            !string.IsNullOrWhiteSpace(key) && ActionCostOverrides.TryGetValue(key, out var c) && !string.IsNullOrWhiteSpace(c) ? c : templateCost;

        public HashSet<string> AdvantageOn { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RidersUsedThisTurn { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int CharacterLevel { get; set; } = 1;

        private string _inspirationDie = "";
        public string InspirationDie { get => _inspirationDie; set => this.RaiseAndSetIfChanged(ref _inspirationDie, value ?? ""); }

        public bool HasSwingLeftInThisAction => AttacksThisAction > 0 && AttacksThisAction < AttacksPerAction + BonusSwings;

        private int _legendaryPerRound;
        public int LegendaryPerRound
        {
            get => _legendaryPerRound;
            set { this.RaiseAndSetIfChanged(ref _legendaryPerRound, Math.Max(0, value)); this.RaisePropertyChanged(nameof(HasLegendaryActions)); this.RaisePropertyChanged(nameof(LegendaryLabel)); }
        }

        private int _legendaryRemaining;
        public int LegendaryRemaining
        {
            get => _legendaryRemaining;
            set { this.RaiseAndSetIfChanged(ref _legendaryRemaining, Math.Clamp(value, 0, LegendaryPerRound)); this.RaisePropertyChanged(nameof(LegendaryLabel)); }
        }

        public ObservableCollection<LegendaryActionOption> LegendaryActions { get; } = new();
        public bool HasLegendaryActions => LegendaryPerRound > 0;
        public string LegendaryLabel => "Legendary " + LegendaryRemaining + "/" + LegendaryPerRound;

        // Legendary actions go between turns, so the active creature is the one that cannot take one.
        public bool CanUseLegendary(int cost) => HasLegendaryActions && !IsActive && !IsIncapacitated && cost > 0 && cost <= LegendaryRemaining;

        public bool SpendLegendary(int cost)
        {
            if (!CanUseLegendary(cost)) return false;
            LegendaryRemaining -= cost;
            return true;
        }

        private string _ownerCharacterId = "";
        public string OwnerCharacterId
        {
            get => _ownerCharacterId;
            set { this.RaiseAndSetIfChanged(ref _ownerCharacterId, value ?? ""); this.RaisePropertyChanged(nameof(IsSummon)); }
        }

        public bool IsSummon => !string.IsNullOrEmpty(OwnerCharacterId);

        // A player drives their own sheet and anything they conjured, and nothing else.
        public bool IsDrivenBy(string? characterId) =>
            !string.IsNullOrEmpty(characterId) &&
            ((IsPlayerCharacter && string.Equals(Id, characterId, StringComparison.OrdinalIgnoreCase))
             || string.Equals(OwnerCharacterId, characterId, StringComparison.OrdinalIgnoreCase));

        private int _lairInitiative;
        public int LairInitiative
        {
            get => _lairInitiative;
            set { this.RaiseAndSetIfChanged(ref _lairInitiative, Math.Max(0, value)); this.RaisePropertyChanged(nameof(HasLairActions)); this.RaisePropertyChanged(nameof(LairLabel)); }
        }

        // The round it last went off in, zero means it has not yet, and it is a round number rather than a flag so a rewound turn does not hand out a second one
        public int LairUsedInRound { get; set; }

        public ObservableCollection<LairActionOption> LairActions { get; } = new();
        public bool HasLairActions => LairActions.Count > 0;
        public string LairLabel => "Lair, on count " + LairInitiative;

        public bool CanUseLairIn(int round) => HasLairActions && round > 0 && CurrentHp > 0 && LairUsedInRound != round;

        public bool UseLairIn(int round)
        {
            if (!CanUseLairIn(round)) return false;
            LairUsedInRound = round;
            return true;
        }

        public void ResetTurnEconomy()
        {
            MaxActions = BaseMaxActions;
            MaxBonusActions = BaseMaxBonusActions;
            MaxReactions = BaseMaxReactions;
            ActionsRemaining = MaxActions;
            BonusActionsRemaining = MaxBonusActions;
            ReactionsRemaining = MaxReactions;
            AttacksThisAction = 0;
            BonusSwings = 0;
            CleavedThisTurn = false;
            NickedThisTurn = false;
            RidersUsedThisTurn.Clear();
            LegendaryRemaining = LegendaryPerRound;
            Dashed = false;
            DashPaid = false;
            Readied = false;
        }

        public bool SpendAction()
        {
            if (ActionsRemaining <= 0) return false;
            ActionsRemaining--;
            return true;
        }

        public bool SpendBonusAction()
        {
            if (BonusActionsRemaining <= 0) return false;
            BonusActionsRemaining--;
            return true;
        }

        public bool SpendReaction()
        {
            if (ReactionsRemaining <= 0) return false;
            ReactionsRemaining--;
            return true;
        }

        public bool UseActionSurge()
        {
            if (!CanActionSurge) return false;
            SurgeUsesSpent++;
            MaxActions += SurgeActionGrant;
            ActionsRemaining += SurgeActionGrant;
            MaxBonusActions += SurgeBonusGrant;
            BonusActionsRemaining += SurgeBonusGrant;
            return true;
        }

        public ObservableCollection<SpellSlotRowViewModel> SpellSlots { get; } = new();
        public bool HasSpellSlots => SpellSlots.Count > 0;

        public void SetSpellSlots(Dictionary<int, int> max, Dictionary<int, int>? used = null)
        {
            SpellSlots.Clear();
            foreach (var lvl in max.Keys.OrderBy(k => k))
            {
                if (max[lvl] <= 0) continue;
                var u = used != null && used.TryGetValue(lvl, out var uu) ? uu : 0;
                if (u > max[lvl]) u = max[lvl];
                SpellSlots.Add(new SpellSlotRowViewModel(lvl, max[lvl], u));
            }
            this.RaisePropertyChanged(nameof(HasSpellSlots));
        }

        public bool HasSlot(int level) => SpellSlots.FirstOrDefault(r => r.Level == level)?.CanSpend ?? false;

        public bool SpendSlot(int level)
        {
            var row = SpellSlots.FirstOrDefault(r => r.Level == level);
            if (row == null || !row.CanSpend) return false;
            row.Used++;
            return true;
        }

        public void RestoreSlot(int level)
        {
            var row = SpellSlots.FirstOrDefault(r => r.Level == level);
            if (row != null && row.Used > 0) row.Used--;
        }

        public void LongRestSlots()
        {
            foreach (var r in SpellSlots) r.Used = 0;
        }

        public string SerializeSlots() => string.Join(";", SpellSlots.Select(r => r.Level + ":" + r.Max + ":" + r.Used));

        public void ApplySlots(string? data)
        {
            SpellSlots.Clear();
            if (!string.IsNullOrWhiteSpace(data))
                foreach (var part in data!.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = part.Split(':');
                    if (bits.Length == 3
                        && int.TryParse(bits[0], out var l)
                        && int.TryParse(bits[1], out var m)
                        && int.TryParse(bits[2], out var u))
                        SpellSlots.Add(new SpellSlotRowViewModel(l, m, u));
                }
            this.RaisePropertyChanged(nameof(HasSpellSlots));
        }

        public override string ToString() => Name;

        private string _name = "";
        public string Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }

        private int _initiative;
        public int Initiative
        {
            get => _initiative;
            set => this.RaiseAndSetIfChanged(ref _initiative, value);
        }

        private int _currentHp;
        public int CurrentHp
        {
            get => _currentHp;
            set
            {
                this.RaiseAndSetIfChanged(ref _currentHp, value);
                if (value > 0 && (_deathSaveSuccesses != 0 || _deathSaveFailures != 0))
                {
                    DeathSaveSuccesses = 0;
                    DeathSaveFailures = 0;
                }
                this.RaisePropertyChanged(nameof(IsDowned));
                this.RaisePropertyChanged(nameof(CanRollDeathSave));
                this.RaisePropertyChanged(nameof(DeathSaveStatus));
            }
        }

        private int _maxHp;
        public int MaxHp
        {
            get => _maxHp;
            set => this.RaiseAndSetIfChanged(ref _maxHp, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => this.RaiseAndSetIfChanged(ref _isSelected, value);
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => this.RaiseAndSetIfChanged(ref _isActive, value);
        }

        private bool _concentration;
        public bool Concentration
        {
            get => _concentration;
            set => this.RaiseAndSetIfChanged(ref _concentration, value);
        }

        private bool _concentrationSummon;
        public bool ConcentrationSummon
        {
            get => _concentrationSummon;
            set => this.RaiseAndSetIfChanged(ref _concentrationSummon, value);
        }

        private int _deathSaveSuccesses;
        public int DeathSaveSuccesses
        {
            get => _deathSaveSuccesses;
            set { this.RaiseAndSetIfChanged(ref _deathSaveSuccesses, value); RaiseDeathDerived(); }
        }
        private int _deathSaveFailures;
        public int DeathSaveFailures
        {
            get => _deathSaveFailures;
            set { this.RaiseAndSetIfChanged(ref _deathSaveFailures, value); RaiseDeathDerived(); }
        }
        public bool IsDowned => CurrentHp <= 0;
        private int DeathSuccessTarget => App.PM?.Rules.DeathSaveSuccessesToStabilize ?? 3;
        private int DeathFailTarget => App.PM?.Rules.DeathSaveFailuresToDie ?? 3;
        public bool IsStableFromSaves => IsDowned && DeathSaveSuccesses >= DeathSuccessTarget && DeathSaveFailures < DeathFailTarget;
        public bool IsDeadFromSaves => DeathSaveFailures >= DeathFailTarget;
        public bool CanRollDeathSave => IsDowned && !IsStableFromSaves && !IsDeadFromSaves;
        public string DeathSaveStatus => IsDeadFromSaves ? "Dead" : IsStableFromSaves ? "Stable" : "Death Saves";
        public string DeathSaveTally => "Success " + DeathSaveSuccesses + "/" + DeathSuccessTarget + "   Fail " + DeathSaveFailures + "/" + DeathFailTarget;

        private void RaiseDeathDerived()
        {
            this.RaisePropertyChanged(nameof(IsStableFromSaves));
            this.RaisePropertyChanged(nameof(IsDeadFromSaves));
            this.RaisePropertyChanged(nameof(CanRollDeathSave));
            this.RaisePropertyChanged(nameof(DeathSaveStatus));
            this.RaisePropertyChanged(nameof(DeathSaveTally));
        }

        private int _coverBonus;
        public int CoverBonus
        {
            get => _coverBonus;
            set { this.RaiseAndSetIfChanged(ref _coverBonus, value); this.RaisePropertyChanged(nameof(CoverLabel)); }
        }
        public string CoverLabel => _coverBonus > 0 ? "Cover +" + _coverBonus + " AC" : "No cover";

        private bool _flanking;
        public bool Flanking
        {
            get => _flanking;
            set => this.RaiseAndSetIfChanged(ref _flanking, value);
        }

        private bool _isFriendly;
        public bool IsFriendly
        {
            get => _isFriendly;
            set { this.RaiseAndSetIfChanged(ref _isFriendly, value); this.RaisePropertyChanged(nameof(OnPlayerSide)); }
        }

        // Players and any npc the dm flagged friendly share the player team, everything else is the enemy team, this is the split the auto flanking and opportunity attacks read.
        public bool OnPlayerSide => IsFriendly || IsPlayerCharacter;
        public bool CanToggleFriendly => !IsPlayerCharacter;

        private int _tempHp;
        public int TempHp
        {
            get => _tempHp;
            set { this.RaiseAndSetIfChanged(ref _tempHp, Math.Max(0, value)); this.RaisePropertyChanged(nameof(HasTempHp)); this.RaisePropertyChanged(nameof(TempHpLabel)); }
        }
        public bool HasTempHp => _tempHp > 0;
        public string TempHpLabel => "+" + _tempHp + " temp";

        private int _speedFeet = 30;
        public int SpeedFeet { get => _speedFeet; set => this.RaiseAndSetIfChanged(ref _speedFeet, Math.Max(0, value)); }

        private int _exhaustionLevel;
        public int ExhaustionLevel
        {
            get => _exhaustionLevel;
            set
            {
                this.RaiseAndSetIfChanged(ref _exhaustionLevel, Math.Max(0, value));
                this.RaisePropertyChanged(nameof(EffectiveSpeedFeet));
                this.RaisePropertyChanged(nameof(EffectiveMaxHp));
            }
        }

        // What the movement overlay and the ruler spend, the raw speed bent by whatever conditions and exhaustion are riding on it
        public int EffectiveSpeedFeet
        {
            get
            {
                var rules = App.PM?.Rules;
                var mult = (rules?.SpeedMultiplierFrom(Conditions) ?? 1.0) * (rules?.Exhaustion?.SpeedMultiplier(_exhaustionLevel) ?? 1.0);
                var penalty = rules?.Exhaustion?.SpeedPenaltyFeet(_exhaustionLevel) ?? 0;
                return Math.Max(0, (int)Math.Floor(_speedFeet * mult) - penalty);
            }
        }

        public int EffectiveMaxHp
        {
            get
            {
                var rules = App.PM?.Rules;
                var mult = (rules?.MaxHpMultiplierFrom(Conditions) ?? 1.0) * (rules?.Exhaustion?.MaxHpMultiplier(_exhaustionLevel) ?? 1.0);
                return Math.Max(1, (int)Math.Floor(MaxHp * mult));
            }
        }

        public string? VexTargetId { get; set; }
        public bool Sapped { get; set; }
        public int SlowPenaltyFeet { get; set; }
        public string? SlowSourceId { get; set; }

        private bool _dodging;
        public bool Dodging { get => _dodging; set => this.RaiseAndSetIfChanged(ref _dodging, value); }

        private bool _surprised;
        public bool Surprised { get => _surprised; set => this.RaiseAndSetIfChanged(ref _surprised, value); }

        private bool _disengaged;
        public bool Disengaged { get => _disengaged; set => this.RaiseAndSetIfChanged(ref _disengaged, value); }

        private bool _hidden;
        public bool Hidden { get => _hidden; set => this.RaiseAndSetIfChanged(ref _hidden, value); }

        public string? HelpedTargetId { get; set; }

        public bool OffHandAbilityMod { get; set; }

        public int ContestBonus { get; set; }
        public int ContestDc
        {
            get
            {
                var rules = App.PM?.Rules;
                var fromSaves = rules == null
                    ? Saves.Values.DefaultIfEmpty(0).Max()
                    : rules.ContestAbilities.Select(SaveBonusFor).DefaultIfEmpty(0).Max();
                return (rules?.ContestDcBase ?? 8) + Math.Max(ContestBonus, fromSaves);
            }
        }

        public List<string> Resistances { get; } = new();
        public List<string> Senses { get; } = new();

        private string _pendingSaveAbility = "";
        public string PendingSaveAbility
        {
            get => _pendingSaveAbility;
            set { this.RaiseAndSetIfChanged(ref _pendingSaveAbility, value); this.RaisePropertyChanged(nameof(HasPendingSave)); this.RaisePropertyChanged(nameof(PendingSaveLabel)); }
        }

        private int _pendingSaveDc;
        public int PendingSaveDc
        {
            get => _pendingSaveDc;
            set { this.RaiseAndSetIfChanged(ref _pendingSaveDc, value); this.RaisePropertyChanged(nameof(PendingSaveLabel)); }
        }

        private int _pendingSaveDamage;
        public int PendingSaveDamage
        {
            get => _pendingSaveDamage;
            set => this.RaiseAndSetIfChanged(ref _pendingSaveDamage, value);
        }

        private string _pendingSaveDamageType = "";
        public string PendingSaveDamageType
        {
            get => _pendingSaveDamageType;
            set => this.RaiseAndSetIfChanged(ref _pendingSaveDamageType, value);
        }

        private bool _pendingSaveHalf;
        public bool PendingSaveHalf
        {
            get => _pendingSaveHalf;
            set => this.RaiseAndSetIfChanged(ref _pendingSaveHalf, value);
        }

        private string _pendingSaveSource = "";
        public string PendingSaveSource
        {
            get => _pendingSaveSource;
            set { this.RaiseAndSetIfChanged(ref _pendingSaveSource, value); this.RaisePropertyChanged(nameof(PendingSaveLabel)); }
        }

        public bool HasPendingSave => !string.IsNullOrEmpty(PendingSaveAbility);
        public string PendingSaveLabel => HasPendingSave
            ? (string.IsNullOrWhiteSpace(PendingSaveSource) ? "" : PendingSaveSource + ", ") + "roll " + PendingSaveAbility.ToUpperInvariant() + " save, DC " + PendingSaveDc
            : "";
        public List<string> Immunities { get; } = new();
        public List<string> Vulnerabilities { get; } = new();

        public ObservableCollection<string> Conditions { get; } = new();

        public List<TimedConditionDto> TimedConditions { get; } = new();

        public void AddCondition(string name, int rounds = 0, string expiresAt = "", string sourceId = "")
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            var rules = App.PM?.Rules;
            if (rounds <= 0 && rules != null && rules.Conditions.TryGetValue(name, out var rule)) rounds = rule.DurationRounds;
            if (string.IsNullOrWhiteSpace(expiresAt) && rules != null && rules.Conditions.TryGetValue(name, out var r2)) expiresAt = r2.ExpiresAt;

            if (!HasCondition(name)) Conditions.Add(name);
            TimedConditions.RemoveAll(t => string.Equals(t.Condition, name, StringComparison.OrdinalIgnoreCase));
            if (rounds > 0)
                TimedConditions.Add(new TimedConditionDto(name, rounds, string.IsNullOrWhiteSpace(expiresAt) ? "end" : expiresAt, sourceId ?? ""));
            RaiseConditionsChanged();
        }

        public void RemoveCondition(string name)
        {
            var hit = Conditions.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
            if (hit != null) Conditions.Remove(hit);
            TimedConditions.RemoveAll(t => string.Equals(t.Condition, name, StringComparison.OrdinalIgnoreCase));
            RaiseConditionsChanged();
        }

        // Saves tick off at the start of your turn and riders at the end, that is all the boundary is picking
        public List<string> TickTimedConditions(string boundary)
        {
            var expired = new List<string>();
            foreach (var t in TimedConditions.ToList())
            {
                if (!string.Equals(t.ExpiresAt, boundary, StringComparison.OrdinalIgnoreCase)) continue;
                var left = t.Rounds - 1;
                TimedConditions.Remove(t);
                if (left > 0) TimedConditions.Add(t with { Rounds = left });
                else
                {
                    expired.Add(t.Condition);
                    var hit = Conditions.FirstOrDefault(c => string.Equals(c, t.Condition, StringComparison.OrdinalIgnoreCase));
                    if (hit != null) Conditions.Remove(hit);
                }
            }
            if (expired.Count > 0) RaiseConditionsChanged();
            return expired;
        }

        public List<TimedBuffDto> TimedBuffs { get; } = new();

        public void AddTimedBuff(string target, int value, int rounds, string expiresAt, string label, string dice = "", string sourceId = "")
        {
            if (string.IsNullOrWhiteSpace(target) || (value == 0 && string.IsNullOrWhiteSpace(dice))) return;
            TimedBuffs.RemoveAll(b => string.Equals(b.Label, label, StringComparison.OrdinalIgnoreCase)
                                      && string.Equals(b.Target, target, StringComparison.OrdinalIgnoreCase));
            TimedBuffs.Add(new TimedBuffDto(target.ToLowerInvariant(), value, rounds, string.IsNullOrWhiteSpace(expiresAt) ? "end" : expiresAt, label ?? "", dice ?? "", sourceId ?? ""));
            RaiseBuffsChanged();
        }

        public List<string> DropBuffsFrom(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return new List<string>();
            var dropped = TimedBuffs.Where(b => b.SourceId == sourceId).Select(b => b.Label).ToList();
            if (dropped.Count == 0) return dropped;
            TimedBuffs.RemoveAll(b => b.SourceId == sourceId);
            RaiseBuffsChanged();
            return dropped;
        }

        public int BuffBonusFor(string target)
        {
            var total = 0;
            foreach (var b in TimedBuffs)
                if (string.Equals(b.Target, target, StringComparison.OrdinalIgnoreCase)) total += b.Value;
            return total;
        }

        // Rolls fresh every time it is asked, which is what Bless and Bane want.
        public int RollBuffBonusFor(string target, out string note)
        {
            var total = 0;
            var parts = new List<string>();
            foreach (var b in TimedBuffs)
            {
                if (!string.Equals(b.Target, target, StringComparison.OrdinalIgnoreCase)) continue;
                total += b.Value;
                if (b.Value != 0) parts.Add(b.Label + " " + (b.Value >= 0 ? "+" : "") + b.Value);
                if (string.IsNullOrWhiteSpace(b.Dice)) continue;
                var sign = b.Dice.TrimStart().StartsWith("-", StringComparison.Ordinal) ? -1 : 1;
                var expr = b.Dice.TrimStart().TrimStart('-', '+');
                if (!DiceManager.TryRoll(expr, false, out var rolled) || rolled == null) continue;
                total += sign * rolled.Total;
                parts.Add(b.Label + " " + (sign < 0 ? "-" : "+") + rolled.Total);
            }
            note = parts.Count == 0 ? "" : " (" + string.Join(", ", parts) + ")";
            return total;
        }

        public string BuffsLabel => TimedBuffs.Count == 0
            ? ""
            : string.Join(", ", TimedBuffs.Select(b => b.Label + " " + (string.IsNullOrWhiteSpace(b.Dice) ? (b.Value >= 0 ? "+" : "") + b.Value : b.Dice) + (b.Rounds > 0 ? " (" + b.Rounds + ")" : "")));

        public bool HasBuffs => TimedBuffs.Count > 0;

        private void RaiseBuffsChanged()
        {
            this.RaisePropertyChanged(nameof(BuffsLabel));
            this.RaisePropertyChanged(nameof(HasBuffs));
            this.RaisePropertyChanged(nameof(ArmorClass));
        }

        public List<string> TickTimedBuffs(string boundary)
        {
            var expired = new List<string>();
            foreach (var b in TimedBuffs.ToList())
            {
                if (b.Rounds <= 0 || !string.Equals(b.ExpiresAt, boundary, StringComparison.OrdinalIgnoreCase)) continue;
                var left = b.Rounds - 1;
                TimedBuffs.Remove(b);
                if (left > 0) TimedBuffs.Add(b with { Rounds = left });
                else expired.Add(b.Label);
            }
            if (expired.Count > 0) RaiseBuffsChanged();
            return expired;
        }

        public int RoundsLeftOn(string condition) =>
            TimedConditions.FirstOrDefault(t => string.Equals(t.Condition, condition, StringComparison.OrdinalIgnoreCase))?.Rounds ?? 0;
        public bool HasConditions => Conditions.Count > 0;
        public string ConditionsLabel => Conditions.Count == 0 ? "No conditions" : string.Join(", ", Conditions);
        public bool HasCondition(string name) => Conditions.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        public void RaiseConditionsChanged()
        {
            this.RaisePropertyChanged(nameof(HasConditions));
            this.RaisePropertyChanged(nameof(ConditionsLabel));
            this.RaisePropertyChanged(nameof(IsIncapacitated));
            this.RaisePropertyChanged(nameof(CanSpendAction));
            this.RaisePropertyChanged(nameof(CanSpendBonusAction));
            this.RaisePropertyChanged(nameof(CanSpendReaction));
            this.RaisePropertyChanged(nameof(EconomyLabel));
        }

        public Dictionary<string, int> Saves { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int SaveBonusFor(string ability) =>
            !string.IsNullOrWhiteSpace(ability) && Saves.TryGetValue(ability.Trim(), out var v) ? v : 0;

        public void SetSave(string ability, int bonus)
        {
            if (string.IsNullOrWhiteSpace(ability)) return;
            Saves[ability.Trim()] = bonus;
        }

        public List<ConcentrationLink> ConcentrationEffects { get; } = new();
        public bool HasConcentrationEffects => ConcentrationEffects.Count > 0;
        public string ConcentrationEffectsLabel => ConcentrationEffects.Count == 0 ? "" : "Holding: " + string.Join(", ", ConcentrationEffects.Select(l => l.Condition));
        public void RaiseConcentrationEffectsChanged()
        {
            this.RaisePropertyChanged(nameof(HasConcentrationEffects));
            this.RaisePropertyChanged(nameof(ConcentrationEffectsLabel));
        }

        private bool _manualAdvantage;
        public bool ManualAdvantage
        {
            get => _manualAdvantage;
            set { this.RaiseAndSetIfChanged(ref _manualAdvantage, value); if (value) ManualDisadvantage = false; }
        }
        private bool _manualDisadvantage;
        public bool ManualDisadvantage
        {
            get => _manualDisadvantage;
            set { this.RaiseAndSetIfChanged(ref _manualDisadvantage, value); if (value) ManualAdvantage = false; }
        }

        private bool _readied;
        public bool Readied
        {
            get => _readied;
            set => this.RaiseAndSetIfChanged(ref _readied, value);
        }

        public List<string> EffectiveResistances()
        {
            var all = Resistances.ToList();
            var fromConditions = App.PM?.Rules?.ResistancesFromConditions(Conditions) ?? new List<string>();
            foreach (var r in fromConditions)
                if (!all.Contains(r, StringComparer.OrdinalIgnoreCase)) all.Add(r);
            return all;
        }

        public int ScaleDamageByType(int amount, string? damageType)
        {
            if (amount <= 0) return 0;
            if (string.IsNullOrWhiteSpace(damageType)) return amount;
            var rules = App.PM?.Rules ?? new GameRules();

            foreach (var i in Immunities)
                if (GameRules.TryTypedAmount(i, damageType!, out _)) return 0;

            foreach (var v in Vulnerabilities)
                if (GameRules.TryTypedAmount(v, damageType!, out var extra))
                    return extra > 0 ? amount + extra : amount * Math.Max(1, rules.VulnerabilityMultiplier);

            foreach (var r in EffectiveResistances())
                if (GameRules.TryTypedAmount(r, damageType!, out var flat))
                    return flat > 0
                        ? Math.Max(0, amount - flat)
                        : amount / Math.Max(1, rules.ResistanceDivisor);

            return amount;
        }

        // Eats temp hp first then real hp, the number coming back is the real loss so concentration keys off that and not the absorbed part.
        public int TakeDamage(int amount)
        {
            if (amount <= 0) return 0;
            var fromTemp = Math.Min(TempHp, amount);
            if (fromTemp > 0) TempHp -= fromTemp;
            var toHp = amount - fromTemp;
            if (toHp <= 0) return 0;

            var spill = toHp - CurrentHp;
            CurrentHp = Math.Max(0, CurrentHp - toHp);

            var rules = App.PM?.Rules;
            var threshold = (int)Math.Ceiling(EffectiveMaxHp * (rules?.MassiveDamageMaxHpMultiple ?? 1.0));
            if ((rules?.MassiveDamageKills ?? true) && CurrentHp == 0 && threshold > 0 && spill >= threshold)
            {
                KilledOutright = true;
                DeathSaveFailures = rules?.DeathSaveFailuresToDie ?? 3;
            }
            return toHp;
        }

        private bool _hasInspiration;
        public bool HasInspiration
        {
            get => _hasInspiration;
            set => this.RaiseAndSetIfChanged(ref _hasInspiration, value);
        }

        public bool SpendInspiration()
        {
            if (!_hasInspiration || !(App.PM?.Rules?.InspirationGrantsAdvantage ?? true)) return false;
            HasInspiration = false;
            return true;
        }

        private bool _killedOutright;
        public bool KilledOutright
        {
            get => _killedOutright;
            set => this.RaiseAndSetIfChanged(ref _killedOutright, value);
        }

        public string SerializeExtras() => JsonSerializer.Serialize(new CombatExtrasDto(
            TempHp, SpeedFeet, Resistances.ToList(), Immunities.ToList(), Vulnerabilities.ToList(), Conditions.ToList(),
            LegacySaveList(),
            BaseMaxReactions, MaxReactions, ReactionsRemaining,
            ConcentrationEffects.ToList(), Dodging, Disengaged, Hidden,
            new Dictionary<string, int>(Saves, StringComparer.OrdinalIgnoreCase), AttacksPerAction,
            LegendaryPerRound, LegendaryRemaining, LegendaryActions.ToList(),
            LairInitiative, LairUsedInRound, LairActions.ToList(), OwnerCharacterId,
            ArmorClass, DexMod, ConSaveBonus, CharacterLevel,
            Dashed, Readied, SurgeActionGrant, SurgeBonusGrant, SurgeUsesMax, SurgeUsesSpent, AttacksThisAction, Riders.ToList(), OffHandAbilityMod, InspirationDie, ConcentrationSummon,
            new Dictionary<string, string>(ActionCostOverrides, StringComparer.OrdinalIgnoreCase), DashPaid,
            SlowPenaltyFeet, SlowSourceId ?? "", RidersUsedThisTurn.ToList(), TimedConditions.ToList(), TimedBuffs.ToList(), Conditional.ToList(), GrantedReactions.ToList(), ExhaustionLevel, HasInspiration, KilledOutright,
            CoverBonus, ManualAdvantage, ManualDisadvantage,
            AdvantageOn.ToList(), ContestBonus, BaseMaxActions, BaseMaxBonusActions,
            ReadiedIntent ?? "", ReadiedTrigger ?? "", Sapped, VexTargetId ?? "", HelpedTargetId ?? "", BonusSwings, CleavedThisTurn, NickedThisTurn, Surprised, Senses.ToList(), PendingSaveAbility ?? "", PendingSaveDc, PendingSaveDamage, PendingSaveDamageType ?? "", PendingSaveHalf, PendingSaveSource ?? ""));

        private static readonly string[] _legacySaveOrder = { "str", "dex", "con", "int", "wis", "cha" };

        private List<int> LegacySaveList() => _legacySaveOrder.Select(SaveBonusFor).ToList();

        public void ApplyExtras(string? json)
        {
            Resistances.Clear();
            Senses.Clear();
            Immunities.Clear();
            Vulnerabilities.Clear();
            Conditions.Clear();
            ConcentrationEffects.Clear();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var dto = JsonSerializer.Deserialize<CombatExtrasDto>(json);
                    if (dto != null)
                    {
                        TempHp = dto.TempHp;
                        if (dto.SpeedFeet > 0) SpeedFeet = dto.SpeedFeet;
                        if (dto.Resist != null) Resistances.AddRange(dto.Resist);
                        if (dto.Senses != null) Senses.AddRange(dto.Senses);
                        PendingSaveAbility = dto.PendingSaveAbility ?? "";
                        PendingSaveDc = dto.PendingSaveDc;
                        PendingSaveDamage = dto.PendingSaveDamage;
                        PendingSaveDamageType = dto.PendingSaveDamageType ?? "";
                        PendingSaveHalf = dto.PendingSaveHalf;
                        PendingSaveSource = dto.PendingSaveSource ?? "";
                        if (dto.Immune != null) Immunities.AddRange(dto.Immune);
                        if (dto.Vulnerable != null) Vulnerabilities.AddRange(dto.Vulnerable);
                        if (dto.Conditions != null) foreach (var c in dto.Conditions) Conditions.Add(c);
                        Saves.Clear();
                        if (dto.AbilitySaves != null && dto.AbilitySaves.Count > 0)
                            foreach (var kv in dto.AbilitySaves) SetSave(kv.Key, kv.Value);
                        else if (dto.Saves != null && dto.Saves.Count >= _legacySaveOrder.Length)
                            for (int i = 0; i < _legacySaveOrder.Length; i++) SetSave(_legacySaveOrder[i], dto.Saves[i]);
                        BaseMaxReactions = dto.BaseMaxReactions;
                        MaxReactions = dto.MaxReactions;
                        ReactionsRemaining = dto.ReactionsRemaining;
                        if (dto.ConcEffects != null) ConcentrationEffects.AddRange(dto.ConcEffects);
                        Dodging = dto.Dodging;
                        Disengaged = dto.Disengaged;
                        Hidden = dto.Hidden;
                        AttacksPerAction = dto.AttacksPerAction;
                        LegendaryActions.Clear();
                        if (dto.Legendary != null) foreach (var la in dto.Legendary) LegendaryActions.Add(la);
                        LegendaryPerRound = dto.LegendaryPerRound;
                        LegendaryRemaining = dto.LegendaryRemaining;
                        LairActions.Clear();
                        if (dto.Lair != null) foreach (var la in dto.Lair) LairActions.Add(la);
                        LairInitiative = dto.LairInitiative;
                        LairUsedInRound = dto.LairUsedInRound;
                        OwnerCharacterId = dto.OwnerCharacterId ?? "";
                        // A blob without these fields carries zero, so only take a real value.
                        if (dto.ArmorClass > 0) ArmorClass = dto.ArmorClass;
                        if (dto.DexMod != 0) DexMod = dto.DexMod;
                        if (dto.ConSaveBonus != 0) ConSaveBonus = dto.ConSaveBonus;
                        if (dto.CharacterLevel > 0) CharacterLevel = dto.CharacterLevel;
                        Dashed = dto.Dashed;
                        Readied = dto.Readied;
                        SurgeActionGrant = dto.SurgeActionGrant;
                        SurgeBonusGrant = dto.SurgeBonusGrant;
                        SurgeUsesMax = dto.SurgeUsesMax;
                        SurgeUsesSpent = dto.SurgeUsesSpent;
                        AttacksThisAction = dto.AttacksThisAction;
                        Riders.Clear();
                        if (dto.Riders != null) Riders.AddRange(dto.Riders);
                        OffHandAbilityMod = dto.OffHandAbilityMod;
                        InspirationDie = dto.InspirationDie ?? "";
                        ConcentrationSummon = dto.ConcentrationSummon;
                        DashPaid = dto.DashPaid;
                        SlowPenaltyFeet = dto.SlowPenaltyFeet;
                        SlowSourceId = string.IsNullOrEmpty(dto.SlowSourceId) ? null : dto.SlowSourceId;
                        CoverBonus = dto.CoverBonus;
                        ManualAdvantage = dto.ManualAdvantage;
                        ManualDisadvantage = dto.ManualDisadvantage;
                        AdvantageOn.Clear();
                        if (dto.AdvantageOn != null) foreach (var a in dto.AdvantageOn) AdvantageOn.Add(a);
                        ContestBonus = dto.ContestBonus;
                        BaseMaxActions = dto.BaseMaxActions;
                        BaseMaxBonusActions = dto.BaseMaxBonusActions;
                        ReadiedIntent = dto.ReadiedIntent ?? "";
                        ReadiedTrigger = dto.ReadiedTrigger ?? "";
                        Sapped = dto.Sapped;
                        VexTargetId = string.IsNullOrEmpty(dto.VexTargetId) ? null : dto.VexTargetId;
                        HelpedTargetId = string.IsNullOrEmpty(dto.HelpedTargetId) ? null : dto.HelpedTargetId;
                        BonusSwings = dto.BonusSwings;
                        CleavedThisTurn = dto.CleavedThisTurn;
                        Surprised = dto.Surprised;
                        NickedThisTurn = dto.NickedThisTurn;
                        RidersUsedThisTurn.Clear();
                        if (dto.RidersUsed != null) foreach (var r in dto.RidersUsed) RidersUsedThisTurn.Add(r);
                        TimedConditions.Clear();
                        if (dto.TimedConditions != null) TimedConditions.AddRange(dto.TimedConditions);
                        TimedBuffs.Clear();
                        if (dto.TimedBuffs != null) TimedBuffs.AddRange(dto.TimedBuffs);
                        Conditional.Clear();
                        if (dto.Conditional != null) Conditional.AddRange(dto.Conditional);
                        GrantedReactions.Clear();
                        if (dto.GrantedReactions != null) GrantedReactions.AddRange(dto.GrantedReactions);
                        ExhaustionLevel = dto.ExhaustionLevel;
                        HasInspiration = dto.HasInspiration;
                        KilledOutright = dto.KilledOutright;
                        ActionCostOverrides.Clear();
                        if (dto.ActionCostOverrides != null)
                            foreach (var kv in dto.ActionCostOverrides) ActionCostOverrides[kv.Key] = kv.Value;
                    }
                }
                catch (Exception ex)
                {
                    ErrorLog.Log("Could not read the extras for " + Name + ", its conditions and saves may be half applied", ex);
                }
            }
            this.RaisePropertyChanged(nameof(HasDamageMods));
            this.RaisePropertyChanged(nameof(DamageModsLabel));
            RaiseConditionsChanged();
        }

        public bool HasDamageMods => Resistances.Count > 0 || Immunities.Count > 0 || Vulnerabilities.Count > 0;
        public string DamageModsLabel
        {
            get
            {
                var parts = new List<string>();
                if (Resistances.Count > 0) parts.Add("Resist " + string.Join(", ", Resistances));
                if (Immunities.Count > 0) parts.Add("Immune " + string.Join(", ", Immunities));
                if (Vulnerabilities.Count > 0) parts.Add("Vuln " + string.Join(", ", Vulnerabilities));
                return string.Join("   ", parts);
            }
        }

        public bool RevealExactHpToPlayers { get; set; }

        public string HpLabelForPlayers
        {
            get
            {
                var rules = App.PM?.Rules ?? new GameRules();
                return rules.HpLabelFor(CurrentHp, MaxHp);
            }
        }

        public CombatantViewModel(string id, string name, bool isPlayerCharacter)
        {
            Id = id;
            _name = name;
            IsPlayerCharacter = isPlayerCharacter;
            Attacks.CollectionChanged += (_, __) => this.RaisePropertyChanged(nameof(HasAttacks));
        }
    }
    public class InitiativeTrackerViewModel : ViewModelBase
    {
        public ObservableCollection<CombatantViewModel> Combatants { get; } = new();
        public event Action<CombatantViewModel?>? ActiveCombatantChanged;

        public event Action? StateChanged;
        private bool _applyingRemote;
        public bool IsApplyingRemote => _applyingRemote;

        public void NotifyStateChanged()
        {
            if (_applyingRemote) return;
            StateChanged?.Invoke();
        }

        public event Action<CombatantViewModel>? ConcentrationEnded;
        public event Action? CombatEnded;

        // Every condition this caster was sustaining comes off its targets and what fell gets handed back, for when concentration drops or they go down
        public List<string> EndConcentrationEffects(CombatantViewModel? caster)
        {
            var removed = new List<string>();
            if (caster == null) return removed;
            foreach (var link in caster.ConcentrationEffects.ToList())
            {
                var target = Combatants.FirstOrDefault(c => c.Id == link.TargetId);
                if (target == null) continue;
                var cond = target.Conditions.FirstOrDefault(x => string.Equals(x, link.Condition, StringComparison.OrdinalIgnoreCase));
                if (cond != null)
                {
                    target.Conditions.Remove(cond);
                    target.RaiseConditionsChanged();
                    removed.Add(link.Condition + " on " + target.Name);
                }
            }
            foreach (var c in Combatants)
                foreach (var label in c.DropBuffsFrom(caster.Id))
                    removed.Add(label + " on " + c.Name);

            caster.ConcentrationEffects.Clear();
            caster.RaiseConcentrationEffectsChanged();
            ConcentrationEnded?.Invoke(caster);
            return removed;
        }

        private int _round;
        public int Round
        {
            get => _round;
            set => this.RaiseAndSetIfChanged(ref _round, value);
        }

        private CombatantViewModel? _activeCombatant;
        public CombatantViewModel? ActiveCombatant
        {
            get => _activeCombatant;
            set => this.RaiseAndSetIfChanged(ref _activeCombatant, value);
        }

        private bool _combatActive;
        public bool CombatActive
        {
            get => _combatActive;
            set => this.RaiseAndSetIfChanged(ref _combatActive, value);
        }

        private bool _isDm;
        public bool IsDm
        {
            get => _isDm;
            set => this.RaiseAndSetIfChanged(ref _isDm, value);
        }

        private CombatantViewModel? _selectedCombatant;
        public CombatantViewModel? SelectedCombatant
        {
            get => _selectedCombatant;
            set => this.RaiseAndSetIfChanged(ref _selectedCombatant, value);
        }

        public ReactiveCommand<CombatantViewModel, Unit> SelectCombatantCommand { get; }

        public ReactiveCommand<Unit, Unit> StartCombatCommand { get; }
        public ReactiveCommand<Unit, Unit> EndCombatCommand { get; }
        public ReactiveCommand<Unit, Unit> NextTurnCommand { get; }
        public ReactiveCommand<Unit, Unit> PrevTurnCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearCombatCommand { get; }
        public ReactiveCommand<Unit, Unit> RollAllInitiativeCommand { get; }

        private int _turnSeconds;
        public int TurnSeconds { get => _turnSeconds; private set { this.RaiseAndSetIfChanged(ref _turnSeconds, value); this.RaisePropertyChanged(nameof(TurnTimerLabel)); } }
        public string TurnTimerLabel => (_turnSeconds / 60).ToString("00") + ":" + (_turnSeconds % 60).ToString("00");
        private readonly DispatcherTimer _turnTimer;

        public InitiativeTrackerViewModel()
        {
            _turnTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _turnTimer.Tick += (_, _) => { if (CombatActive) TurnSeconds++; };
            _turnTimer.Start();
            StartCombatCommand = ReactiveCommand.Create(StartCombat);
            EndCombatCommand = ReactiveCommand.Create(EndCombat);
            NextTurnCommand = ReactiveCommand.Create(NextTurn);
            PrevTurnCommand = ReactiveCommand.Create(PrevTurn);
            ClearCombatCommand = ReactiveCommand.Create(ClearInitiative);
            RollAllInitiativeCommand = ReactiveCommand.Create(RollAllInitiative);

            SelectCombatantCommand = ReactiveCommand.Create<CombatantViewModel>(c =>
            {
                SelectedCombatant = c;
            });

            this.WhenAnyValue(t => t.SelectedCombatant)
                .Subscribe(selected =>
                {
                    foreach (var c in Combatants)
                        c.IsSelected = ReferenceEquals(c, selected);
                });

            this.WhenAnyValue(t => t.ActiveCombatant)
                .Subscribe(active =>
                {
                    foreach (var c in Combatants)
                        c.IsActive = ReferenceEquals(c, active);

                    ActiveCombatantChanged?.Invoke(active);
    });
        }

        public int TurnCounter { get; private set; }

        private CombatantViewModel? _endingTurn;
        public event Action<CombatantViewModel, string>? ConditionExpired;

        private void BeginTurn(CombatantViewModel? c)
        {
            TurnSeconds = 0;
            TurnCounter++;
            _endingTurn?.TickTimedConditions("end");
            _endingTurn?.TickTimedBuffs("end");
            _endingTurn = c;
            c?.ResetTurnEconomy();
            if (c == null) return;
            foreach (var gone in c.TickTimedConditions("start"))
                ConditionExpired?.Invoke(c, gone);
            c.TickTimedBuffs("start");
            c.Dodging = false;
            foreach (var other in Combatants)
            {
                other.Disengaged = false;
                if (other.SlowSourceId != c.Id || other.SlowPenaltyFeet <= 0) continue;
                other.SpeedFeet += other.SlowPenaltyFeet;
                other.SlowPenaltyFeet = 0;
                other.SlowSourceId = null;
            }
        }

        public Func<string, string, double, double>? PushCombatant { get; set; }

        public void ApplyCharacterCondition(string characterId, string condition)
        {
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(condition)) return;
            var c = Combatants.FirstOrDefault(x => x.IsPlayerCharacter && x.Id == characterId);
            if (c == null || c.HasCondition(condition)) return;
            c.Conditions.Add(condition);
            c.RaiseConditionsChanged();
            NotifyStateChanged();
        }

        public void ApplyCharacterInspiration(string characterId, string die)
        {
            if (string.IsNullOrWhiteSpace(characterId) || string.IsNullOrWhiteSpace(die)) return;
            var c = Combatants.FirstOrDefault(x => x.IsPlayerCharacter && x.Id == characterId);
            if (c == null) return;
            c.InspirationDie = die;
            NotifyStateChanged();
        }

        public string ResolveTacticalAction(CombatantViewModel? actor, string kind, CombatantViewModel? target, CombatantViewModel? ally)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            if (actor == null || !rules.TacticalActions.TryGetValue(kind, out var rule)) return "";
            var cost = actor.CostForAction(rule.Id, rule.Cost);
            if (!CanAfford(actor, cost)) return actor.Name + " has no " + GameRules.CostPool(cost) + " left for " + rule.Name + ".";

            switch (rule.Effect)
            {
                case "dodge":
                    Pay(actor, cost);
                    actor.Dodging = true;
                    NotifyStateChanged();
                    return actor.Name + " takes the " + rule.Name + ", attacks against them have disadvantage until their next turn.";

                case "disengage":
                    Pay(actor, cost);
                    actor.Disengaged = true;
                    NotifyStateChanged();
                    return actor.Name + " takes the " + rule.Name + ", their movement does not provoke this turn.";

                case "hide":
                    Pay(actor, cost);
                    actor.Hidden = true;
                    NotifyStateChanged();
                    return actor.Name + " takes the " + rule.Name + ", their next attack has advantage. Nothing rolled stealth for them, that is on the dm.";

                case "help":
                    if (ally == null || target == null || ReferenceEquals(ally, actor)) return "";
                    Pay(actor, cost);
                    ally.HelpedTargetId = target.Id;
                    NotifyStateChanged();
                    return actor.Name + " helps " + ally.Name + " against " + target.Name + ", advantage on their next attack.";

                case "check":
                {
                    Pay(actor, cost);
                    var mode = rules.AbilityCheckModeFrom(actor.Conditions);
                    var advantage = mode == "advantage";
                    var disadvantage = mode == "disadvantage" || (rules.Exhaustion?.AbilityChecksAtDisadvantage(actor.ExhaustionLevel) ?? false);
                    var checkRoll = DiceManager.RollCore(rules.AttackDie, advantage, disadvantage);
                    var checkBonus = string.IsNullOrWhiteSpace(rule.CheckAbility) ? 0 : SaveBonusFor(actor, rule.CheckAbility);
                    checkBonus -= rules.Exhaustion?.D20Penalty(actor.ExhaustionLevel) ?? 0;
                    var checkTotal = checkRoll + checkBonus;
                    var what = string.IsNullOrWhiteSpace(rule.CheckSkill) ? rule.Name : rule.CheckSkill;
                    var swingTag = advantage == disadvantage ? "" : advantage ? " (advantage)" : " (disadvantage)";
                    NotifyStateChanged();
                    var bonusText = checkBonus >= 0 ? "+" + checkBonus : checkBonus.ToString();
                    var head = actor.Name + " takes the " + rule.Name + ", " + what + " check" + swingTag + ": [" + checkRoll + "]" + bonusText + " = " + checkTotal;
                    var checkDc = rules.CheckDcFor(rule.CheckDifficulty, rule.CheckDc);
                    if (checkDc <= 0) return head + ". The dm sets the dc.";
                    var band = rules.CheckDifficultyName(rule.CheckDifficulty);
                    var against = " vs DC " + checkDc + (string.IsNullOrWhiteSpace(band) ? "" : " (" + band + ")");
                    return head + against + ", " + (rules.SaveSucceeds(checkTotal, checkDc) ? "pass" : "fail") + ".";
                }

                case "dispel":
                {
                    if (target == null) return "";
                    if (!target.Concentration) return target.Name + " is not holding anything for " + rule.Name + " to take down.";
                    var dispelDc = rules.CheckDcFor(rule.CheckDifficulty, rule.CheckDc);
                    Pay(actor, cost);
                    var mode = rules.AbilityCheckModeFrom(actor.Conditions);
                    var advantage = mode == "advantage";
                    var disadvantage = mode == "disadvantage" || (rules.Exhaustion?.AbilityChecksAtDisadvantage(actor.ExhaustionLevel) ?? false);
                    var roll = DiceManager.RollCore(rules.AttackDie, advantage, disadvantage);
                    var bonus = string.IsNullOrWhiteSpace(rule.CheckAbility) ? 0 : SaveBonusFor(actor, rule.CheckAbility);
                    bonus -= rules.Exhaustion?.D20Penalty(actor.ExhaustionLevel) ?? 0;
                    var total = roll + bonus;

                    if (dispelDc > 0 && !rules.SaveSucceeds(total, dispelDc))
                    {
                        NotifyStateChanged();
                        return actor.Name + " tries " + rule.Name + " on " + target.Name + " and fails, " + total + " vs DC " + dispelDc + ".";
                    }

                    var lifted = EndConcentrationEffects(target);
                    target.Concentration = false;
                    NotifyStateChanged();
                    var scoreline = dispelDc > 0 ? ", " + total + " vs DC " + dispelDc : ", the dm called it";
                    return actor.Name + " lands " + rule.Name + " on " + target.Name + scoreline
                           + (lifted.Count == 0 ? ", nothing was riding on it." : ", down comes " + string.Join(", ", lifted) + ".");
                }

                case "contest":
                {
                    if (target == null || (string.IsNullOrWhiteSpace(rule.Condition) && rule.PushFeet <= 0)) return "";
                    if (!string.IsNullOrWhiteSpace(rule.Condition) && target.HasCondition(rule.Condition)) return "";
                    Pay(actor, cost);
                    var dc = actor.ContestDc;
                    var bonus = rule.DefenderSaves.Select(ab => SaveBonusFor(target, ab)).DefaultIfEmpty(0).Max();
                    var roll = DiceManager.RollCore(rules.AttackDie);
                    var total = roll + bonus;
                    if (rules.SaveSucceeds(total, dc))
                    {
                        NotifyStateChanged();
                        return target.Name + " resists the " + rule.Name + ", " + total + " vs DC " + dc + ".";
                    }
                    var line = target.Name + " fails the " + rule.Name + ", " + total + " vs DC " + dc;
                    if (!string.IsNullOrWhiteSpace(rule.Condition))
                    {
                        target.Conditions.Add(rule.Condition);
                        target.RaiseConditionsChanged();
                        line += ", and is " + rule.Condition;
                    }
                    if (rule.PushFeet > 0)
                    {
                        var moved = PushCombatant?.Invoke(actor.Id, target.Id, rule.PushFeet);
                        line += moved == null
                            ? ", the push had no map to land on"
                            : ", shoved " + (int)Math.Round(moved.Value) + " ft back";
                    }
                    NotifyStateChanged();
                    return line + ".";
                }
            }

            return "";
        }

        public static bool CanAfford(CombatantViewModel c, string cost)
        {
            if (c.IsIncapacitated) return false;
            var amount = GameRules.CostAmount(cost);
            return GameRules.CostPool(cost) switch
            {
                "bonus" => c.BonusActionsRemaining >= amount,
                "reaction" => c.ReactionsRemaining >= amount,
                "none" => true,
                _ => c.ActionsRemaining >= amount
            };
        }

        // The first swing of a turn buys the Attack action, the rest of the multiattack ride on it for free.
        public static bool TrySpendAttack(CombatantViewModel c, string cost)
        {
            if (c.HasSwingLeftInThisAction)
            {
                c.AttacksThisAction++;
                return true;
            }
            if (!CanAfford(c, cost)) return false;
            Pay(c, cost);
            c.AttacksThisAction = 1;
            return true;
        }

        public static void Pay(CombatantViewModel c, string cost)
        {
            var amount = GameRules.CostAmount(cost);
            switch (GameRules.CostPool(cost))
            {
                case "bonus": for (var i = 0; i < amount; i++) c.SpendBonusAction(); break;
                case "reaction": for (var i = 0; i < amount; i++) c.SpendReaction(); break;
                case "none": break;
                default: for (var i = 0; i < amount; i++) c.SpendAction(); break;
            }
        }

        public static int SaveBonusFor(CombatantViewModel c, string ability) => c.SaveBonusFor(ability);

        public void RollAllInitiative()
        {
            if (Combatants.Count == 0) return;
            var die = App.PM?.Rules?.InitiativeDie ?? 20;
            foreach (var c in Combatants) c.Initiative = DiceManager.RollInitiativeDie(c.Surprised) + c.DexMod;
            var tieBreak = App.PM?.Rules?.InitiativeTieBreakByModifier ?? true;
            var sorted = (tieBreak
                ? Combatants.OrderByDescending(c => c.Initiative).ThenByDescending(c => c.DexMod)
                : Combatants.OrderByDescending(c => c.Initiative)).ToList();
            Combatants.Clear();
            foreach (var c in sorted) Combatants.Add(c);
            if (CombatActive) ActiveCombatant = Combatants[0];
            NotifyStateChanged();
        }

        public void AddInInitiativeOrder(CombatantViewModel combatant)
        {
            if (!CombatActive || Combatants.Count == 0) { Combatants.Add(combatant); return; }
            var tieBreak = App.PM?.Rules?.InitiativeTieBreakByModifier ?? true;
            var idx = 0;
            while (idx < Combatants.Count)
            {
                var e = Combatants[idx];
                var goesBefore = e.Initiative > combatant.Initiative
                    || (e.Initiative == combatant.Initiative && (!tieBreak || e.DexMod >= combatant.DexMod));
                if (!goesBefore) break;
                idx++;
            }
            Combatants.Insert(idx, combatant);
        }

        private void StartCombat()
        {
            if (Combatants.Count == 0) return;
            var tieBreak = App.PM?.Rules?.InitiativeTieBreakByModifier ?? true;
            var sorted = (tieBreak
                ? Combatants.OrderByDescending(c => c.Initiative).ThenByDescending(c => c.DexMod)
                : Combatants.OrderByDescending(c => c.Initiative)).ToList();
            Combatants.Clear();
            foreach (var c in sorted) Combatants.Add(c);

            CombatActive = true;
            Round = 1;
            AdvanceTo(Combatants[0]);
            NotifyStateChanged();
        }

        private void EndCombat()
        {
            CombatActive = false;
            CombatEnded?.Invoke();
            Round = 0;
            ActiveCombatant = null;
            foreach (var c in Combatants)
            {
                c.ResetTurnEconomy();
                c.Sapped = false;
                c.VexTargetId = null;
                c.Hidden = false;
                c.Dodging = false;
                c.Disengaged = false;
                c.HelpedTargetId = null;
                if (c.SlowPenaltyFeet > 0)
                {
                    c.SpeedFeet += c.SlowPenaltyFeet;
                    c.SlowPenaltyFeet = 0;
                    c.SlowSourceId = null;
                }
            }
            NotifyStateChanged();
        }

        public event Action? InitiativeCleared;

        public void ClearInitiative()
        {
            Combatants.Clear();
            CombatActive = false;
            Round = 0;
            ActiveCombatant = null;
            SelectedCombatant = null;
            NotifyStateChanged();
            InitiativeCleared?.Invoke();
        }

        public void NextTurn()
        {
            if (!CombatActive || ActiveCombatant == null || Combatants.Count == 0) return;
            var idx = Combatants.IndexOf(ActiveCombatant);
            idx = (idx + 1) % Combatants.Count;
            if (idx == 0) Round++;
            AdvanceTo(Combatants[idx]);
            NotifyStateChanged();
        }

        private void PrevTurn()
        {
            if (!CombatActive || ActiveCombatant == null || Combatants.Count == 0) return;
            var idx = Combatants.IndexOf(ActiveCombatant);
            idx = (idx - 1 + Combatants.Count) % Combatants.Count;
            if (idx == Combatants.Count - 1 && Round > 1) Round--;
            AdvanceTo(Combatants[idx]);
            NotifyStateChanged();
        }

        private void AdvanceTo(CombatantViewModel next)
        {
            var repeat = ReferenceEquals(ActiveCombatant, next);
            ActiveCombatant = next;
            BeginTurn(next);
            if (repeat) ActiveCombatantChanged?.Invoke(next);
        }

        // Drop the active combatant to the bottom of the order so they act later this round, whoever was next takes over now. Their economy resets when their delayed slot comes up.
        public void DelayTurn()
        {
            if (!CombatActive || ActiveCombatant == null || Combatants.Count < 2) return;
            var delayed = ActiveCombatant;
            var idx = Combatants.IndexOf(delayed);
            var wrapsToTheTop = idx == Combatants.Count - 1;
            var next = Combatants[(idx + 1) % Combatants.Count];
            Combatants.Move(idx, Combatants.Count - 1);
            if (wrapsToTheTop) Round++;
            AdvanceTo(next);
            NotifyStateChanged();
        }

        public CombatStateMessage BuildSnapshot(string encounterId, string mapId)
        {
            var combatants = Combatants
                .Select(c => new CombatantSnapshot(
                    c.Id, c.Name, c.Initiative, c.CurrentHp, c.MaxHp,
                    c.IsPlayerCharacter, c.RevealExactHpToPlayers, c.TokenId,
                    c.MaxActions, c.ActionsRemaining, c.MaxBonusActions, c.BonusActionsRemaining, c.SerializeSlots(), c.Concentration,
                    c.DeathSaveSuccesses, c.DeathSaveFailures, c.SerializeAttacks(), c.IsFriendly, c.SerializeExtras()))
                .ToList();

            return new CombatStateMessage(
                encounterId, mapId, CombatActive, Round, ActiveCombatant?.Id, combatants);
        }

        // Player side of the sync, whatever the DM snapshot says wins so just rebuild the list. Update-in-place would keep selection nicer but combat lists are small enough that nobody will notice
        public void ApplySnapshot(CombatStateMessage state)
        {
            _applyingRemote = true;
            try
            {
                Combatants.Clear();
                foreach (var c in state.Combatants)
                {
                    var cb = new CombatantViewModel(c.Id, c.Name, c.IsPlayerCharacter)
                    {
                        Initiative = c.Initiative,
                        CurrentHp = c.CurrentHp,
                        MaxHp = c.MaxHp,
                        RevealExactHpToPlayers = c.RevealExactHp,
                        TokenId = c.TokenId,
                        BaseMaxActions = c.MaxActions,
                        BaseMaxBonusActions = c.MaxBonusActions,
                        MaxActions = c.MaxActions,
                        ActionsRemaining = c.ActionsRemaining,
                        MaxBonusActions = c.MaxBonusActions,
                        BonusActionsRemaining = c.BonusActionsRemaining,
                        Concentration = c.Concentration,
                        DeathSaveSuccesses = c.DeathSaveSuccesses,
                        DeathSaveFailures = c.DeathSaveFailures,
                        IsFriendly = c.IsFriendly
                    };
                    cb.ApplySlots(c.SpellSlots);
                    cb.ApplyAttacks(c.AttacksJson);
                    cb.ApplyExtras(c.ExtrasJson);
                    Combatants.Add(cb);
                }

                Round = state.Round;
                CombatActive = state.CombatActive;
                ActiveCombatant = state.ActiveCombatantId == null
                    ? null
                    : Combatants.FirstOrDefault(c => c.Id == state.ActiveCombatantId);
            }
            finally
            {
                _applyingRemote = false;
            }
            SnapshotApplied?.Invoke();
        }

        public event Action? SnapshotApplied;
    }

    public record QuickRollOption(string Label, string Ability);

    public class CharacterQuickPanelViewModel : ViewModelBase
    {
        public CharacterRuntime Character { get; }

        public string Name => Character.Name;

        private int _currentHp;
        public int CurrentHp
        {
            get => _currentHp;
            set { this.RaiseAndSetIfChanged(ref _currentHp, value); Character.CurrentHp = value; }
        }

        private int _maxHp;
        public int MaxHp
        {
            get => _maxHp;
            set { this.RaiseAndSetIfChanged(ref _maxHp, value); Character.MaxHp = value; }
        }

        private int _tempHp;
        public int TempHp
        {
            get => _tempHp;
            set { this.RaiseAndSetIfChanged(ref _tempHp, value); Character.TempHp = value; }
        }

        private int _armorClass;
        public int ArmorClass
        {
            get => _armorClass;
            set { this.RaiseAndSetIfChanged(ref _armorClass, value); Character.ArmorClass = value; }
        }

        public ObservableCollection<InventoryItemViewModel> Weapons { get; } = new();
        public ObservableCollection<QuickShortcutViewModel> Shortcuts { get; } = new();
        public bool HasShortcuts => Shortcuts.Count > 0;

        private readonly InitiativeTrackerViewModel? _initiative;
        private readonly Action<string, bool>? _rollToChat;

        public int ProficiencyBonus { get; }
        public ObservableCollection<CombatantViewModel>? Targets => _initiative?.Combatants;

        private CombatantViewModel? _attackTarget;
        public CombatantViewModel? AttackTarget { get => _attackTarget; set => this.RaiseAndSetIfChanged(ref _attackTarget, value); }

        private CombatantViewModel? _ownCombatant;
        public CombatantViewModel? OwnCombatant
        {
            get => _ownCombatant;
            private set
            {
                this.RaiseAndSetIfChanged(ref _ownCombatant, value);
                this.RaisePropertyChanged(nameof(HasCombatLine));
                this.RaisePropertyChanged(nameof(IsMyTurn));
                this.RaisePropertyChanged(nameof(CanAttackNow));
                RaiseEconomyChanged();
            }
        }

        public bool HasCombatLine => OwnCombatant != null;
        public bool IsMyTurn => OwnCombatant != null && OwnCombatant.IsActive;
        public bool CanAttackNow => OwnCombatant == null || (OwnCombatant.IsActive && (OwnCombatant.HasSwingLeftInThisAction || InitiativeTrackerViewModel.CanAfford(OwnCombatant, App.PM?.Rules?.CostFor("attack") ?? "action")));

        public event Action<string, string, string, int, string, string>? EconomyActionRequested;
        public event Action<SpellAoe>? AoeTemplateRequested;

        private CombatantViewModel? _helpAlly;
        public CombatantViewModel? HelpAlly { get => _helpAlly; set => this.RaiseAndSetIfChanged(ref _helpAlly, value); }

        public bool CanSpendActionNow => IsMyTurn && OwnCombatant!.CanSpendAction;
        public bool CanSpendBonusNow => IsMyTurn && OwnCombatant!.CanSpendBonusAction;
        public bool CanSpendReactionNow => OwnCombatant != null && OwnCombatant.CanSpendReaction;
        public bool CanSurgeNow => IsMyTurn && OwnCombatant!.CanActionSurge;
        public bool CanDashNow => IsMyTurn && !OwnCombatant!.Dashed
                                  && InitiativeTrackerViewModel.CanAfford(OwnCombatant, MapSessionViewModel.EconomyCostFor(OwnCombatant, "dash"));
        public bool CanReadyNow => IsMyTurn && !OwnCombatant!.Readied
                                   && InitiativeTrackerViewModel.CanAfford(OwnCombatant, MapSessionViewModel.EconomyCostFor(OwnCombatant, "ready"));
        public bool CanDelayNow => IsMyTurn;
        public bool CanRollDeathSaveNow => IsMyTurn && OwnCombatant!.CanRollDeathSave;

        public void RaiseEconomyChanged()
        {
            this.RaisePropertyChanged(nameof(CanSpendActionNow));
            this.RaisePropertyChanged(nameof(CanSpendBonusNow));
            this.RaisePropertyChanged(nameof(CanSpendReactionNow));
            this.RaisePropertyChanged(nameof(CanSurgeNow));
            this.RaisePropertyChanged(nameof(CanDashNow));
            this.RaisePropertyChanged(nameof(CanReadyNow));
            this.RaisePropertyChanged(nameof(CanDelayNow));
            this.RaisePropertyChanged(nameof(CanRollDeathSaveNow));
        }

        public ReactiveCommand<Unit, Unit> SpendActionCommand { get; }
        public ReactiveCommand<Unit, Unit> SpendBonusActionCommand { get; }
        public ReactiveCommand<Unit, Unit> SpendReactionCommand { get; }
        public ReactiveCommand<Unit, Unit> ActionSurgeCommand { get; }
        public ReactiveCommand<Unit, Unit> DashCommand { get; }
        public ReactiveCommand<Unit, Unit> ReadyActionCommand { get; }
        public ReactiveCommand<Unit, Unit> DelayTurnCommand { get; }
        public ReactiveCommand<Unit, Unit> RollDeathSaveCommand { get; }
        public ReactiveCommand<Unit, Unit> RollOwedSaveCommand { get; }

        public event Action<string>? SaveOwed;
        public event Action? SaveSettled;
        public ObservableCollection<TacticalActionRule> TacticalActions { get; } = new();
        public ReactiveCommand<string, Unit> TacticalCommand { get; }

        public ReactiveCommand<Unit, Unit> RollInitiativeCommand { get; }
        public ReactiveCommand<Unit, Unit> RollPerceptionCommand { get; }
        public ReactiveCommand<Unit, Unit> RollSaveCommand { get; }
        public ReactiveCommand<Unit, Unit> RollSkillCommand { get; }

        public ObservableCollection<QuickRollOption> SaveOptions { get; } = new();
        private QuickRollOption? _selectedSave;
        public QuickRollOption? SelectedSave { get => _selectedSave; set => this.RaiseAndSetIfChanged(ref _selectedSave, value); }

        public ObservableCollection<QuickRollOption> SkillOptions { get; } = new();
        private QuickRollOption? _selectedSkill;
        public QuickRollOption? SelectedSkill { get => _selectedSkill; set => this.RaiseAndSetIfChanged(ref _selectedSkill, value); }

        private int AbilityScore(string sht)
        {
            var id = App.PM?.Rules?.Abilities?.FirstOrDefault(a => string.Equals(a.Short, sht, StringComparison.OrdinalIgnoreCase))?.Id;
            if (!string.IsNullOrEmpty(id)) return Character.AbilityScores.Get(id!);
            return (sht ?? "").ToUpperInvariant() switch
            {
                "STR" => Character.AbilityScores.Strength,
                "DEX" => Character.AbilityScores.Dexterity,
                "CON" => Character.AbilityScores.Constitution,
                "INT" => Character.AbilityScores.Intelligence,
                "WIS" => Character.AbilityScores.Wisdom,
                "CHA" => Character.AbilityScores.Charisma,
                _ => 10
            };
        }
        private int AbilityMod(string sht) => App.PM?.AbilityMod(AbilityScore(sht)) ?? (int)Math.Floor((AbilityScore(sht) - 10) / 2.0);

        private int PostCheck(string label, int mod) => PostCheck(label, mod, RollMode.Normal);

        private int PostCheck(string label, int mod, RollMode mode)
        {
            var (total, line) = DiceManager.CheckRoll(Name, label, mod, OwnCombatant?.ExhaustionLevel ?? 0, mode);
            _rollToChat?.Invoke(line, false);
            return total;
        }

        private RollMode CheckSwing() => ConditionEffects.CheckMode(OwnCombatant?.Conditions ?? (IEnumerable<string>)Array.Empty<string>(), OwnCombatant?.ExhaustionLevel ?? 0);

        private RollMode SaveSwing() => ConditionEffects.SaveMode(OwnCombatant?.Conditions ?? (IEnumerable<string>)Array.Empty<string>(), OwnCombatant?.ExhaustionLevel ?? 0);
        public ReactiveCommand<int, Unit> SpendSlotCommand { get; }
        public ReactiveCommand<int, Unit> RestoreSlotCommand { get; }
        public ReactiveCommand<Unit, Unit> LongRestCommand { get; }
        public ReactiveCommand<Unit, Unit> CastSpellCommand { get; }

        public ObservableCollection<CastableSpell> CastableSpells { get; } = new();

        private bool _castAsRitual;
        public bool CastAsRitual { get => _castAsRitual; set => this.RaiseAndSetIfChanged(ref _castAsRitual, value); }
        public bool CanCastAsRitual => SelectedCastSpell?.Ritual == true;
        private CastableSpell? _selectedCastSpell;
        public CastableSpell? SelectedCastSpell { get => _selectedCastSpell; set { this.RaiseAndSetIfChanged(ref _selectedCastSpell, value); this.RaisePropertyChanged(nameof(CanCastAsRitual)); } }
        public bool HasSpellcasting => CastableSpells.Count > 0;
        private int _castSaveDc;
        private int _castAttackBonus;
        private int _castAbilityMod;
        private int _castCharLevel;

        public ObservableCollection<int> UpcastLevels { get; } = new();
        private int _selectedCastLevel = 1;
        public int SelectedCastLevel { get => _selectedCastLevel; set => this.RaiseAndSetIfChanged(ref _selectedCastLevel, value); }
        public bool HasUpcastChoice => UpcastLevels.Count > 1;

        public CharacterQuickPanelViewModel(CharacterRuntime character, InitiativeTrackerViewModel? initiative = null, Action<string, bool>? rollToChat = null)
        {
            Character = character;
            _initiative = initiative;
            _rollToChat = rollToChat;
            _currentHp = character.CurrentHp;
            _maxHp = character.MaxHp;
            _tempHp = character.TempHp;
            _armorClass = character.ArmorClass;
            ProficiencyBonus = App.PM?.ProficiencyBonusForLevel(character.Level) ?? (2 + (Math.Max(1, character.Level) - 1) / 4);

            var saveDefs = App.PM?.Rules?.Abilities;
            if (saveDefs != null && saveDefs.Count > 0)
                foreach (var d in saveDefs) SaveOptions.Add(new QuickRollOption(d.Name, d.Short));
            else
                foreach (var (label, ab) in new[] { ("Strength", "STR"), ("Dexterity", "DEX"), ("Constitution", "CON"), ("Intelligence", "INT"), ("Wisdom", "WIS"), ("Charisma", "CHA") })
                    SaveOptions.Add(new QuickRollOption(label, ab));
            var defaultSaveAbility = App.PM?.Rules?.DefaultSaveAbility ?? "dex";
            SelectedSave = SaveOptions.FirstOrDefault(o => string.Equals(o.Ability, defaultSaveAbility, StringComparison.OrdinalIgnoreCase))
                           ?? SaveOptions.FirstOrDefault();

            var skillDefs = App.PM?.Rules?.Skills;
            if (skillDefs != null)
                foreach (var d in skillDefs) SkillOptions.Add(new QuickRollOption(d.Name, d.Ability));
            SelectedSkill = SkillOptions.FirstOrDefault();

            RollInitiativeCommand = ReactiveCommand.Create(() =>
            {
                var mod = (OwnCombatant?.DexMod ?? AbilityMod(App.PM?.Rules?.InitiativeAbility ?? "dex")) + _initBonus;
                var t = PostCheck("initiative", mod);
                if (OwnCombatant != null) OwnCombatant.Initiative = t;
            });
            RollPerceptionCommand = ReactiveCommand.Create(() =>
            {
                var percName = App.PM?.Rules?.PerceptionSkill ?? "Perception";
                var per = App.PM?.Rules?.Skills?.FirstOrDefault(x => string.Equals(x.Name, percName, StringComparison.OrdinalIgnoreCase));
                var bonus = (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(Character.ProficientSkills.Contains(percName), Character.ExpertiseSkills.Contains(percName)), ProficiencyBonus);
                var percSwing = CheckSwing();
                PostCheck(percName + " check", AbilityMod(per?.Ability ?? "WIS") + bonus, percSwing);
            });
            RollSaveCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedSave is not QuickRollOption s) return;
                var prof = (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(Character.ProficientSaves.Contains(s.Ability.ToLowerInvariant())), ProficiencyBonus);
                var swing = SaveSwing();
                PostCheck(s.Label + " save", AbilityMod(s.Ability) + prof, swing);
            });
            RollSkillCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedSkill is not QuickRollOption s) return;
                var bonus = (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(Character.ProficientSkills.Contains(s.Label), Character.ExpertiseSkills.Contains(s.Label)), ProficiencyBonus);
                var swing = CheckSwing();
                PostCheck(s.Label + " check", AbilityMod(s.Ability) + bonus, swing);
            });

            SpendSlotCommand = ReactiveCommand.Create<int>(level => { OwnCombatant?.SpendSlot(level); });
            RestoreSlotCommand = ReactiveCommand.Create<int>(level => { OwnCombatant?.RestoreSlot(level); });
            LongRestCommand = ReactiveCommand.Create(() => { OwnCombatant?.LongRestSlots(); });
            CastSpellCommand = ReactiveCommand.Create(CastSelectedSpell);

            SpendActionCommand = ReactiveCommand.Create(() => RequestEconomy("action"));
            SpendBonusActionCommand = ReactiveCommand.Create(() => RequestEconomy("bonus"));
            SpendReactionCommand = ReactiveCommand.Create(() => RequestEconomy("reaction"));
            ActionSurgeCommand = ReactiveCommand.Create(() => RequestEconomy("surge"));
            DashCommand = ReactiveCommand.Create(() => RequestEconomy("dash"));
            ReadyActionCommand = ReactiveCommand.Create(() => RequestEconomy("ready"));
            DelayTurnCommand = ReactiveCommand.Create(() => RequestEconomy("delay"));
            RollDeathSaveCommand = ReactiveCommand.Create(() => RequestEconomy("death-save"));
            RollOwedSaveCommand = ReactiveCommand.Create(() => RequestEconomy("area-save"));

            this.WhenAnyValue(x => x.OwnCombatant!.HasPendingSave)
                .DistinctUntilChanged()
                .Subscribe(owed =>
                {
                    if (owed && OwnCombatant != null) SaveOwed?.Invoke(OwnCombatant.PendingSaveLabel);
                    else SaveSettled?.Invoke();
                });
            TacticalCommand = ReactiveCommand.Create<string>(id =>
                RequestEconomy(id, AttackTarget?.Id ?? "", HelpAlly?.Id ?? ""));

            var tactical = App.PM?.Rules?.TacticalActions;
            if (tactical != null)
                foreach (var t in tactical.Values)
                    TacticalActions.Add(t);

            this.WhenAnyValue(t => t.SelectedCastSpell).Subscribe(_ => RebuildUpcastLevels());
            this.WhenAnyValue(t => t.OwnCombatant).Subscribe(_ => RebuildUpcastLevels());

            if (_initiative != null)
            {
                _initiative.WhenAnyValue(t => t.ActiveCombatant)
                           .Subscribe(_ => { RefreshOwnCombatant(); this.RaisePropertyChanged(nameof(IsMyTurn)); this.RaisePropertyChanged(nameof(CanAttackNow)); RaiseEconomyChanged(); });
                _initiative.Combatants.CollectionChanged += (_, __) =>
                {
                    RefreshOwnCombatant();
                    this.RaisePropertyChanged(nameof(Targets));
                };
                RefreshOwnCombatant();
            }

            _ = LoadWeaponsAsync();
            _ = LoadCastSpellsAsync();
            _ = LoadInitiativeBonusAsync();
        }

        private int _initBonus;

        private async Task LoadInitiativeBonusAsync()
        {
            if (App.PM == null) return;
            try
            {
                var bonuses = await App.PM.ResolveCharacterBonusesAsync(Character.Id);
                _initBonus = bonuses.Initiative;
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[QuickPanel] init bonus load failed", ex);
            }
        }

        private async Task LoadCastSpellsAsync()
        {
            if (App.PM == null) return;
            try
            {
                var info = await App.PM.ResolveSpellcastingAsync(Character);
                if (info == null) return;
                _castSaveDc = info.Value.SaveDc;
                _castAttackBonus = info.Value.AttackBonus;
                _castAbilityMod = info.Value.AbilityMod;
                _castCharLevel = info.Value.Level;

                var spells = await App.PM.LoadPreparedSpellsAsync(Character);
                CastableSpells.Clear();
                foreach (var s in spells) CastableSpells.Add(s);
                this.RaisePropertyChanged(nameof(HasSpellcasting));
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[QuickPanel] cast load failed", ex);
            }
        }

        private void CastSelectedSpell()
        {
            var spell = SelectedCastSpell;
            if (spell == null || _rollToChat == null) return;

            var kind = SpellCaster.ClassifyCastTime(spell.CastingTime);
            var asRitual = CastAsRitual && spell.Ritual;

            if (OwnCombatant != null && !asRitual && kind != SpellActionKind.Other)
            {
                var castCost = SpellCaster.CastCost(spell.CastingTime);
                if (!InitiativeTrackerViewModel.CanAfford(OwnCombatant, castCost))
                {
                    _rollToChat(Character.Name + " has no " + GameRules.CostPool(castCost) + " left for " + spell.Name, false);
                    return;
                }
            }
            var castLevel = asRitual ? 0 : (spell.Level > 0 ? Math.Max(spell.Level, SelectedCastLevel) : 0);
            if (castLevel > 0 && OwnCombatant != null && !OwnCombatant.HasSlot(castLevel))
            {
                _rollToChat(Character.Name + " has no level " + castLevel + " slot left for " + spell.Name, false);
                return;
            }

            if (OwnCombatant != null && !asRitual)
            {
                RequestEconomy(kind == SpellActionKind.Action ? "cast-action" : kind == SpellActionKind.BonusAction ? "cast-bonus" : kind == SpellActionKind.Reaction ? "cast-reaction" : "cast-none", AttackTarget?.Id ?? "", "", castLevel, "", spell.Id);
            }
            else
            {
                var line = SpellCaster.Resolve(Character.Name, spell.Name, spell.Level, spell.EffectsJson,
                    castLevel, _castCharLevel, _castAbilityMod, ProficiencyBonus, _castSaveDc, _castAttackBonus, AttackTarget?.Name);
                _rollToChat(line, false);
            }

            var aoe = SpellCaster.ReadAoe(spell.EffectsJson);
            if (aoe != null) AoeTemplateRequested?.Invoke(aoe with { SaveDc = _castSaveDc });
        }

        private void RebuildUpcastLevels()
        {
            if (UpcastLevels is null) return;
            UpcastLevels.Clear();
            var spell = SelectedCastSpell;
            var own = OwnCombatant;
            if (spell != null && spell.Level > 0 && own != null)
            {
                var slots = own.SpellSlots;
                var top = slots != null && slots.Count > 0 ? slots.Max(r => r.Level) : spell.Level;
                if (top < spell.Level) top = spell.Level;
                for (var l = spell.Level; l <= top; l++) UpcastLevels.Add(l);
            }
            SelectedCastLevel = spell != null && spell.Level > 0 ? spell.Level : 0;
            this.RaisePropertyChanged(nameof(HasUpcastChoice));
        }

        private void RequestEconomy(string kind, string targetId = "", string allyId = "", int level = 0, string itemId = "", string spellId = "")
        {
            if (OwnCombatant == null) return;
            EconomyActionRequested?.Invoke(kind, targetId, allyId, level, itemId, spellId);
        }

        private void RefreshOwnCombatant()
        {
            OwnCombatant = _initiative?.Combatants.FirstOrDefault(c => c.Id == Character.Id);
        }

        private async Task LoadWeaponsAsync()
        {
            if (App.PM?.GameDataRepo == null || App.PM?.DbManager == null) return;
            try
            {
                var instances = await App.PM.GameDataRepo.LoadInstancesForCharacterAsync(Character.Id);
                if (instances.Count == 0) return;

                var metaById = new Dictionary<string, (string Name, string DataJson)>();
                await using (var conn = await App.PM.DbManager.OpenAsync())
                {
                    foreach (var bid in instances.Select(i => i.BaseItemId).Distinct())
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"SELECT Name, {CatalogResolver.ResolvedJsonSql("Items", "Items")} FROM Items WHERE Id = $id";
                        CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                        cmd.Parameters.AddWithValue("$id", bid);
                        await using var r = await cmd.ExecuteReaderAsync();
                        if (await r.ReadAsync())
                            metaById[bid] = (r.GetString(0), r.IsDBNull(1) ? "{}" : r.GetString(1));
                    }
                }

                Weapons.Clear();
                foreach (var inst in instances)
                {
                    string name, dataJson;
                    if (metaById.TryGetValue(inst.BaseItemId, out var meta)) { name = meta.Name; dataJson = meta.DataJson; }
                    else { name = inst.BaseItemId; dataJson = "{}"; }
                    if (!string.IsNullOrEmpty(inst.CustomName)) name = inst.CustomName!;

                    var vm = new InventoryItemViewModel(inst.Id, inst.BaseItemId, name, dataJson, inst.Quantity <= 0 ? 1 : inst.Quantity, inst.StateJson);
                    if (!vm.IsWeapon) continue;
                    vm.IsEquipped = ReadEquipped(inst.StateJson);
                    if (!vm.IsEquipped) continue;
                    vm.IsOffHand = ReadOffHand(inst.StateJson);
                    vm.AttackRolled += OnWeaponAttack;
                    vm.DamageRolled += OnWeaponDamage;
                    vm.CritRolled += OnWeaponCrit;
                    Weapons.Add(vm);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[QuickPanel] weapon load failed", ex);
            }
        }

        private static bool ReadEquipped(string? stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(stateJson);
                return doc.RootElement.TryGetProperty("equipped", out var e) && e.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }

        private static bool ReadOffHand(string? stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(stateJson);
                return doc.RootElement.TryGetProperty("offhand", out var e) && e.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }

        private void OnWeaponAttack(InventoryItemViewModel item)
        {
            if (_rollToChat == null) return;
            var own = OwnCombatant;
            if (own == null) { RollLooseAttack(item); return; }
            if (!own.IsActive)
            {
                _rollToChat(Name + " can only attack on their own turn.", false);
                return;
            }
            var cost = item.IsOffHand
                ? App.PM?.Rules?.CostFor("offhand-attack", "bonus") ?? "bonus"
                : App.PM?.Rules?.CostFor("attack") ?? "action";
            if ((item.IsOffHand || !own.HasSwingLeftInThisAction) && !InitiativeTrackerViewModel.CanAfford(own, cost))
            {
                _rollToChat(Name + " has no " + cost + " left this turn.", false);
                return;
            }

            // The whole swing is the host's, a hit rolled here would be walked over by the next snapshot before anyone saw the hp move.
            RequestEconomy("weapon-attack", AttackTarget?.Id ?? "", "", 0, item.InstanceId);
            this.RaisePropertyChanged(nameof(CanAttackNow));
        }

        // Nothing tracks an economy outside initiative, so a swing there is just a number in chat.
        private void RollLooseAttack(InventoryItemViewModel item)
        {
            if (_rollToChat == null) return;
            var bonus = WeaponAbilityMod(item) + (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(item.IsProficient), ProficiencyBonus) + item.HitBonus;
            var nat = DiceManager.RollCore(App.PM?.Rules?.AttackDie ?? 20);
            var total = nat + bonus;
            var bonusText = bonus >= 0 ? "+" + bonus : bonus.ToString();
            var crit = (App.PM?.Rules.IsCrit(nat) ?? nat == 20) ? " CRIT!" : (App.PM?.Rules.IsFumble(nat) ?? nat == 1) ? " (nat 1)" : "";
            var tgt = AttackTarget != null ? " -> " + AttackTarget.Name : "";
            _rollToChat(Name + " attacks" + tgt + " with " + item.Name + ": 1d20" + bonusText + " -> [" + nat + "]" + bonusText + " = " + total + crit, false);
        }

        private void OnWeaponDamage(InventoryItemViewModel item) => RollWeaponDamage(item, false);
        private void OnWeaponCrit(InventoryItemViewModel item) => RollWeaponDamage(item, true);

        private void RollWeaponDamage(InventoryItemViewModel item, bool crit)
        {
            if (_rollToChat == null) return;
            var primary = WeaponAbilityMod(item) + item.DamageBonus;
            var lines = new List<string>();
            var grand = 0;
            var first = true;
            foreach (var d in item.DamageValues)
            {
                if (string.IsNullOrWhiteSpace(d.DiceId)) continue;
                var count = d.Count > 0 ? d.Count : 1;
                var mod = (first ? primary : 0) + d.Flat;
                first = false;
                var expr = count + d.DiceId + (mod > 0 ? "+" + mod : (mod < 0 ? mod.ToString() : ""));
                if (!DiceManager.TryRoll(expr, crit, out var res) || res == null) continue;
                grand += res.Total;
                lines.Add(res.Total + " [" + res.Breakdown + "]");
            }
            if (lines.Count == 0) return;
            var tgt = AttackTarget != null ? " -> " + AttackTarget.Name : "";
            var critTxt = crit ? " CRIT" : "";
            _rollToChat(Name + " " + item.Name + " damage" + critTxt + tgt + ": " + string.Join(", ", lines) + " = " + grand, false);
        }

        private int WeaponAbilityMod(InventoryItemViewModel item)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            return rules.AttackAbilitiesFor(item.WeaponCategory, item.IsRanged)
                        .Select(sht => Mod(Character.AbilityScores.Get(rules.AbilityIdForShort(sht))))
                        .DefaultIfEmpty(0).Max();
        }

        private static int Mod(int score) => App.PM?.AbilityMod(score) ?? (int)Math.Floor((score - 10) / 2.0);
    }

    public class QuickShortcutViewModel : ViewModelBase
    {
        public string Label { get; }
        public ReactiveCommand<Unit, Unit> ActivateCommand { get; }

        public QuickShortcutViewModel(string label, Action onActivate)
        {
            Label = label;
            ActivateCommand = ReactiveCommand.Create(onActivate);
        }
    }
    public class DmCombatPanelViewModel : ViewModelBase
    {
        public InitiativeTrackerViewModel Initiative { get; }

        private CombatantViewModel? _selectedCombatant;
        public CombatantViewModel? SelectedCombatant
        {
            get => _selectedCombatant;
            set
            {
                this.RaiseAndSetIfChanged(ref _selectedCombatant, value);
                this.RaisePropertyChanged(nameof(HasSelection));
                this.RaisePropertyChanged(nameof(HasDismissableSummons));
                this.RaisePropertyChanged(nameof(CanSwingSelectedNow));
            }
        }

        public bool HasSelection => SelectedCombatant != null;

        public bool HasDismissableSummons
        {
            get
            {
                var c = SelectedCombatant;
                if (c == null) return false;
                var casterId = c.IsSummon ? c.OwnerCharacterId : c.Id;
                return Initiative.Combatants.Any(x => x.IsSummon && x.IsDrivenBy(casterId));
            }
        }

        public bool CanSwingSelectedNow
        {
            get
            {
                var c = SelectedCombatant;
                if (c == null) return false;
                return c.HasSwingLeftInThisAction || InitiativeTrackerViewModel.CanAfford(c, App.PM?.Rules?.CostFor("attack") ?? "action");
            }
        }

        public bool AutoFlankingEnabled => App.PM?.CombatAutoFlanking ?? true;

        private CombatantViewModel? _attackTarget;
        public CombatantViewModel? AttackTarget
        {
            get => _attackTarget;
            set => this.RaiseAndSetIfChanged(ref _attackTarget, value);
        }

        private readonly Action<string, bool>? _rollToChat;
        private readonly Func<string, string, bool>? _flankingCheck;
        private readonly Func<string, string, double>? _distanceCheck;
        private readonly Func<string, string, SightLine>? _lineCheck;
        private readonly Func<string, string, double, double>? _pushToken;
        private CharacterRuntime? _selectedRuntime;
        private int _selectedProf;
        public ObservableCollection<InventoryItemViewModel> SelectedWeapons { get; } = new();
        public bool HasSelectedWeapons => SelectedWeapons.Count > 0;

        public ObservableCollection<string> CombatLog { get; } = new();

        public event Action<CombatantViewModel>? LinkTokenRequested;
        public event Action<CombatantViewModel>? ResetMoveRequested;
        public event Action? AoeSaveRequested;
        public event Action<CombatantViewModel, PlayerOptionViewModel>? PlayerPulledIn;

        public ReactiveCommand<CombatantViewModel, Unit> LinkTokenCommand { get; }
        public ReactiveCommand<CombatantAttackViewModel, Unit> RollAttackCommand { get; }
        public ReactiveCommand<LegendaryActionOption, Unit> UseLegendaryCommand { get; }
        public ReactiveCommand<LairActionOption, Unit> UseLairCommand { get; }
        public event Action<CombatantViewModel, string, int>? SummonRequested;
        public event Action<CombatantViewModel>? DismissSummonsRequested;
        public ReactiveCommand<Unit, Unit> DismissSummonsCommand { get; }
        public ReactiveCommand<Unit, Unit> RerollInitiativeCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearLogCommand { get; }
        public ReactiveCommand<Unit, Unit> AddAttackCommand { get; }
        public ReactiveCommand<CombatantAttackViewModel, Unit> RemoveAttackCommand { get; }

        private string _newAttackName = "";
        public string NewAttackName { get => _newAttackName; set => this.RaiseAndSetIfChanged(ref _newAttackName, value); }
        private int _newAttackToHit;
        public int NewAttackToHit { get => _newAttackToHit; set => this.RaiseAndSetIfChanged(ref _newAttackToHit, value); }
        private string _newAttackDamage = "";
        public string NewAttackDamage { get => _newAttackDamage; set => this.RaiseAndSetIfChanged(ref _newAttackDamage, value); }
        private string _newAttackDamageType = "";
        public string NewAttackDamageType { get => _newAttackDamageType; set => this.RaiseAndSetIfChanged(ref _newAttackDamageType, value); }
        private int _newAttackRange;
        public int NewAttackRange { get => _newAttackRange; set => this.RaiseAndSetIfChanged(ref _newAttackRange, value); }

        public ObservableCollection<MasteryRule> MasteryOptions { get; } = new();
        private MasteryRule? _newAttackMastery;
        public MasteryRule? NewAttackMastery { get => _newAttackMastery; set => this.RaiseAndSetIfChanged(ref _newAttackMastery, value); }

        private CombatantViewModel? _helpAlly;
        public CombatantViewModel? HelpAlly { get => _helpAlly; set => this.RaiseAndSetIfChanged(ref _helpAlly, value); }

        public ObservableCollection<TacticalActionRule> TacticalActions { get; } = new();
        public ReactiveCommand<string, Unit> TacticalCommand { get; }

        private int _quickAdjustAmount;
        public int QuickAdjustAmount
        {
            get => _quickAdjustAmount;
            set => this.RaiseAndSetIfChanged(ref _quickAdjustAmount, value);
        }

        public ReactiveCommand<Unit, Unit> ApplyDamageCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyHealCommand { get; }
        public ReactiveCommand<Unit, Unit> RollCombatantDeathSaveCommand { get; }
        public ReactiveCommand<int, Unit> SetCoverCommand { get; }
        public ReactiveCommand<Unit, Unit> ToggleFriendlyCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyCombatantEditsCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetMoveCommand { get; }
        public ReactiveCommand<string, Unit> ToggleConditionCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearConditionsCommand { get; }
        public ReactiveCommand<Unit, Unit> ApplyHazardCommand { get; }

        public List<HazardRule> HazardOptions => (App.PM?.Rules?.Hazards.Values ?? Enumerable.Empty<HazardRule>()).ToList();

        private HazardRule? _selectedHazard;
        public HazardRule? SelectedHazard { get => _selectedHazard; set => this.RaiseAndSetIfChanged(ref _selectedHazard, value); }

        private decimal _hazardMagnitude = 10;
        public decimal HazardMagnitude { get => _hazardMagnitude; set => this.RaiseAndSetIfChanged(ref _hazardMagnitude, value); }

        public bool HasHazards => HazardOptions.Count > 0;
        public ReactiveCommand<string, Unit> ApplyConcentrationConditionCommand { get; }
        public ReactiveCommand<Unit, Unit> ConcentrationChangedCommand { get; }
        public ReactiveCommand<Unit, Unit> SpendReactionCommand { get; }
        public ReactiveCommand<Unit, Unit> DashCommand { get; }
        public ReactiveCommand<string, Unit> RollSaveCommand { get; }
        public ReactiveCommand<Unit, Unit> RollAoeSavesCommand { get; }
        public ReactiveCommand<Unit, Unit> ReadyActionCommand { get; }
        public ReactiveCommand<Unit, Unit> TriggerReadiedCommand { get; }

        private string _readyIntent = "";
        public string ReadyIntent { get => _readyIntent; set => this.RaiseAndSetIfChanged(ref _readyIntent, value); }

        private string _readyTrigger = "";
        public string ReadyTrigger { get => _readyTrigger; set => this.RaiseAndSetIfChanged(ref _readyTrigger, value); }
        public ReactiveCommand<Unit, Unit> DelayTurnCommand { get; }
        public event Action<SpellAoe>? AoeTemplateRequested;
        public event Action<ActionResolution, CombatantViewModel, string, CombatantViewModel?, SpellAoe?>? AreaCastArmed;

        public event Action<CombatantViewModel, double>? StandingUp;
        private int _saveDc = App.PM?.Rules?.DefaultSaveDc ?? 10;
        public int SaveDc { get => _saveDc; set => this.RaiseAndSetIfChanged(ref _saveDc, value); }
        public ObservableCollection<string> AoeSaveAbilities { get; } = new();
        private string _aoeSaveAbility = "DEX";
        public string AoeSaveAbility { get => _aoeSaveAbility; set => this.RaiseAndSetIfChanged(ref _aoeSaveAbility, value); }
        public ObservableCollection<string> ConditionOptions { get; } = new();
        public int NoCover => 0;
        public int CoverHalfBonus => App.PM?.Rules?.CoverHalfBonus ?? 2;
        public int CoverThreeQuartersBonus => App.PM?.Rules?.CoverThreeQuartersBonus ?? 5;

        public Action<CombatantViewModel>? RollOwedSaveFor { get; set; }
        public ReactiveCommand<Unit, Unit> RollTheirOwedSaveCommand { get; private set; } = null!;
        public ReactiveCommand<Unit, Unit> RemoveSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> AddNpcCommand { get; }
        public ReactiveCommand<Unit, Unit> AddPlayerCommand { get; }
        public ReactiveCommand<Unit, Unit> AddEncounterCommand { get; }
        public ReactiveCommand<Unit, Unit> SpendActionCommand { get; }
        public ReactiveCommand<Unit, Unit> SpendBonusActionCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetEconomyCommand { get; }
        public ReactiveCommand<Unit, Unit> ActionSurgeCommand { get; }
        public ReactiveCommand<int, Unit> SpendSlotCommand { get; }
        public ReactiveCommand<int, Unit> RestoreSlotCommand { get; }
        public ReactiveCommand<Unit, Unit> LongRestCommand { get; }
        public ReactiveCommand<Unit, Unit> CastSpellCommand { get; }
        public ObservableCollection<CastableSpell> CastableSpells { get; } = new();

        private bool _castAsRitual;
        public bool CastAsRitual { get => _castAsRitual; set => this.RaiseAndSetIfChanged(ref _castAsRitual, value); }
        public bool CanCastAsRitual => SelectedCastSpell?.Ritual == true;
        private CastableSpell? _selectedCastSpell;
        public CastableSpell? SelectedCastSpell { get => _selectedCastSpell; set { this.RaiseAndSetIfChanged(ref _selectedCastSpell, value); this.RaisePropertyChanged(nameof(CanCastAsRitual)); } }
        public bool HasSpellcasting => CastableSpells.Count > 0;
        private int _castSaveDc;
        private int _castAttackBonus;
        private int _castAbilityMod;
        private int _castCharLevel;

        public ObservableCollection<int> UpcastLevels { get; } = new();
        private int _selectedCastLevel = 1;
        public int SelectedCastLevel { get => _selectedCastLevel; set => this.RaiseAndSetIfChanged(ref _selectedCastLevel, value); }
        public bool HasUpcastChoice => UpcastLevels.Count > 1;

        public event Action? AddNpcRequested;
        public event Action? AddPlayerRequested;
        public event Action? AddEncounterRequested;
        public event Action<EncounterPreset>? EncounterChosen;

        public DmCombatPanelViewModel(InitiativeTrackerViewModel initiative, Action<string, bool>? rollToChat = null, Func<string, string, bool>? flankingCheck = null, Func<string, string, double>? distanceCheck = null, Func<string, string, SightLine>? lineCheck = null, Func<string, string, double, double>? pushToken = null)
        {
            Initiative = initiative;
            _rollToChat = (t, s) => { LogLine(t); rollToChat?.Invoke(t, s); };
            _flankingCheck = flankingCheck;
            _distanceCheck = distanceCheck;
            _lineCheck = lineCheck;
            _pushToken = pushToken;
            if (App.PM != null) _ = WarmFlankingSettingAsync();

            this.WhenAnyValue(t => t.SelectedCastSpell).Subscribe(_ => RebuildUpcastLevels());
            this.WhenAnyValue(t => t.SelectedCombatant).Subscribe(_ => RebuildUpcastLevels());

            initiative.WhenAnyValue(t => t.SelectedCombatant)
                      .Subscribe(c => { SelectedCombatant = c; _ = LoadSelectedWeaponsAsync(c); });

            initiative.WhenAnyValue(t => t.ActiveCombatant)
                      .Subscribe(c =>
                      {
                          if (initiative.SelectedCombatant == null)
                              initiative.SelectedCombatant = c;
                      });

            ApplyDamageCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                var hpLost = SelectedCombatant.TakeDamage(QuickAdjustAmount);
                ConcentrationOnTarget(SelectedCombatant, hpLost);
                QuickAdjustAmount = 0;
                Initiative.NotifyStateChanged();
            });

            ApplyHealCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                SelectedCombatant.CurrentHp =
                    Math.Min(SelectedCombatant.MaxHp, SelectedCombatant.CurrentHp + QuickAdjustAmount);
                QuickAdjustAmount = 0;
                Initiative.NotifyStateChanged();
            });

            AddAttackCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null || string.IsNullOrWhiteSpace(NewAttackName)) return;
                var dmg = string.IsNullOrWhiteSpace(NewAttackDamage) ? "0" : NewAttackDamage.Trim();
                var type = string.IsNullOrWhiteSpace(NewAttackDamageType) ? "" : NewAttackDamageType.Trim();
                SelectedCombatant.Attacks.Add(new CombatantAttackViewModel(NewAttackName.Trim(), NewAttackToHit, dmg, type, NewAttackRange < 0 ? 0 : NewAttackRange, NewAttackMastery?.Id ?? ""));
                NewAttackName = "";
                NewAttackToHit = 0;
                NewAttackDamage = "";
                NewAttackDamageType = "";
                NewAttackRange = 0;
                NewAttackMastery = null;
                Initiative.NotifyStateChanged();
            });

            RemoveAttackCommand = ReactiveCommand.Create<CombatantAttackViewModel>(atk =>
            {
                if (SelectedCombatant == null || atk == null) return;
                SelectedCombatant.Attacks.Remove(atk);
                Initiative.NotifyStateChanged();
            });

            RollCombatantDeathSaveCommand = ReactiveCommand.Create(RollCombatantDeathSave);
            SetCoverCommand = ReactiveCommand.Create<int>(b => { if (SelectedCombatant != null) SelectedCombatant.CoverBonus = b; });
            RollTheirOwedSaveCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant is { HasPendingSave: true } owing) RollOwedSaveFor?.Invoke(owing);
            });

            // Checkbox two-way binding already flipped the value, this just pushes the new side out so it syncs and saves
            ToggleFriendlyCommand = ReactiveCommand.Create(() => Initiative.NotifyStateChanged());
            ApplyCombatantEditsCommand = ReactiveCommand.Create(() => Initiative.NotifyStateChanged());
            ResetMoveCommand = ReactiveCommand.Create(() => { if (SelectedCombatant != null) ResetMoveRequested?.Invoke(SelectedCombatant); });

            foreach (var name in BuildConditionOptions()) ConditionOptions.Add(name);
            ToggleConditionCommand = ReactiveCommand.Create<string>(name =>
            {
                var c = SelectedCombatant;
                if (c == null || string.IsNullOrWhiteSpace(name)) return;
                var existing = c.Conditions.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    c.Conditions.Remove(existing);
                    if (string.Equals(name, "Prone", StringComparison.OrdinalIgnoreCase)) StandUpCost(c);
                }
                else c.Conditions.Add(name);
                c.RaiseConditionsChanged();
                Initiative.NotifyStateChanged();
            });

            ApplyHazardCommand = ReactiveCommand.Create(() =>
            {
                var c = SelectedCombatant;
                var rule = SelectedHazard;
                if (c == null || rule == null) return;
                if (ApplyHazardTo(c, rule.Id, (double)HazardMagnitude).Length == 0)
                    LogLine(rule.Name + " did nothing to " + c.Name + ", check the distance.");
            });

            ClearConditionsCommand = ReactiveCommand.Create(() =>
            {
                var c = SelectedCombatant;
                if (c == null || c.Conditions.Count == 0) return;
                c.Conditions.Clear();
                c.RaiseConditionsChanged();
                Initiative.NotifyStateChanged();
            });

            ApplyConcentrationConditionCommand = ReactiveCommand.Create<string>(name =>
            {
                var caster = SelectedCombatant;
                var target = AttackTarget;
                if (caster == null || target == null || string.IsNullOrWhiteSpace(name)) return;
                if (!target.HasCondition(name)) { target.Conditions.Add(name); target.RaiseConditionsChanged(); }
                caster.Concentration = true;
                if (!caster.ConcentrationEffects.Any(l => l.TargetId == target.Id && string.Equals(l.Condition, name, StringComparison.OrdinalIgnoreCase)))
                    caster.ConcentrationEffects.Add(new ConcentrationLink(target.Id, name));
                caster.RaiseConcentrationEffectsChanged();
                Initiative.NotifyStateChanged();
            });

            ConcentrationChangedCommand = ReactiveCommand.Create(() =>
            {
                var c = SelectedCombatant;
                if (c == null) return;
                if (!c.Concentration) DropConcentration(c);
                Initiative.NotifyStateChanged();
            });

            SpendReactionCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                if (SelectedCombatant.SpendReaction()) Initiative.NotifyStateChanged();
            });

            RollSaveCommand = ReactiveCommand.Create<string>(ability =>
            {
                if (SelectedCombatant == null) return;
                var cost = App.PM?.Rules?.CostFor("save", "none") ?? "none";
                if (!InitiativeTrackerViewModel.CanAfford(SelectedCombatant, cost))
                {
                    LogLine(SelectedCombatant.Name + " has no " + cost + " left this turn.");
                    return;
                }
                InitiativeTrackerViewModel.Pay(SelectedCombatant, cost);
                RollSave(SelectedCombatant, ability, SaveDc);
                if (cost != "none") Initiative.NotifyStateChanged();
            });

            // Ticking the box costs nothing on purpose, SettleDashCost charges the action only once the token outruns its normal speed, so this just tells the overlay to redraw
            DashCommand = ReactiveCommand.Create(() => Initiative.NotifyStateChanged());

            RemoveSelectedCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                var going = SelectedCombatant;
                if (ReferenceEquals(Initiative.ActiveCombatant, going) && Initiative.Combatants.Count > 1)
                    Initiative.NextTurn();
                Initiative.Combatants.Remove(going);
                if (ReferenceEquals(Initiative.ActiveCombatant, going)) Initiative.ActiveCombatant = null;
                SelectedCombatant = null;
                Initiative.NotifyStateChanged();
            });

            DismissSummonsCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                DismissSummonsRequested?.Invoke(SelectedCombatant);
                this.RaisePropertyChanged(nameof(HasDismissableSummons));
            });

            Initiative.StateChanged += () =>
            {
                this.RaisePropertyChanged(nameof(HasDismissableSummons));
                this.RaisePropertyChanged(nameof(CanSwingSelectedNow));
            };

            LinkTokenCommand = ReactiveCommand.Create<CombatantViewModel>(c =>
            {
                LinkTokenRequested?.Invoke(c);
            });

            AddNpcCommand = ReactiveCommand.Create(() => AddNpcRequested?.Invoke());
            AddPlayerCommand = ReactiveCommand.Create(() => AddPlayerRequested?.Invoke());
            AddEncounterCommand = ReactiveCommand.Create(() => AddEncounterRequested?.Invoke());

            RollAttackCommand = ReactiveCommand.Create<CombatantAttackViewModel>(atk =>
            {
                if (SelectedCombatant == null) return;
                if (atk != null && !atk.IsReady)
                {
                    LogLine(atk.Name + " has not recharged yet.");
                    return;
                }
                var cost = App.PM?.Rules?.CostFor("attack") ?? "action";
                if (!InitiativeTrackerViewModel.TrySpendAttack(SelectedCombatant, cost))
                {
                    LogLine(SelectedCombatant.Name + " has no " + cost + " left this turn.");
                    return;
                }
                RollAttack(SelectedCombatant, atk);
                if (atk != null && atk.NeedsRecharge) atk.IsSpent = true;
                Initiative.NotifyStateChanged();
            });

            UseLegendaryCommand = ReactiveCommand.Create<LegendaryActionOption>(opt =>
            {
                var c = SelectedCombatant;
                if (c == null || opt == null) return;
                if (!c.SpendLegendary(opt.Cost))
                {
                    LogLine(c.IsActive
                        ? c.Name + " cannot take a legendary action on its own turn."
                        : c.Name + " has no legendary actions left this round.");
                    return;
                }

                var atk = string.IsNullOrEmpty(opt.AttackName) ? null : c.Attacks.FirstOrDefault(a => a.Name == opt.AttackName);
                if (atk != null) RollAttack(c, atk);
                else _rollToChat?.Invoke(c.Name + " uses " + opt.Name + (string.IsNullOrWhiteSpace(opt.Description) ? "" : ", " + opt.Description), false);

                LogLine(c.Name + " spends " + opt.Cost + " legendary, " + c.LegendaryRemaining + " left.");
                Initiative.NotifyStateChanged();
            });

            UseLairCommand = ReactiveCommand.Create<LairActionOption>(opt =>
            {
                var c = SelectedCombatant;
                if (c == null || opt == null) return;
                if (!c.UseLairIn(Initiative.Round))
                {
                    LogLine(c.CurrentHp <= 0
                        ? c.Name + " is down, its lair has gone quiet."
                        : c.Name + " has already used its lair this round.");
                    return;
                }

                var atk = string.IsNullOrEmpty(opt.AttackName) ? null : c.Attacks.FirstOrDefault(a => a.Name == opt.AttackName);
                if (atk != null) RollAttack(c, atk);
                else _rollToChat?.Invoke("On count " + c.LairInitiative + ", " + opt.Name + (string.IsNullOrWhiteSpace(opt.Description) ? "" : ", " + opt.Description), false);

                Initiative.NotifyStateChanged();
            });

            RerollInitiativeCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                SelectedCombatant.Initiative = DiceManager.RollInitiativeDie(SelectedCombatant.Surprised) + SelectedCombatant.DexMod;
                Initiative.NotifyStateChanged();
            });

            ClearLogCommand = ReactiveCommand.Create(() => CombatLog.Clear());

            var masteries = App.PM?.Rules?.Masteries;
            if (masteries != null)
                foreach (var m in masteries.Values.OrderBy(m => m.Name))
                    MasteryOptions.Add(m);

            var abilityDefs = App.PM?.Rules?.Abilities;
            if (abilityDefs != null && abilityDefs.Count > 0)
                foreach (var def in abilityDefs) AoeSaveAbilities.Add(def.Short);
            else
                foreach (var sht in new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA" }) AoeSaveAbilities.Add(sht);

            var defaultSave = App.PM?.Rules?.DefaultSaveAbility ?? "dex";
            AoeSaveAbility = AoeSaveAbilities.FirstOrDefault(x => string.Equals(x, defaultSave, StringComparison.OrdinalIgnoreCase))
                             ?? AoeSaveAbilities.FirstOrDefault() ?? defaultSave.ToUpperInvariant();

            SpendActionCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                if (SelectedCombatant.SpendAction()) Initiative.NotifyStateChanged();
            });

            SpendBonusActionCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                if (SelectedCombatant.SpendBonusAction()) Initiative.NotifyStateChanged();
            });

            ResetEconomyCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                SelectedCombatant.ResetTurnEconomy();
                Initiative.NotifyStateChanged();
            });

            ActionSurgeCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                if (SelectedCombatant.UseActionSurge()) Initiative.NotifyStateChanged();
            });

            SpendSlotCommand = ReactiveCommand.Create<int>(level =>
            {
                if (SelectedCombatant == null) return;
                if (SelectedCombatant.SpendSlot(level)) Initiative.NotifyStateChanged();
            });

            RestoreSlotCommand = ReactiveCommand.Create<int>(level =>
            {
                if (SelectedCombatant == null) return;
                SelectedCombatant.RestoreSlot(level);
                Initiative.NotifyStateChanged();
            });

            LongRestCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null) return;
                SelectedCombatant.LongRestSlots();
                Initiative.NotifyStateChanged();
            });

            CastSpellCommand = ReactiveCommand.Create(CastSelectedSpell);

            RollAoeSavesCommand = ReactiveCommand.Create(() => AoeSaveRequested?.Invoke());

            ReadyActionCommand = ReactiveCommand.Create(() =>
            {
                if (SelectedCombatant == null || !SelectedCombatant.CanSpendAction) return;
                SelectedCombatant.SpendAction();
                SelectedCombatant.Readied = true;
                SelectedCombatant.ReadiedIntent = ReadyIntent;
                SelectedCombatant.ReadiedTrigger = ReadyTrigger;
                LogLine(SelectedCombatant.Name + " readies an action.");
                Initiative.NotifyStateChanged();
            });

            TriggerReadiedCommand = ReactiveCommand.Create(() =>
            {
                var actor = SelectedCombatant;
                if (actor == null || !actor.Readied) return;
                if (!InitiativeTrackerViewModel.CanAfford(actor, "reaction"))
                {
                    LogLine(actor.Name + " has no reaction left to spring the readied action.");
                    return;
                }
                InitiativeTrackerViewModel.Pay(actor, "reaction");
                var what = string.IsNullOrWhiteSpace(actor.ReadiedIntent) ? "their readied action" : actor.ReadiedIntent;
                var when = string.IsNullOrWhiteSpace(actor.ReadiedTrigger) ? "" : " (" + actor.ReadiedTrigger + ")";
                actor.Readied = false;
                actor.ReadiedIntent = "";
                actor.ReadiedTrigger = "";
                _rollToChat?.Invoke(actor.Name + " springs " + what + when + ", reaction spent.", false);
                Initiative.NotifyStateChanged();
            });

            DelayTurnCommand = ReactiveCommand.Create(() => Initiative.DelayTurn());

            TacticalCommand = ReactiveCommand.Create<string>(RunTactical);

            var tactical = App.PM?.Rules?.TacticalActions;
            if (tactical != null)
                foreach (var t in tactical.Values)
                    TacticalActions.Add(t);
        }

        private void RunTactical(string kind)
        {
            var line = Initiative.ResolveTacticalAction(SelectedCombatant, kind, AttackTarget, HelpAlly);
            if (!string.IsNullOrEmpty(line)) _rollToChat?.Invoke(line, false);
        }

        private async Task LoadSelectedWeaponsAsync(CombatantViewModel? c)
        {
            SelectedWeapons.Clear();
            CastableSpells.Clear();
            _selectedRuntime = null;
            this.RaisePropertyChanged(nameof(HasSelectedWeapons));
            this.RaisePropertyChanged(nameof(HasSpellcasting));
            if (c == null || !c.IsPlayerCharacter || string.IsNullOrEmpty(c.Id)) return;
            if (App.PM?.GameDataRepo == null || App.PM?.DbManager == null) return;
            try
            {
                var runtime = await App.PM.LoadCharacterByIdAsync(c.Id);
                if (runtime == null) return;
                _selectedRuntime = runtime;
                _selectedProf = App.PM.ProficiencyBonusForLevel(runtime.Level);

                var sc = await App.PM.ResolveSpellcastingAsync(runtime);
                if (sc != null)
                {
                    _castSaveDc = sc.Value.SaveDc;
                    _castAttackBonus = sc.Value.AttackBonus;
                    _castAbilityMod = sc.Value.AbilityMod;
                    _castCharLevel = sc.Value.Level;
                    var castable = await App.PM.LoadPreparedSpellsAsync(runtime);
                    foreach (var s in castable) CastableSpells.Add(s);
                    this.RaisePropertyChanged(nameof(HasSpellcasting));
                }

                var instances = await App.PM.GameDataRepo.LoadInstancesForCharacterAsync(c.Id);
                if (instances.Count == 0) return;

                var metaById = new Dictionary<string, (string Name, string DataJson)>();
                await using (var conn = await App.PM.DbManager.OpenAsync())
                {
                    foreach (var bid in instances.Select(i => i.BaseItemId).Distinct())
                    {
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = $"SELECT Name, {CatalogResolver.ResolvedJsonSql("Items", "Items")} FROM Items WHERE Id = $id";
                        CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                        cmd.Parameters.AddWithValue("$id", bid);
                        await using var r = await cmd.ExecuteReaderAsync();
                        if (await r.ReadAsync())
                            metaById[bid] = (r.GetString(0), r.IsDBNull(1) ? "{}" : r.GetString(1));
                    }
                }

                foreach (var inst in instances)
                {
                    string name, dataJson;
                    if (metaById.TryGetValue(inst.BaseItemId, out var meta)) { name = meta.Name; dataJson = meta.DataJson; }
                    else { name = inst.BaseItemId; dataJson = "{}"; }
                    if (!string.IsNullOrEmpty(inst.CustomName)) name = inst.CustomName!;

                    var vm = new InventoryItemViewModel(inst.Id, inst.BaseItemId, name, dataJson, inst.Quantity <= 0 ? 1 : inst.Quantity, inst.StateJson);
                    if (!vm.IsWeapon) continue;
                    vm.IsEquipped = ReadEquipped(inst.StateJson);
                    if (!vm.IsEquipped) continue;
                    vm.IsOffHand = ReadOffHand(inst.StateJson);
                    vm.AttackRolled += OnDmWeaponAttack;
                    vm.DamageRolled += OnDmWeaponDamage;
                    vm.CritRolled += OnDmWeaponCrit;
                    SelectedWeapons.Add(vm);
                }
                this.RaisePropertyChanged(nameof(HasSelectedWeapons));
            }
            catch (Exception ex)
            {
                ErrorLog.Log($"[DmPanel] weapon load failed", ex);
            }
        }

        private void CastSelectedSpell()
        {
            var spell = SelectedCastSpell;
            if (spell == null || _rollToChat == null || SelectedCombatant == null) return;

            var kind = SpellCaster.ClassifyCastTime(spell.CastingTime);
            var castCost = SpellCaster.CastCost(spell.CastingTime);
            var asRitual = CastAsRitual && spell.Ritual;

            if (!asRitual && kind != SpellActionKind.Other && !InitiativeTrackerViewModel.CanAfford(SelectedCombatant, castCost))
            {
                _rollToChat(SelectedCombatant.Name + " has no " + GameRules.CostPool(castCost) + " left for " + spell.Name, false);
                return;
            }

            var castLevel = asRitual ? 0 : (spell.Level > 0 ? Math.Max(spell.Level, SelectedCastLevel) : 0);
            if (castLevel > 0)
            {
                if (!SelectedCombatant.SpendSlot(castLevel))
                {
                    _rollToChat(SelectedCombatant.Name + " has no level " + castLevel + " slot left for " + spell.Name, false);
                    return;
                }
            }

            if (!asRitual && kind != SpellActionKind.Other) InitiativeTrackerViewModel.Pay(SelectedCombatant, castCost);

            var res = ActionProcessor.Resolve(SelectedCombatant.Name, spell.Name, spell.Level, spell.EffectsJson,
                castLevel, _castCharLevel, _castAbilityMod, _selectedProf, _castSaveDc, _castAttackBonus,
                AttackTarget?.Name, AttackTarget != null ? AttackTarget.EffectiveArmorClass + AttackTarget.CoverBonus : 0);

            var aoe = SpellCaster.ReadAoe(spell.EffectsJson);
            if (aoe != null) AoeTemplateRequested?.Invoke(aoe with { SaveDc = _castSaveDc });

            CombatantViewModel? holder = null;
            if (spell.Concentration)
            {
                Initiative.EndConcentrationEffects(SelectedCombatant);
                SelectedCombatant.Concentration = true;
                holder = SelectedCombatant;
            }

            var line = res.Line;

            var bitesNow = res.Kind == ActionOutcomeKind.Damage && !string.IsNullOrEmpty(res.SaveAbility) && res.HpDelta < 0;
            var armedArea = aoe != null && (bitesNow || aoe.LastsRounds > 0);
            if (armedArea)
            {
                AreaCastArmed?.Invoke(res, SelectedCombatant, spell.Name, holder, aoe);
                line += aoe!.LastsRounds > 0 && !bitesNow
                    ? "  [place the area, it stays for " + aoe.LastsRounds + " rounds]"
                    : "  [place the area to see who it catches]";
            }

            if (!armedArea && AttackTarget != null && bitesNow)
            {
                var total = RollSave(AttackTarget, res.SaveAbility!, res.SaveDc);
                var raw = -res.HpDelta;
                var halfDiv = Math.Max(1, App.PM?.Rules?.HalfOnSaveDivisor ?? 2);
                var saved = (App.PM?.Rules ?? new GameRules()).SaveSucceeds(total, res.SaveDc);
                var beforeResist = saved ? (res.HalfOnSave ? raw / halfDiv : 0) : raw;
                var applied = AttackTarget.ScaleDamageByType(beforeResist, res.DamageType);
                var hpLost = AttackTarget.TakeDamage(applied);
                ConcentrationOnTarget(AttackTarget, hpLost);
                line += "  [" + AttackTarget.Name + " " + AttackTarget.CurrentHp + "/" + AttackTarget.MaxHp + "]";
                line += ApplySpellCondition(res, AttackTarget, saved, spell.Name, holder);
            }
            else if (!armedArea && AttackTarget != null && res.AutoApply && res.Kind != ActionOutcomeKind.None)
            {
                if (res.HpDelta < 0)
                {
                    var applied = AttackTarget.ScaleDamageByType(-res.HpDelta, res.DamageType);
                    var hpLost = AttackTarget.TakeDamage(applied);
                    ConcentrationOnTarget(AttackTarget, hpLost);
                }
                else if (res.HpDelta > 0)
                {
                    var upper = AttackTarget.MaxHp > 0 ? AttackTarget.MaxHp : int.MaxValue;
                    AttackTarget.CurrentHp = Math.Min(upper, AttackTarget.CurrentHp + res.HpDelta);
                }
                line += "  [" + AttackTarget.Name + " " + AttackTarget.CurrentHp + "/" + AttackTarget.MaxHp + "]";
                line += ApplySpellCondition(res, AttackTarget, false, spell.Name, holder);
            }
            else if (!armedArea && AttackTarget != null && !string.IsNullOrWhiteSpace(res.Condition))
            {
                line += ApplySpellCondition(res, AttackTarget, RollSpellSave(res, AttackTarget), spell.Name, holder);
            }

            _rollToChat(line, false);
            SummonRequested?.Invoke(SelectedCombatant, spell.Id, castLevel);
            Initiative.NotifyStateChanged();
        }

        private void RebuildUpcastLevels()
        {
            // Came in null here mid reactive wire-up and blew the whole app up, cheaper to bail than to chase the exact timing.
            if (UpcastLevels is null) return;
            UpcastLevels.Clear();
            var spell = SelectedCastSpell;
            var caster = SelectedCombatant;
            if (spell != null && spell.Level > 0 && caster != null)
            {
                var slots = caster.SpellSlots;
                var top = slots != null && slots.Count > 0 ? slots.Max(r => r.Level) : spell.Level;
                if (top < spell.Level) top = spell.Level;
                for (var l = spell.Level; l <= top; l++) UpcastLevels.Add(l);
            }
            SelectedCastLevel = spell != null && spell.Level > 0 ? spell.Level : 0;
            this.RaisePropertyChanged(nameof(HasUpcastChoice));
        }

        private static bool ReadEquipped(string? stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(stateJson);
                return doc.RootElement.TryGetProperty("equipped", out var e) && e.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }

        private static bool ReadOffHand(string? stateJson)
        {
            if (string.IsNullOrWhiteSpace(stateJson)) return false;
            try
            {
                using var doc = JsonDocument.Parse(stateJson);
                return doc.RootElement.TryGetProperty("offhand", out var e) && e.ValueKind == JsonValueKind.True;
            }
            catch { return false; }
        }

        private void OnDmWeaponAttack(InventoryItemViewModel item)
        {
            if (_selectedRuntime == null || SelectedCombatant == null) return;
            ResolveWeaponAttack(SelectedCombatant, AttackTarget, item, _selectedRuntime, _selectedProf, false);
        }

        // A remote player has no tracker in front of them, so their swing lands here whole, damage and all, while the dm keeps rolling damage on their own button
        internal string ApplySaveDamage(ActionResolution res, CombatantViewModel target, string spellName, CombatantViewModel? holder)
        {
            var total = RollSave(target, res.SaveAbility!, res.SaveDc);
            var raw = -res.HpDelta;
            var halfDiv = Math.Max(1, App.PM?.Rules?.HalfOnSaveDivisor ?? 2);
            var saved = (App.PM?.Rules ?? new GameRules()).SaveSucceeds(total, res.SaveDc);
            var beforeResist = saved ? (res.HalfOnSave ? raw / halfDiv : 0) : raw;
            var applied = target.ScaleDamageByType(beforeResist, res.DamageType);
            var hpLost = target.TakeDamage(applied);
            ConcentrationOnTarget(target, hpLost);
            var note = "  [" + target.Name + " rolled " + total + " vs DC " + res.SaveDc + ", " + (saved ? "saved" : "failed")
                       + ", " + target.CurrentHp + "/" + target.MaxHp + "]";
            return note + ApplySpellCondition(res, target, saved, spellName, holder);
        }

        public async Task ResolvePlayerSpellCastAsync(CombatantViewModel caster, CombatantViewModel? target, string spellId, int castLevel)
        {
            if (string.IsNullOrEmpty(spellId) || App.PM == null || _rollToChat == null) return;
            try
            {
                var runtime = await App.PM.LoadCharacterByIdAsync(caster.Id);
                if (runtime == null) return;
                var info = await App.PM.ResolveSpellcastingAsync(runtime);
                if (info == null) return;
                var spells = await App.PM.LoadPreparedSpellsAsync(runtime);
                var spell = spells.FirstOrDefault(s => s.Id == spellId);
                if (spell == null) return;

                var res = ActionProcessor.Resolve(caster.Name, spell.Name, spell.Level, spell.EffectsJson,
                    castLevel, info.Value.Level, info.Value.AbilityMod, App.PM.ProficiencyBonusForLevel(runtime.Level),
                    info.Value.SaveDc, info.Value.AttackBonus,
                    target?.Name, target != null ? target.ArmorClass + target.CoverBonus : 0);

                CombatantViewModel? holder = null;
                if (spell.Concentration)
                {
                    Initiative.EndConcentrationEffects(caster);
                    caster.Concentration = true;
                    holder = caster;
                }

                var line = res.Line;

                var areaShape = SpellCaster.ReadAoe(spell.EffectsJson);
                var bitesNow = res.Kind == ActionOutcomeKind.Damage && !string.IsNullOrEmpty(res.SaveAbility) && res.HpDelta < 0;
                if (areaShape != null && (bitesNow || areaShape.LastsRounds > 0))
                {
                    AreaCastArmed?.Invoke(res, caster, spell.Name, holder, areaShape);
                    line += areaShape.LastsRounds > 0 && !bitesNow
                        ? "  [place the area, it stays for " + areaShape.LastsRounds + " rounds]"
                        : "  [place the area to see who it catches]";
                }
                else if (target != null && res.Kind == ActionOutcomeKind.Damage && !string.IsNullOrEmpty(res.SaveAbility) && res.HpDelta < 0)
                {
                    line += ApplySaveDamage(res, target, spell.Name, holder);
                }
                else if (target != null && res.AutoApply && res.Kind != ActionOutcomeKind.None)
                {
                    if (res.HpDelta < 0)
                    {
                        var applied = target.ScaleDamageByType(-res.HpDelta, res.DamageType);
                        var hpLost = target.TakeDamage(applied);
                        ConcentrationOnTarget(target, hpLost);
                    }
                    else if (res.HpDelta > 0)
                    {
                        var upper = target.MaxHp > 0 ? target.MaxHp : int.MaxValue;
                        target.CurrentHp = Math.Min(upper, target.CurrentHp + res.HpDelta);
                    }
                    line += "  [" + target.Name + " " + target.CurrentHp + "/" + target.MaxHp + "]";
                    line += ApplySpellCondition(res, target, false, spell.Name, holder);
                }
                else if (target != null && !string.IsNullOrWhiteSpace(res.Condition))
                {
                    line += ApplySpellCondition(res, target, RollSpellSave(res, target), spell.Name, holder);
                }

                _rollToChat(line, false);
                Initiative.NotifyStateChanged();
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[DmPanel] player spell cast failed", ex);
            }
        }

        public async Task ResolvePlayerWeaponAttackAsync(CombatantViewModel attacker, CombatantViewModel? target, string itemInstanceId)
        {
            if (string.IsNullOrEmpty(itemInstanceId) || App.PM?.GameDataRepo == null || App.PM?.DbManager == null) return;
            try
            {
                var runtime = await App.PM.LoadCharacterByIdAsync(attacker.Id);
                if (runtime == null) return;
                var item = await LoadEquippedWeaponAsync(attacker.Id, itemInstanceId);
                if (item == null)
                {
                    LogLine(attacker.Name + " swung a weapon they are not holding.");
                    return;
                }
                ResolveWeaponAttack(attacker, target, item, runtime, App.PM.ProficiencyBonusForLevel(runtime.Level), true);
            }
            catch (Exception ex)
            {
                ErrorLog.Log("[DmPanel] player weapon attack failed", ex);
            }
        }

        private async Task<InventoryItemViewModel?> LoadEquippedWeaponAsync(string characterId, string itemInstanceId)
        {
            var instances = await App.PM!.GameDataRepo!.LoadInstancesForCharacterAsync(characterId);
            var inst = instances.FirstOrDefault(i => i.Id == itemInstanceId);
            if (inst == null || !ReadEquipped(inst.StateJson)) return null;

            var name = inst.BaseItemId;
            var dataJson = "{}";
            await using (var conn = await App.PM.DbManager!.OpenAsync())
            {
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT Name, {CatalogResolver.ResolvedJsonSql("Items", "Items")} FROM Items WHERE Id = $id";
                CatalogResolver.BindScope(cmd, App.PM.GetActiveTemplateId());
                cmd.Parameters.AddWithValue("$id", inst.BaseItemId);
                await using var r = await cmd.ExecuteReaderAsync();
                if (await r.ReadAsync())
                {
                    name = r.GetString(0);
                    dataJson = r.IsDBNull(1) ? "{}" : r.GetString(1);
                }
            }
            if (!string.IsNullOrEmpty(inst.CustomName)) name = inst.CustomName!;

            var vm = new InventoryItemViewModel(inst.Id, inst.BaseItemId, name, dataJson, inst.Quantity <= 0 ? 1 : inst.Quantity, inst.StateJson);
            vm.IsOffHand = ReadOffHand(inst.StateJson);
            return vm.IsWeapon ? vm : null;
        }

        private bool SpendAmmoFor(CombatantViewModel attacker, InventoryItemViewModel item)
        {
            if (App.PM?.GameDataRepo == null) return true;
            var repo = App.PM.GameDataRepo;
            var left = Task.Run(() => repo.SpendAmmoAsync(attacker.Id, item.AmmoItemId)).GetAwaiter().GetResult();
            if (left < 0)
            {
                LogLine(attacker.Name + " is out of ammunition for " + item.Name + ".");
                return false;
            }
            if (left <= 3) LogLine(attacker.Name + " has " + left + " left for " + item.Name + ".");
            return true;
        }

        private bool SpendItemCharge(CombatantViewModel attacker, InventoryItemViewModel item)
        {
            if (!item.SpendCharge())
            {
                LogLine(item.Name + " has no charges left.");
                return false;
            }
            if (App.PM?.GameDataRepo != null)
                _ = App.PM.GameDataRepo.SetInstanceChargesAsync(item.InstanceId, item.Charges);
            return true;
        }

        private void ResolveWeaponAttack(CombatantViewModel attacker, CombatantViewModel? target, InventoryItemViewModel item, CharacterRuntime rt, int prof, bool resolveDamage)
        {
            if (_rollToChat == null) return;
            // Off hand buys its own bonus action, it does not ride the multiattack.
            var offHand = item.IsOffHand;
            var cost = offHand
                ? App.PM?.Rules?.CostFor("offhand-attack", "bonus") ?? "bonus"
                : App.PM?.Rules?.CostFor("attack") ?? "action";
            var paid = offHand
                ? PayOffHandSwing(attacker, cost)
                : InitiativeTrackerViewModel.TrySpendAttack(attacker, cost);
            if (item.NeedsAmmo && !SpendAmmoFor(attacker, item)) return;
            if (item.HasCharges && !SpendItemCharge(attacker, item)) return;
            if (!paid)
            {
                LogLine(attacker.Name + " has no " + GameRules.CostPool(cost) + " left this turn.");
                return;
            }

            var sight = target != null ? (_lineCheck?.Invoke(attacker.Id, target.Id) ?? SightLine.Clear) : SightLine.Clear;
            if (sight == SightLine.Blocked)
            {
                LogLine(attacker.Name + " has no line to " + target!.Name + ", a wall is in the way.");
                return;
            }

            var bonus = WeaponAbilityMod(rt, item) + (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(item.IsProficient), prof) + item.HitBonus;
            var atk = WeaponAsAttack(rt, item, bonus, offHand && !attacker.OffHandAbilityMod);
            var range = RangeVerdict(attacker, atk, target);
            var nat = RollAttackDie(attacker, target, App.PM?.Rules?.AttackDie ?? 20, out var hadAdvantage, range.LongRange);
            var insp = target != null ? ConsumeInspiration(attacker) : (Bonus: 0, Note: "");
            var total = nat + bonus + insp.Bonus - (App.PM?.Rules?.Exhaustion?.D20Penalty(attacker.ExhaustionLevel) ?? 0);
            var natCrit = App.PM?.Rules.IsCrit(nat) ?? nat == 20;
            var fumble = App.PM?.Rules.IsFumble(nat) ?? nat == 1;
            var bonusText = Signed(bonus);
            var roll = ": 1d20" + bonusText + " -> [" + nat + "]" + bonusText + insp.Note + " = " + total;

            string line;
            var hit = false;
            var miss = false;
            var crit = natCrit;
            var dealt = 0;
            if (target == null)
                line = attacker.Name + " attacks with " + item.Name + roll + (natCrit ? " CRIT!" : fumble ? " (nat 1)" : "");
            else
            {
                var autoCover = sight == SightLine.Cover ? (App.PM?.Rules?.CoverHalfBonus ?? 2) : 0;
                var effAc = target.EffectiveArmorClass + Math.Max(target.CoverBonus, autoCover);
                var coverTag = autoCover > 0 && autoCover >= target.CoverBonus ? " (cover)" : "";
                (hit, crit) = (App.PM?.Rules ?? new GameRules()).ResolveAttackOutcome(nat, total, effAc);
                miss = !hit;
                line = fumble && !hit
                    ? attacker.Name + " -> " + target.Name + " with " + item.Name + roll + " fumbles (nat " + nat + ")"
                    : attacker.Name + " -> " + target.Name + " with " + item.Name + roll + (hit ? " HIT" : " MISS") + " vs AC " + effAc + coverTag + (crit ? " CRIT!" : "");
            }

            var typedRiders = new List<(string Type, int Amount)>();
            if (resolveDamage && hit && target != null)
            {
                dealt = SafeRollDamage(atk.Damage, crit) + (App.PM?.Rules?.DamageBonusFromConditions(attacker.Conditions) ?? 0);
                var ridden = ApplyRiders(attacker, hadAdvantage, crit);
                typedRiders = ridden.Typed;
                dealt += ridden.Extra;
                line += ", " + dealt + " " + atk.DamageType + ridden.Note;
            }

            line += range.Note;
            line += ApplyMastery(attacker, atk, target, hit, miss);
            if (target != null && (dealt > 0 || typedRiders.Count > 0))
            {
                var hpLostTotal = 0;
                if (dealt > 0)
                {
                    line += ApplyAttackDamage(target, dealt, atk.DamageType, out var hpBase);
                    hpLostTotal += hpBase;
                }
                foreach (var t in typedRiders)
                {
                    line += ApplyAttackDamage(target, t.Amount, t.Type, out var hpRider);
                    hpLostTotal += hpRider;
                }
                ConcentrationOnTarget(target, hpLostTotal);
            }
            _rollToChat(line, false);

            Initiative.NotifyStateChanged();
        }

        private static bool PayOffHandSwing(CombatantViewModel attacker, string cost)
        {
            if (!InitiativeTrackerViewModel.CanAfford(attacker, cost)) return false;
            InitiativeTrackerViewModel.Pay(attacker, cost);
            return true;
        }

        // The damage expression only matters to Graze.
        internal static CombatantAttackViewModel WeaponAsAttack(CharacterRuntime rt, InventoryItemViewModel item, int toHit, bool offHandNoMod = false)
        {
            var range = item.IsRanged ? item.RangeNormal : 0;
            var rangeMax = item.IsRanged ? item.RangeMax : 0;
            var d = item.DamageValues.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.DiceId));
            if (d == null) return new CombatantAttackViewModel(item.Name, toHit, "", "", range, item.Mastery, rangeMax);
            var count = d.Count > 0 ? d.Count : 1;
            var mod = (offHandNoMod ? 0 : WeaponAbilityMod(rt, item)) + item.DamageBonus + d.Flat;
            var expr = count + d.DiceId + (mod > 0 ? "+" + mod : (mod < 0 ? mod.ToString() : ""));
            var typeLabel = (App.PM?.Rules ?? new GameRules()).DamageTypeLabel(d.TypeId);
            return new CombatantAttackViewModel(item.Name, toHit, expr, typeLabel, range, item.Mastery, rangeMax);
        }

        private void RollCombatantDeathSave() => RollDeathSaveFor(SelectedCombatant);

        public void RollDeathSaveFor(CombatantViewModel? c)
        {
            if (c == null || !c.CanRollDeathSave || _rollToChat == null) return;
            var rules = App.PM?.Rules ?? new GameRules();
            int die = rules.AttackDie;
            int roll = DiceManager.RollCore(die);

            if (rules.IsCrit(roll))
            {
                var cap = c.MaxHp > 0 ? c.MaxHp : int.MaxValue;
                c.CurrentHp = Math.Min(cap, Math.Max(0, rules.DeathSaveCritHeal));
                c.DeathSaveSuccesses = 0;
                c.DeathSaveFailures = 0;
                _rollToChat(c.Name + " death save: d" + die + " [" + roll + "] natural, back up on " + rules.DeathSaveCritHeal + " HP.", false);
            }
            else if (rules.IsFumble(roll))
            {
                c.DeathSaveFailures = Math.Min(rules.DeathSaveFailuresToDie, c.DeathSaveFailures + rules.DeathSaveFumbleFailures);
                _rollToChat(c.Name + " death save: d" + die + " [" + roll + "] natural, " + rules.DeathSaveFumbleFailures + " failures. " + c.DeathSaveStatus + ".", false);
            }
            else if (rules.SaveSucceeds(roll, rules.DeathSaveThreshold))
            {
                c.DeathSaveSuccesses = Math.Min(rules.DeathSaveSuccessesToStabilize, c.DeathSaveSuccesses + 1);
                _rollToChat(c.Name + " death save: d" + die + " [" + roll + "] success " + c.DeathSaveSuccesses + "/" + rules.DeathSaveSuccessesToStabilize + ". " + c.DeathSaveStatus + ".", false);
            }
            else
            {
                c.DeathSaveFailures = Math.Min(rules.DeathSaveFailuresToDie, c.DeathSaveFailures + 1);
                _rollToChat(c.Name + " death save: d" + die + " [" + roll + "] failure " + c.DeathSaveFailures + "/" + rules.DeathSaveFailuresToDie + ". " + c.DeathSaveStatus + ".", false);
            }
            Initiative.NotifyStateChanged();
        }

        private void ConcentrationOnTarget(CombatantViewModel? target, int damage)
        {
            if (target == null || !target.Concentration || _rollToChat == null) return;
            if (target.CurrentHp <= 0) { DropConcentration(target); return; }
            if (damage <= 0) return;
            var rules = App.PM?.Rules ?? new GameRules();
            int dc = Math.Min(rules.ConcentrationDcCap, Math.Max(rules.ConcentrationDcFloor, damage / Math.Max(1, rules.ConcentrationDcDivisor)));
            int die = rules.AttackDie;
            var mode = rules.SaveRollModeFrom(target.Conditions);
            var adv = mode == "advantage" || target.AdvantageOn.Contains("save:con");
            var dis = mode == "disadvantage" || (rules.Exhaustion?.SavesAtDisadvantage(target.ExhaustionLevel) ?? false);
            int roll = DiceManager.RollCore(die, adv, dis);
            var bonus = target.ConSaveBonus + target.ConditionalBonusFor("saving-throw")
                        - (rules.Exhaustion?.D20Penalty(target.ExhaustionLevel) ?? 0);
            int total = roll + bonus;
            var bonusTxt = bonus >= 0 ? "+" + bonus : bonus.ToString();
            var swingTxt = adv && !dis ? " (advantage)" : dis && !adv ? " (disadvantage)" : "";
            bool held = rules.SaveSucceeds(total, dc);
            _rollToChat(target.Name + " concentration save (DC " + dc + ")" + swingTxt + ": d" + die + " [" + roll + "]" + bonusTxt + " = " + total + ", " + (held ? "held" : "lost concentration") + ".", false);
            if (!held) DropConcentration(target);
        }

        private void DropConcentration(CombatantViewModel target)
        {
            target.Concentration = false;
            var removed = Initiative.EndConcentrationEffects(target);
            if (removed.Count > 0 && _rollToChat != null)
                _rollToChat(target.Name + "'s concentration ends, " + string.Join(" and ", removed) + " fades.", false);
        }

        private void OnDmWeaponDamage(InventoryItemViewModel item) => RollDmWeaponDamage(item, false);
        private void OnDmWeaponCrit(InventoryItemViewModel item) => RollDmWeaponDamage(item, true);

        private void RollDmWeaponDamage(InventoryItemViewModel item, bool crit)
        {
            if (_rollToChat == null || _selectedRuntime == null || SelectedCombatant == null) return;
            var primary = (item.IsOffHand && !SelectedCombatant.OffHandAbilityMod ? 0 : WeaponAbilityMod(item)) + item.DamageBonus;
            var lines = new List<string>();
            var grand = 0;
            var first = true;
            foreach (var d in item.DamageValues)
            {
                if (string.IsNullOrWhiteSpace(d.DiceId)) continue;
                var count = d.Count > 0 ? d.Count : 1;
                var mod = (first ? primary : 0) + d.Flat;
                first = false;
                var expr = count + d.DiceId + (mod > 0 ? "+" + mod : (mod < 0 ? mod.ToString() : ""));
                if (!DiceManager.TryRoll(expr, crit, out var res) || res == null) continue;
                grand += res.Total;
                lines.Add(res.Total + " [" + res.Breakdown + "]");
            }
            if (lines.Count == 0) return;
            var tgt = AttackTarget != null ? " -> " + AttackTarget.Name : "";
            var critTxt = crit ? " CRIT" : "";
            _rollToChat(SelectedCombatant.Name + " " + item.Name + " damage" + critTxt + tgt + ": " + string.Join(", ", lines) + " = " + grand, false);
        }

        private int WeaponAbilityMod(InventoryItemViewModel item) => WeaponAbilityMod(_selectedRuntime!, item);

        private static int WeaponAbilityMod(CharacterRuntime rt, InventoryItemViewModel item)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            return rules.AttackAbilitiesFor(item.WeaponCategory, item.IsRanged)
                        .Select(sht => Mod(rt.AbilityScores.Get(rules.AbilityIdForShort(sht))))
                        .DefaultIfEmpty(0).Max();
        }

        private static int RuntimeAbilityMod(CharacterRuntime rt, string? abilityShort)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            return Mod(rt.AbilityScores.Get(rules.AbilityIdForShort(abilityShort ?? "")));
        }

        private static int RuntimeSaveBonus(CharacterRuntime rt, string? abilityShort)
        {
            var prof = App.PM?.ProficiencyBonusForLevel(rt.Level) ?? 2;
            var proficient = rt.ProficientSaves.Contains((abilityShort ?? "").ToLowerInvariant());
            return RuntimeAbilityMod(rt, abilityShort) + (App.PM?.Rules ?? new GameRules()).RankBonus(GameRules.RankIdFor(proficient), prof);
        }

        private static int Mod(int score) => App.PM?.AbilityMod(score) ?? (int)Math.Floor((score - 10) / 2.0);

        private void StandUpCost(CombatantViewModel c)
        {
            var fraction = App.PM?.Rules?.StandFromProneSpeedFraction ?? 0.5;
            if (fraction <= 0 || !c.IsActive) return;
            var feet = c.EffectiveSpeedFeet * fraction;
            if (feet <= 0) return;
            StandingUp?.Invoke(c, feet);
            _rollToChat?.Invoke(c.Name + " stands up, " + Math.Round(feet) + " ft of movement gone.", false);
        }

        private bool BreatheArea(CombatantViewModel attacker, CombatantAttackViewModel atk)
        {
            if (AreaCastArmed == null) return false;
            if (!DiceManager.TryRoll(atk.Damage, false, out var rolled) || rolled == null) return false;

            var res = new ActionResolution
            {
                Line = attacker.Name + " uses " + atk.Name,
                Kind = ActionOutcomeKind.Damage,
                HpDelta = -rolled.Total,
                DamageType = atk.DamageType,
                SaveAbility = atk.SaveAbility,
                SaveDc = atk.SaveDc,
                HalfOnSave = true
            };

            var area = new SpellAoe(atk.AreaShape, atk.AreaSizeFt, atk.AreaWidthFt, atk.SaveAbility, atk.SaveDc);
            AoeTemplateRequested?.Invoke(area);
            AreaCastArmed.Invoke(res, attacker, atk.Name, null, area);
            _rollToChat?.Invoke(res.Line + "  [place the area to see who it catches]", false);
            Initiative.NotifyStateChanged();
            return true;
        }

        private void RollAttack(CombatantViewModel attacker, CombatantAttackViewModel atk)
        {
            if (atk != null && atk.IsArea && BreatheArea(attacker, atk)) return;

            var target = AttackTarget;
            string line;
            int dealt = 0;
            bool resolvedHit = false;
            bool resolvedMiss = false;
            var typedRiders = new List<(string Type, int Amount)>();

            var range = RangeVerdict(attacker, atk, target);
            var sight = target != null ? (_lineCheck?.Invoke(attacker.Id, target.Id) ?? SightLine.Clear) : SightLine.Clear;
            if (sight == SightLine.Blocked)
            {
                LogLine(attacker.Name + " has no line to " + target!.Name + ", a wall is in the way.");
                return;
            }

            if (atk.AutoHit)
            {
                // No roll here means no advantage and no crit, but the riders that fire every hit, rage and the like, still ride an auto hit the same as a rolled one
                var ridden = target == null
                    ? (Extra: 0, Typed: new List<(string Type, int Amount)>(), Note: "")
                    : ApplyRiders(attacker, hadAdvantage: false, crit: false);
                typedRiders = ridden.Typed;
                dealt = SafeRollDamage(atk.Damage, false) + (App.PM?.Rules?.DamageBonusFromConditions(attacker.Conditions) ?? 0) + ridden.Extra;
                SpendOneShotFlags(attacker, target);
                resolvedHit = target != null;
                line = target == null
                    ? attacker.Name + ": " + atk.Name + " auto hits for " + dealt + " " + atk.DamageType + ridden.Note
                    : attacker.Name + " -> " + target.Name + ": " + atk.Name + " auto hits for " + dealt + " " + atk.DamageType + ridden.Note;
            }
            else
            {
                var d20 = RollAttackDie(attacker, target, App.PM?.Rules?.AttackDie ?? 20, out var hadAdvantage, range.LongRange);
                var natCrit = (App.PM?.Rules.IsCrit(d20) ?? d20 == 20);
                var fumble = (App.PM?.Rules.IsFumble(d20) ?? d20 == 1);
                var insp = target != null ? ConsumeInspiration(attacker) : (Bonus: 0, Note: "");
                var buffBonus = attacker.RollBuffBonusFor("attack-roll", out var buffNote) + attacker.ConditionalBonusFor("attack-roll");
                var total = d20 + atk.ToHit + insp.Bonus + buffBonus - (App.PM?.Rules?.Exhaustion?.D20Penalty(attacker.ExhaustionLevel) ?? 0);
                var condBonus = App.PM?.Rules?.DamageBonusFromConditions(attacker.Conditions) ?? 0;

                if (target == null)
                    line = attacker.Name + ": " + atk.Name + " rolls " + total + " (d20 " + d20 + Signed(atk.ToHit) + ")" + (natCrit ? " CRIT" : "") + ", " + (SafeRollDamage(atk.Damage, natCrit) + condBonus) + " " + atk.DamageType;
                else
                {
                    var autoCover = sight == SightLine.Cover ? (App.PM?.Rules?.CoverHalfBonus ?? 2) : 0;
                    var effAc = target.EffectiveArmorClass + Math.Max(target.CoverBonus, autoCover);
                    var coverTag = autoCover > 0 && autoCover >= target.CoverBonus ? " (cover)" : "";
                    var (hit, crit) = (App.PM?.Rules ?? new GameRules()).ResolveAttackOutcome(d20, total, effAc);
                    resolvedHit = hit;
                    resolvedMiss = !hit;
                    if (hit)
                    {
                        dealt = SafeRollDamage(atk.Damage, crit) + condBonus;
                        var ridden = ApplyRiders(attacker, hadAdvantage, crit);
                        typedRiders = ridden.Typed;
                        dealt += ridden.Extra;
                        line = attacker.Name + " -> " + target.Name + ": " + atk.Name + " HIT " + total + " vs AC " + effAc + coverTag + (crit ? " CRIT" : "") + ", " + dealt + " " + atk.DamageType + ridden.Note + buffNote;
                    }
                    else if (fumble) line = attacker.Name + " -> " + target.Name + ": " + atk.Name + " fumbles (d20 " + d20 + ")";
                    else line = attacker.Name + " -> " + target.Name + ": " + atk.Name + " MISS " + total + " vs AC " + effAc + coverTag + buffNote;
                    if (insp.Bonus > 0) line += insp.Note;
                }
            }

            if (target != null && (dealt > 0 || typedRiders.Count > 0))
            {
                var hpLostTotal = 0;
                if (dealt > 0)
                {
                    line += ApplyAttackDamage(target, dealt, atk.DamageType, out var hpBase);
                    hpLostTotal += hpBase;
                }
                foreach (var t in typedRiders)
                {
                    line += ApplyAttackDamage(target, t.Amount, t.Type, out var hpRider);
                    hpLostTotal += hpRider;
                }
                ConcentrationOnTarget(target, hpLostTotal);
                Initiative.NotifyStateChanged();
            }

            line += range.Note;
            line += ApplyMastery(attacker, atk, target, resolvedHit, resolvedMiss);
            _rollToChat?.Invoke(line, false);
        }

        // Vex, Sap and Hidden are one shot, they are spent by the roll they bend whether or not it lands.
        internal static void SpendOneShotFlags(CombatantViewModel attacker, CombatantViewModel? target)
        {
            if (target == null) return;
            if (attacker.HelpedTargetId == target.Id) attacker.HelpedTargetId = null;
            if (attacker.VexTargetId == target.Id) attacker.VexTargetId = null;
            attacker.Sapped = false;
            attacker.Hidden = false;
        }

        private int RollAttackDie(CombatantViewModel attacker, CombatantViewModel? target, int die) => RollAttackDie(attacker, target, die, out _);

        internal int RollAttackDie(CombatantViewModel attacker, CombatantViewModel? target, int die, out bool hadAdvantage) =>
            RollAttackDie(attacker, target, die, out hadAdvantage, false);

        internal bool CanSee(CombatantViewModel watcher, CombatantViewModel? subject)
        {
            if (subject == null || _lineCheck == null) return true;
            if (_lineCheck(watcher.Id, subject.Id) != SightLine.Blocked) return true;
            return SenseReaches(watcher, subject);
        }

        private bool SenseReaches(CombatantViewModel watcher, CombatantViewModel subject)
        {
            if (watcher.Senses.Count == 0) return false;
            var rules = App.PM?.Rules;
            if (rules == null) return false;
            var feet = _distanceCheck != null ? _distanceCheck(watcher.Id, subject.Id) * (rules.FeetPerSquare > 0 ? rules.FeetPerSquare : 5) : 0;
            foreach (var sense in rules.Senses)
            {
                if (string.Equals(sense.MatchId, "darkvision", StringComparison.OrdinalIgnoreCase)) continue;
                var carried = watcher.Senses.FirstOrDefault(s => s.Contains(sense.MatchId, StringComparison.OrdinalIgnoreCase));
                if (carried == null) continue;
                var m = Regex.Match(carried, @"(\d+)");
                var reach = m.Success ? double.Parse(m.Groups[1].Value) : sense.RangeFeet;
                if (reach <= 0 || feet <= reach) return true;
            }
            return false;
        }

        internal int RollAttackDie(CombatantViewModel attacker, CombatantViewModel? target, int die, out bool hadAdvantage, bool longRange)
        {
            var unseenTarget = target != null && !CanSee(attacker, target);
            var unseenAttacker = target != null && !CanSee(target, attacker);
            var flankAdv = AutoFlankingEnabled && (attacker.Flanking || (target != null && (_flankingCheck?.Invoke(attacker.Id, target.Id) ?? false)));
            var attackerMode = ConditionEffects.AttackMode(attacker.Conditions);
            var targetMode = target != null ? ConditionEffects.DefenderMode(target.Conditions, FeetTo(attacker, target)) : RollMode.Normal;
            var vexed = target != null && attacker.VexTargetId == target.Id;
            var helped = target != null && attacker.HelpedTargetId == target.Id;
            var advAlready = flankAdv || attacker.ManualAdvantage || attackerMode == RollMode.Advantage || targetMode == RollMode.Advantage || vexed || helped || attacker.Hidden || attacker.AdvantageOn.Contains("attack") || unseenAttacker;
            var adv = advAlready || (target != null && attacker.SpendInspiration());
            var dis = longRange || attacker.ManualDisadvantage || attackerMode == RollMode.Disadvantage || targetMode == RollMode.Disadvantage || attacker.Sapped || (target != null && target.Dodging)
                      || (App.PM?.Rules?.Exhaustion?.AttacksAtDisadvantage(attacker.ExhaustionLevel) ?? false) || unseenTarget;
            SpendOneShotFlags(attacker, target);

            hadAdvantage = adv && !dis;
            return DiceManager.RollCore(die, adv, dis);
        }

        private string ApplyAttackDamage(CombatantViewModel target, int dealt, string damageType, out int hpLost)
        {
            var scaled = target.ScaleDamageByType(dealt, damageType);
            var beforeTemp = target.TempHp;
            hpLost = target.TakeDamage(scaled);
            var absorbed = beforeTemp - target.TempHp;

            if (scaled == dealt && absorbed <= 0) return "";

            var parts = new List<string>();
            if (scaled == 0) parts.Add("immune");
            else if (scaled < dealt) parts.Add("resisted");
            else if (scaled > dealt) parts.Add("vulnerable");
            if (absorbed > 0) parts.Add(absorbed + " temp");
            return " [" + string.Join(", ", parts) + "]";
        }

        private static int FlatDamageBonus(string expr)
        {
            if (string.IsNullOrWhiteSpace(expr)) return 0;
            var m = Regex.Match(expr, @"([+-]\s*\d+)\s*$");
            if (!m.Success) return 0;
            return int.TryParse(m.Groups[1].Value.Replace(" ", ""), out var v) ? Math.Max(0, v) : 0;
        }

        private string ApplyMastery(CombatantViewModel attacker, CombatantAttackViewModel atk, CombatantViewModel? target, bool hit, bool miss)
        {
            if (target == null || string.IsNullOrWhiteSpace(atk.Mastery)) return "";
            var rules = App.PM?.Rules;
            if (rules == null || !rules.Masteries.TryGetValue(atk.Mastery, out var m)) return "";

            switch (m.Effect)
            {
                case "vex" when hit:
                    attacker.VexTargetId = target.Id;
                    return " [vex, advantage on the next swing at " + target.Name + "]";

                case "sap" when hit:
                    target.Sapped = true;
                    return " [sap, " + target.Name + " has disadvantage on its next attack]";

                case "slow" when hit && m.SpeedPenaltyFeet > 0 && target.SlowPenaltyFeet == 0:
                {
                    var penalty = Math.Min(m.SpeedPenaltyFeet, target.SpeedFeet);
                    if (penalty <= 0) return "";
                    target.SlowPenaltyFeet = penalty;
                    target.SlowSourceId = attacker.Id;
                    target.SpeedFeet -= penalty;
                    Initiative.NotifyStateChanged();
                    return " [slow, " + target.Name + " loses " + penalty + " ft of speed]";
                }

                case "graze" when miss:
                {
                    var flat = FlatDamageBonus(atk.Damage);
                    if (flat <= 0) return "";
                    var scaled = target.ScaleDamageByType(flat, atk.DamageType);
                    if (scaled <= 0) return " [graze, " + target.Name + " is immune]";
                    var lost = target.TakeDamage(scaled);
                    ConcentrationOnTarget(target, lost);
                    Initiative.NotifyStateChanged();
                    return " [graze, " + scaled + " " + atk.DamageType + " anyway]";
                }

                case "topple" when hit:
                {
                    if (string.IsNullOrWhiteSpace(m.Condition) || target.HasCondition(m.Condition)) return "";
                    var die = rules.AttackDie;
                    var dc = rules.ContestDcBase + atk.ToHit;
                    var roll = DiceManager.RollCore(die);
                    var bonus = InitiativeTrackerViewModel.SaveBonusFor(target, m.SaveAbility);
                    var total = roll + bonus;
                    if (rules.SaveSucceeds(total, dc)) return " [" + m.Name + ", " + target.Name + " saves " + total + " vs DC " + dc + "]";
                    target.Conditions.Add(m.Condition);
                    target.RaiseConditionsChanged();
                    Initiative.NotifyStateChanged();
                    return " [" + m.Name + ", " + target.Name + " fails " + total + " vs DC " + dc + " and is " + m.Condition + "]";
                }

                case "push" when hit:
                {
                    if (m.PushFeet <= 0 || _pushToken == null) return "";
                    var moved = _pushToken(attacker.Id, target.Id, m.PushFeet);
                    if (moved <= 0) return " [" + m.Name + ", " + target.Name + " has nowhere to go]";
                    Initiative.NotifyStateChanged();
                    var shown = (int)Math.Round(moved);
                    return shown >= m.PushFeet
                        ? " [" + m.Name + ", " + target.Name + " is shoved " + shown + " ft back]"
                        : " [" + m.Name + ", " + target.Name + " is shoved " + shown + " ft back and stops at a wall]";
                }

                case "cleave" when hit:
                    if (attacker.CleavedThisTurn) return "";
                    attacker.CleavedThisTurn = true;
                    attacker.BonusSwings++;
                    Initiative.NotifyStateChanged();
                    return " [" + m.Name + ", one more swing at a second creature within 5 ft of " + target.Name + ", and no ability modifier on that damage]";

                case "nick" when hit:
                    if (attacker.NickedThisTurn) return "";
                    attacker.NickedThisTurn = true;
                    attacker.BonusSwings++;
                    Initiative.NotifyStateChanged();
                    return " [" + m.Name + ", the light weapon's extra attack rides on the Attack action, one more swing this turn with no ability modifier on that damage]";
            }

            return "";
        }


        // A rider only lands when its When is satisfied, and once a turn means once a turn even across a multiattack, that one bit me twice
        internal static (int Extra, List<(string Type, int Amount)> Typed, string Note) ApplyRiders(CombatantViewModel attacker, bool hadAdvantage, bool crit)
        {
            var typed = new List<(string Type, int Amount)>();
            if (attacker.Riders.Count == 0) return (0, typed, "");
            var total = 0;
            var notes = new List<string>();
            foreach (var r in attacker.Riders)
            {
                if (r.OncePerTurn && attacker.RidersUsedThisTurn.Contains(r.Id)) continue;
                if (!RiderApplies(attacker, r, hadAdvantage)) continue;
                var expr = r.DiceForLevel(attacker.CharacterLevel);
                if (string.IsNullOrWhiteSpace(expr)) continue;
                if (!DiceManager.TryRoll(expr, crit, out var res) || res == null) continue;
                if (string.IsNullOrWhiteSpace(r.DamageType)) total += res.Total;
                else typed.Add((r.DamageType, res.Total));
                if (r.OncePerTurn) attacker.RidersUsedThisTurn.Add(r.Id);
                var label = string.IsNullOrWhiteSpace(r.Name) ? r.Id : r.Name;
                notes.Add(label + " " + expr + " -> " + res.Total + (string.IsNullOrWhiteSpace(r.DamageType) ? "" : " " + r.DamageType));
            }
            return (total, typed, notes.Count == 0 ? "" : " [" + string.Join(", ", notes) + "]");
        }

        private static bool RiderApplies(CombatantViewModel attacker, RiderRule r, bool hadAdvantage)
        {
            var when = (r.When ?? "always").ToLowerInvariant();
            if (when == "always") return true;
            if (when == "advantage") return hadAdvantage;
            return attacker.HasCondition(when);
        }

        internal static (int Bonus, string Note) ConsumeInspiration(CombatantViewModel attacker)
        {
            var die = attacker.InspirationDie;
            if (string.IsNullOrWhiteSpace(die)) return (0, "");
            attacker.InspirationDie = "";
            if (!DiceManager.TryRoll(die, false, out var res) || res == null) return (0, "");
            return (res.Total, " [inspiration " + die + " -> " + res.Total + "]");
        }

        // Soft warn only, the dm stays authoritative so an out of range shot still rolls, it just gets flagged in the log. I am the man from the island of snipers, and all that
        private (string Note, bool LongRange) RangeVerdict(CombatantViewModel attacker, CombatantAttackViewModel atk, CombatantViewModel? target)
        {
            if (target == null || _distanceCheck == null) return ("", false);
            var distCells = _distanceCheck(attacker.Id, target.Id);
            if (distCells < 0) return ("", false);
            var feetPerCell = App.PM?.Rules?.FeetPerSquare ?? 5.0;
            if (feetPerCell <= 0) return ("", false);
            var reachCells = atk.RangeFeet > 0 ? atk.RangeFeet / feetPerCell : (App.PM?.CombatMeleeReachCells ?? 1.5);
            if (distCells <= reachCells + 0.05) return ("", false);

            var distFeet = (int)Math.Round(distCells * feetPerCell);
            if (atk.RangeMaxFeet > atk.RangeFeet && distCells <= atk.RangeMaxFeet / feetPerCell + 0.05)
            {
                var atDisadvantage = App.PM?.Rules?.LongRangeDisadvantage ?? true;
                return (" [long range, " + distFeet + " ft vs " + atk.RangeFeet + " ft" + (atDisadvantage ? ", disadvantage]" : "]"), atDisadvantage);
            }

            var rangeFeet = atk.RangeMaxFeet > atk.RangeFeet ? atk.RangeMaxFeet
                : atk.RangeFeet > 0 ? atk.RangeFeet
                : (int)Math.Round(reachCells * feetPerCell);
            return (" [out of range, " + distFeet + " ft vs " + rangeFeet + " ft]", false);
        }

        private double FeetTo(CombatantViewModel attacker, CombatantViewModel? target)
        {
            if (target == null || _distanceCheck == null) return -1;
            var cells = _distanceCheck(attacker.Id, target.Id);
            if (cells < 0) return -1;
            return cells * (App.PM?.Rules?.FeetPerSquare ?? 5.0);
        }

        private static string Signed(int n) => n >= 0 ? "+" + n : n.ToString();

        public void ResolveOpportunityAttack(CombatantViewModel attacker, CombatantViewModel target)
        {
            if (attacker == null || target == null) return;
            if (!attacker.CanSpendReaction) { LogLine(attacker.Name + " has no reaction, no opportunity attack."); return; }
            if (!attacker.HasAttacks) { LogLine(attacker.Name + " has no attack for an opportunity attack."); return; }
            attacker.SpendReaction();
            var prev = AttackTarget;
            AttackTarget = target;
            LogLine(attacker.Name + " takes an opportunity attack on " + target.Name + " (reaction).");
            RollAttack(attacker, attacker.Attacks[0]);
            AttackTarget = prev;
            Initiative.NotifyStateChanged();
        }

        public string ApplyHazardTo(CombatantViewModel c, string hazardId, double magnitude = 0)
        {
            if (c == null) return "";
            var rules = App.PM?.Rules;
            if (rules == null || !rules.Hazards.TryGetValue(hazardId, out var rule)) return "";
            if (string.IsNullOrWhiteSpace(rule.Die)) return "";

            var expression = rule.Die;
            if (rule.PerFeet > 0)
            {
                var dice = (int)Math.Floor(magnitude / rule.PerFeet);
                if (dice <= 0) return "";
                if (rule.MaxDice > 0) dice = Math.Min(dice, rule.MaxDice);
                expression = dice.ToString() + rule.Die.TrimStart('d').Insert(0, "d");
            }

            if (!DiceManager.TryRoll(expression, false, out var rolled) || rolled == null) return "";

            var saved = false;
            if (!string.IsNullOrWhiteSpace(rule.SaveAbility) && rule.SaveDc > 0)
                saved = rules.SaveSucceeds(RollSave(c, rule.SaveAbility, rule.SaveDc), rule.SaveDc);

            var raw = rolled.Total;
            if (saved) raw = rule.HalfOnSave ? raw / Math.Max(1, rules.HalfOnSaveDivisor) : 0;

            var applied = c.ScaleDamageByType(raw, rule.DamageType);
            var lost = c.TakeDamage(applied);
            ConcentrationOnTarget(c, lost);

            if (!saved && !string.IsNullOrWhiteSpace(rule.Condition) && !c.HasCondition(rule.Condition))
                c.AddCondition(rule.Condition);

            var said = c.Name + " takes " + applied + " " + rule.DamageType + " from " + rule.Name
                       + (rule.PerFeet > 0 ? " (" + Math.Round(magnitude) + " ft)" : "")
                       + (saved ? " (saved)" : "") + "  [" + c.CurrentHp + "/" + c.MaxHp + "]";
            _rollToChat?.Invoke(said, false);
            Initiative.NotifyStateChanged();
            return said;
        }

        public void RollRechargesFor(CombatantViewModel c)
        {
            if (c == null) return;
            var die = App.PM?.Rules?.RechargeDie ?? "d6";
            foreach (var atk in c.Attacks)
            {
                if (!atk.NeedsRecharge || !atk.IsSpent) continue;
                if (!DiceManager.TryRoll(die, false, out var rolled) || rolled == null) continue;
                if (rolled.Total < atk.RechargeOn) continue;
                atk.IsSpent = false;
                _rollToChat?.Invoke(c.Name + " recharges " + atk.Name + " on a " + rolled.Total + ".", false);
            }
        }

        public void ResolveOngoingDamageFor(CombatantViewModel c, string trigger)
        {
            if (c == null || c.CurrentHp <= 0) return;
            var rules = App.PM?.Rules;
            if (rules == null) return;

            foreach (var name in c.Conditions.ToList())
            {
                if (!rules.Conditions.TryGetValue(name, out var rule)) continue;
                if (string.IsNullOrWhiteSpace(rule.DamageOverTime)) continue;
                if (!string.Equals(rule.DamageOverTimeAt, trigger, StringComparison.OrdinalIgnoreCase)) continue;
                if (!DiceManager.TryRoll(rule.DamageOverTime, false, out var rolled) || rolled == null) continue;

                var applied = c.ScaleDamageByType(rolled.Total, rule.DamageOverTimeType);
                var lost = c.TakeDamage(applied);
                ConcentrationOnTarget(c, lost);
                _rollToChat?.Invoke(c.Name + " takes " + applied + " " + rule.DamageOverTimeType + " from " + name
                                    + "  [" + c.CurrentHp + "/" + c.MaxHp + "]", false);

                if (rule.EndsOnSaveDc <= 0 || string.IsNullOrWhiteSpace(rule.EndsOnSaveAbility)) continue;
                var total = RollSave(c, rule.EndsOnSaveAbility, rule.EndsOnSaveDc);
                if (!rules.SaveSucceeds(total, rule.EndsOnSaveDc)) continue;
                c.RemoveCondition(name);
                _rollToChat?.Invoke(c.Name + " shakes off " + name + ".", false);
            }
            Initiative.NotifyStateChanged();
        }

        // A placed template that carries damage bites whoever is standing in it, on the trigger the template names.
        public void ResolveHazardsFor(CombatantViewModel c, string trigger, IEnumerable<AoeTemplateViewModel> templates, double pxPerFoot, double defaultLineFt, Func<string, (double X, double Y)?> whereIs)
        {
            if (c == null || c.CurrentHp <= 0) return;
            var at = whereIs?.Invoke(c.Id);
            if (at == null) return;

            foreach (var t in templates)
            {
                if (!t.IsHazard) continue;
                if (!string.IsNullOrWhiteSpace(t.Trigger) && !string.Equals(t.Trigger, trigger, StringComparison.OrdinalIgnoreCase)) continue;
                if (!t.Contains(at.Value.X, at.Value.Y, pxPerFoot, defaultLineFt)) continue;

                var label = string.IsNullOrWhiteSpace(t.Label) ? "the area" : t.Label;

                var saved = false;
                if (!string.IsNullOrWhiteSpace(t.SaveAbility) && t.SaveDc > 0)
                {
                    var total = RollSave(c, t.SaveAbility, t.SaveDc);
                    saved = (App.PM?.Rules ?? new GameRules()).SaveSucceeds(total, t.SaveDc);
                }

                var applied = 0;
                if (DiceManager.TryRoll(t.Damage, false, out var rolled) && rolled != null)
                {
                    var halfDiv = Math.Max(1, App.PM?.Rules?.HalfOnSaveDivisor ?? 2);
                    var before = saved ? rolled.Total / halfDiv : rolled.Total;
                    applied = c.ScaleDamageByType(before, t.DamageType);
                    var lost = c.TakeDamage(applied);
                    ConcentrationOnTarget(c, lost);
                }

                var pinned = "";
                if (!saved && !string.IsNullOrWhiteSpace(t.Condition) && !c.HasCondition(t.Condition))
                {
                    c.AddCondition(t.Condition, t.ConditionRounds, "", t.Id);
                    pinned = t.Condition;
                }

                if (applied <= 0 && pinned.Length == 0) continue;

                var said = c.Name + " is caught in " + label;
                if (applied > 0) said += " for " + applied + " " + t.DamageType;
                if (pinned.Length > 0) said += (applied > 0 ? " and is " : " and is now ") + pinned;
                _rollToChat?.Invoke(said + (saved ? " (saved)" : "") + "  [" + c.CurrentHp + "/" + c.MaxHp + "]", false);
            }
            Initiative.NotifyStateChanged();
        }

        // One door for everything a spell does that is not damage, so the two cast paths cannot drift.
        private string ApplySpellCondition(ActionResolution res, CombatantViewModel? target, bool saved, string spellLabel = "spell", CombatantViewModel? concentratingCaster = null)
        {
            if (target == null) return "";

            var sourceId = concentratingCaster?.Id ?? "";
            var note = "";
            if (!string.IsNullOrWhiteSpace(res.Buff) && (res.BuffValue != 0 || !string.IsNullOrWhiteSpace(res.BuffDice)))
            {
                target.AddTimedBuff(res.Buff!, res.BuffValue, res.BuffRounds, res.BuffExpiresAt ?? "", spellLabel, res.BuffDice ?? "", sourceId);
                var amount = string.IsNullOrWhiteSpace(res.BuffDice) ? (res.BuffValue >= 0 ? "+" : "") + res.BuffValue : res.BuffDice!;
                note += " [" + target.Name + " " + amount + " " + res.Buff
                        + (res.BuffRounds > 0 ? " for " + res.BuffRounds + " rounds" : "") + "]";
            }

            if (string.IsNullOrWhiteSpace(res.Condition)) { if (note.Length > 0) Initiative.NotifyStateChanged(); return note; }
            if (saved && !res.ConditionOnSaveToo) return note + " [" + target.Name + " shrugs off " + res.Condition + "]";
            target.AddCondition(res.Condition!, res.ConditionRounds, res.ConditionExpiresAt ?? "");
            if (concentratingCaster != null
                && !concentratingCaster.ConcentrationEffects.Any(l => l.TargetId == target.Id && string.Equals(l.Condition, res.Condition, StringComparison.OrdinalIgnoreCase)))
            {
                concentratingCaster.ConcentrationEffects.Add(new ConcentrationLink(target.Id, res.Condition!));
                concentratingCaster.RaiseConcentrationEffectsChanged();
            }
            Initiative.NotifyStateChanged();
            return note + " [" + target.Name + " is " + res.Condition + (res.ConditionRounds > 0 ? " for " + res.ConditionRounds + " rounds" : "") + "]";
        }

        private bool RollSpellSave(ActionResolution res, CombatantViewModel target)
        {
            if (string.IsNullOrEmpty(res.SaveAbility)) return false;
            var total = RollSave(target, res.SaveAbility!, res.SaveDc);
            return (App.PM?.Rules ?? new GameRules()).SaveSucceeds(total, res.SaveDc);
        }

        public int RollSave(CombatantViewModel c, string ability, int dc)
        {
            var rules = App.PM?.Rules ?? new GameRules();
            var die = rules.AttackDie;
            var mode = rules.SaveRollModeFrom(c.Conditions);
            var adv = c.AdvantageOn.Contains("save:" + ability) || mode == "advantage" || c.SpendInspiration();
            var dis = mode == "disadvantage" || (rules.Exhaustion?.SavesAtDisadvantage(c.ExhaustionLevel) ?? false);
            var roll = DiceManager.RollCore(die, adv, dis);
            var bonus = c.SaveBonusFor(ability) + c.RollBuffBonusFor("saving-throw", out var saveBuffNote) + c.ConditionalBonusFor("saving-throw")
                        - (rules.Exhaustion?.D20Penalty(c.ExhaustionLevel) ?? 0);
            var total = roll + bonus;
            var held = rules.SaveSucceeds(total, dc);
            var advTag = adv && !dis ? " (advantage)" : dis && !adv ? " (disadvantage)" : "";
            _rollToChat?.Invoke(c.Name + " " + ability + " save (DC " + dc + ")" + advTag + ": d" + die + " [" + roll + "]" + Signed(bonus) + " = " + total + ", " + (held ? "success" : "fail") + "." + saveBuffNote, false);
            return total;
        }

        private async Task WarmFlankingSettingAsync()
        {
            await App.PM!.GetCombatSettingsAsync();
            this.RaisePropertyChanged(nameof(AutoFlankingEnabled));
        }

        // The standard set plus whatever the template flagged with an attack effect, so a homebrew condition that swings a roll still shows up as a chip
        private static IEnumerable<string> BuildConditionOptions()
        {
            var r = App.PM?.Rules;
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (r != null && r.Conditions.Count > 0)
                foreach (var c in r.Conditions.Values) { if (c.Trackable) set.Add(c.Name); }
            else
                foreach (var c in new[] { "blinded", "charmed", "deafened", "frightened", "grappled", "incapacitated",
                                          "invisible", "paralyzed", "petrified", "poisoned", "prone", "restrained", "stunned", "unconscious" })
                    set.Add(c);

            if (r != null)
            {
                foreach (var x in r.AttackAdvantageConditions) set.Add(x);
                foreach (var x in r.AttackDisadvantageConditions) set.Add(x);
                foreach (var x in r.TargetAdvantageConditions) set.Add(x);
                foreach (var x in r.TargetDisadvantageConditions) set.Add(x);
                foreach (var x in r.IncapacitatingConditions) set.Add(x);
                foreach (var x in r.MovementStoppingConditions) set.Add(x);
                foreach (var a in r.TacticalActions.Values) if (!string.IsNullOrWhiteSpace(a.Condition)) set.Add(a.Condition);
                foreach (var m in r.Masteries.Values) if (!string.IsNullOrWhiteSpace(m.Condition)) set.Add(m.Condition);
            }
            return set;
        }

        private static int SafeRollDamage(string expr, bool crit)
        {
            try { return Math.Max(0, DiceManager.Roll(expr, crit).Total); }
            catch { return 0; }
        }

        public void AddCombatant(CombatantViewModel combatant)
        {
            if (combatant == null) return;
            if (Initiative.Combatants.Any(c => c.Id == combatant.Id))
            {
                LogLine(combatant.Name + " is already in this fight, so nothing was added.");
                return;
            }
            Initiative.Combatants.Add(combatant);
            Initiative.NotifyStateChanged();
        }

        public string WhyNobodyToAdd(bool players) =>
            Initiative.Combatants.Count > 0
                ? "Everyone is already in this fight, anyone in initiative is left out of this list. There are " + Initiative.Combatants.Count + " in it right now."
                : players
                    ? "No player characters in this campaign yet, build one on the Characters page first."
                    : "No npcs in this campaign yet, make one in the Codex first.";

        public void RaisePlayerPulledIn(CombatantViewModel combatant, PlayerOptionViewModel option) => PlayerPulledIn?.Invoke(combatant, option);

        public void RaiseEncounterChosen(EncounterPreset preset) => EncounterChosen?.Invoke(preset);

        public async Task<List<EncounterPresetRowViewModel>> LoadSavedEncountersAsync()
        {
            var result = new List<EncounterPresetRowViewModel>();
            if (App.PM == null) return result;
            var saved = await App.PM.LoadEncounterPresetsAsync();
            foreach (var p in saved) result.Add(new EncounterPresetRowViewModel(p));
            return result;
        }

        public void LogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            CombatLog.Insert(0, line);
            while (CombatLog.Count > 50) CombatLog.RemoveAt(CombatLog.Count - 1);
        }

        public async Task<List<PlayerOptionViewModel>> LoadAvailablePlayersAsync()
        {
            var result = new List<PlayerOptionViewModel>();
            if (App.PM == null) return result;

            var characters = await App.PM.LoadAllCharactersInCampaignAsync();
            if (characters == null) return result;

            var onlineUserIds = App.PM.ComController?.OnlineUserIds
                ?.ToHashSet() ?? new HashSet<string>();

            foreach (var runtime in characters)
            {
                if (!string.Equals(runtime.CharacterKind, "pc", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (Initiative.Combatants.Any(c => c.Id == runtime.Id))
                    continue;

                var owner = App.PM.ComController?.Members
                    ?.FirstOrDefault(m => m.UserId == runtime.OwnerUserId);
                var ownerName = owner?.Username ?? "Unassigned";
                var isOnline = !string.IsNullOrEmpty(runtime.OwnerUserId)
                               && onlineUserIds.Contains(runtime.OwnerUserId);

                result.Add(new PlayerOptionViewModel(
                    characterId: runtime.Id,
                    characterName: runtime.Name,
                    playerName: ownerName,
                    level: runtime.Level,
                    currentHp: runtime.CurrentHp,
                    maxHp: runtime.MaxHp,
                    race: runtime.RaceId ?? "-",
                    @class: runtime.ClassId ?? "-")
                {
                    IsOnline = isOnline,
                    Concentration = runtime.Concentration,
                    ColorHex = runtime.ColorHex,
                    TokenImagePath = runtime.TokenImagePath,
                    InitiativeMod = RuntimeAbilityMod(runtime, App.PM.Rules.InitiativeAbility),
                    ConSaveBonus = RuntimeSaveBonus(runtime, App.PM.Rules.ConcentrationAbility),
                    ArmorClass = runtime.ArmorClass
                });
            }
            return result;
        }

        public CombatantViewModel BuildCombatantFromPlayer(PlayerOptionViewModel option, int initiativeRoll)
        {
            var c = new CombatantViewModel(option.CharacterId, option.CharacterName, isPlayerCharacter: true)
            {
                MaxHp = option.MaxHp,
                CurrentHp = option.CurrentHp,
                ArmorClass = option.ArmorClass,
                Initiative = initiativeRoll,
                DexMod = option.InitiativeMod,
                ConSaveBonus = option.ConSaveBonus,
                RevealExactHpToPlayers = true,
                Concentration = option.Concentration,
                Portrait = CharacterTokenRenderer.Resolve(option.CharacterName, option.ColorHex, option.TokenImagePath)
            };
            return c;
        }

        public async Task<List<PlayerOptionViewModel>> LoadAvailableNpcsAsync()
        {
            var result = new List<PlayerOptionViewModel>();
            if (App.PM == null) return result;

            var characters = await App.PM.LoadAllCharactersInCampaignAsync();
            if (characters == null) return result;

            foreach (var runtime in characters)
            {
                if (!string.Equals(runtime.CharacterKind, "npc", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Initiative.Combatants.Any(c => c.Id == runtime.Id))
                    continue;

                result.Add(new PlayerOptionViewModel(
                    characterId: runtime.Id,
                    characterName: runtime.Name,
                    playerName: "NPC",
                    level: runtime.Level,
                    currentHp: runtime.CurrentHp,
                    maxHp: runtime.MaxHp,
                    race: runtime.RaceId ?? "-",
                    @class: runtime.ClassId ?? "-")
                {
                    InitiativeMod = RuntimeAbilityMod(runtime, App.PM.Rules.InitiativeAbility),
                    ConSaveBonus = RuntimeSaveBonus(runtime, App.PM.Rules.ConcentrationAbility),
                    ColorHex = runtime.ColorHex,
                    TokenImagePath = runtime.TokenImagePath
                });
            }

            foreach (var monster in await LoadMonsterOptionsAsync())
                result.Add(new PlayerOptionViewModel(
                    characterId: "monster:" + monster.Id,
                    characterName: monster.Name,
                    playerName: "Monster",
                    level: 0,
                    currentHp: monster.HitPoints,
                    maxHp: monster.HitPoints,
                    race: string.IsNullOrWhiteSpace(monster.ChallengeRating) ? monster.Size : "CR " + monster.ChallengeRating,
                    @class: "-")
                {
                    Monster = monster,
                    InitiativeMod = monster.DexMod,
                    ArmorClass = monster.ArmorClass,
                    ColorHex = monster.DefaultColor
                });

            return result;
        }

        private async Task<List<MonsterOption>> LoadMonsterOptionsAsync()
        {
            if (App.PM?.DbManager == null) return new List<MonsterOption>();
            try
            {
                var reader = new MonsterCatalogReader(App.PM.DbManager);
                return await reader.ReadAsync(App.PM.GetActiveTemplateId());
            }
            catch (Exception ex)
            {
                ErrorLog.Log("Could not read the monster catalog for the add list", ex);
                return new List<MonsterOption>();
            }
        }

        private string FreeCombatantName(string baseName)
        {
            var name = string.IsNullOrWhiteSpace(baseName) ? "Monster" : baseName.Trim();
            if (!Initiative.Combatants.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))) return name;
            for (int n = 2; n < 500; n++)
            {
                var tried = name + " " + n;
                if (!Initiative.Combatants.Any(c => string.Equals(c.Name, tried, StringComparison.OrdinalIgnoreCase))) return tried;
            }
            return name + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        public CombatantViewModel BuildCombatantFromMonster(MonsterOption monster, int initiativeRoll)
        {
            var combatant = new CombatantViewModel(Guid.NewGuid().ToString("N"), FreeCombatantName(monster.Name), isPlayerCharacter: false)
            {
                Initiative = initiativeRoll,
                MaxHp = monster.HitPoints,
                CurrentHp = monster.HitPoints,
                ArmorClass = monster.ArmorClass,
                DexMod = monster.DexMod,
                RevealExactHpToPlayers = false
            };
            combatant.SpeedFeet = monster.Speed > 0 ? monster.Speed : (App.PM?.CombatBaseSpeedFeet ?? 30);
            combatant.AttacksPerAction = monster.AttacksPerAction;
            combatant.LegendaryPerRound = monster.LegendaryPerRound;
            combatant.LegendaryRemaining = monster.LegendaryPerRound;
            foreach (var la in monster.LegendaryActions) combatant.LegendaryActions.Add(la);
            combatant.LairInitiative = monster.LairInitiative;
            foreach (var la in monster.LairActions) combatant.LairActions.Add(la);
            combatant.Resistances.AddRange(monster.Resistances);
            combatant.Immunities.AddRange(monster.Immunities);
            combatant.Vulnerabilities.AddRange(monster.Vulnerabilities);
            foreach (var kv in monster.Saves) combatant.SetSave(kv.Key, kv.Value);
            combatant.ConSaveBonus = combatant.SaveBonusFor(App.PM?.Rules?.ConcentrationAbility ?? "con");
            foreach (var a in monster.Attacks)
                combatant.Attacks.Add(new CombatantAttackViewModel(a.Name, a.ToHit, a.Damage, a.DamageType, a.RangeFeet, "", 0,
                    a.AreaShape, a.AreaSizeFt, a.AreaWidthFt, a.SaveAbility, a.SaveDc));
            return combatant;
        }

        public CombatantViewModel BuildCombatantFromNpc(PlayerOptionViewModel option, int initiativeRoll)
        {
            return new CombatantViewModel(option.CharacterId, option.CharacterName, isPlayerCharacter: false)
            {
                MaxHp = option.MaxHp,
                CurrentHp = option.CurrentHp,
                Initiative = initiativeRoll,
                DexMod = option.InitiativeMod,
                RevealExactHpToPlayers = false,
                Portrait = CharacterTokenRenderer.Resolve(option.CharacterName, option.ColorHex, option.TokenImagePath)
            };
        }
    }
}
