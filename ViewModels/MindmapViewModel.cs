using Avalonia.Media;
using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Dujahit.Models.Communication;

namespace Dujahit.ViewModels
{
    public class MindmapViewModel : ViewModelBase
    {
        private static readonly string[] _palette =
        {
            "#4F81BD", "#C0504D", "#9BBB59", "#8064A2", "#4BACC6", "#F79646", "#7F6084", "#C8843C"
        };

        public static readonly (string Kind, string Label, string Body)[] Templates =
        {
            ("npc", "NPC", "Role: \nWorks at: \nAge: \nAlignment: \nGoal: \nNotes: "),
            ("faction", "Organization", "Type: \nLeader: \nGoal: \nAllies: \nEnemies: \nNotes: "),
            ("event", "Event", "When: \nWhere: \nWhat happened: \nConsequences: "),
            ("place", "Place", "Type: \nWho lives here: \nWhat is notable: "),
            ("blank", "Note", "")
        };

        public ObservableCollection<MindmapListItem> Maps { get; } = new();
        public ObservableCollection<MindNodeViewModel> Nodes { get; } = new();
        public ObservableCollection<MindLinkViewModel> Links { get; } = new();
        public ObservableCollection<MindSwatch> ColorSwatches { get; } = new(_palette.Select(h => new MindSwatch(h)));
        public ObservableCollection<MindShareTarget> ShareTargets { get; } = new();
        public ObservableCollection<MindTemplateOption> NodeTemplates { get; } = new(Templates.Select(t => new MindTemplateOption(t.Kind, t.Label)));

        public Func<(double X, double Y)>? SpawnPointProvider;

        private MindmapListItem? _selectedMap;
        public MindmapListItem? SelectedMap
        {
            get => _selectedMap;
            set
            {
                if (_selectedMap == value) return;
                this.RaiseAndSetIfChanged(ref _selectedMap, value);
                this.RaisePropertyChanged(nameof(HasSelectedMap));
                this.RaisePropertyChanged(nameof(SelectedMapIsShared));
                this.RaisePropertyChanged(nameof(ShowEmptyHint));
                _ = LoadMapContentAsync();
                RefreshShareTargets();
            }
        }
        public bool HasSelectedMap => _selectedMap != null;
        public bool SelectedMapIsShared => _selectedMap?.Scope == "shared";

        private bool _isMapOpen;
        public bool IsMapOpen { get => _isMapOpen; set => this.RaiseAndSetIfChanged(ref _isMapOpen, value); }

        public ObservableCollection<MindmapListItem> PrivateMaps { get; } = new();
        public ObservableCollection<MindmapListItem> SharedMaps { get; } = new();
        public bool HasPrivateMaps => PrivateMaps.Count > 0;
        public bool HasSharedMaps => SharedMaps.Count > 0;

        private void RefreshBuckets()
        {
            PrivateMaps.Clear();
            SharedMaps.Clear();
            var q = _newMapTitle?.Trim();
            IEnumerable<MindmapListItem> maps = Maps;
            if (!string.IsNullOrEmpty(q))
                maps = maps.Where(m => !string.IsNullOrEmpty(m.Title) && m.Title.Contains(q, StringComparison.OrdinalIgnoreCase));
            foreach (var m in maps)
                (m.Scope == "shared" ? SharedMaps : PrivateMaps).Add(m);
            this.RaisePropertyChanged(nameof(HasPrivateMaps));
            this.RaisePropertyChanged(nameof(HasSharedMaps));
        }

        private MindNodeViewModel? _selectedNode;
        public MindNodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode != null) _selectedNode.IsSelected = false;
                this.RaiseAndSetIfChanged(ref _selectedNode, value);
                if (_selectedNode != null) { _selectedNode.IsSelected = true; SelectedLink = null; }
                this.RaisePropertyChanged(nameof(HasSelectedNode));
            }
        }
        public bool HasSelectedNode => _selectedNode != null;

        private MindLinkViewModel? _selectedLink;
        public MindLinkViewModel? SelectedLink
        {
            get => _selectedLink;
            set
            {
                if (_selectedLink != null) _selectedLink.IsSelected = false;
                this.RaiseAndSetIfChanged(ref _selectedLink, value);
                if (_selectedLink != null) _selectedLink.IsSelected = true;
                this.RaisePropertyChanged(nameof(HasSelectedLink));
            }
        }
        public bool HasSelectedLink => _selectedLink != null;

        private bool _linkMode;
        public bool LinkMode { get => _linkMode; set => this.RaiseAndSetIfChanged(ref _linkMode, value); }

        private string _newMapTitle = "";
        public string NewMapTitle { get => _newMapTitle; set { this.RaiseAndSetIfChanged(ref _newMapTitle, value); RefreshBuckets(); } }

        private bool _newMapShared;
        public bool NewMapShared { get => _newMapShared; set => this.RaiseAndSetIfChanged(ref _newMapShared, value); }

        private MindTemplateOption? _pickedTemplate;
        public MindTemplateOption? PickedTemplate { get => _pickedTemplate; set => this.RaiseAndSetIfChanged(ref _pickedTemplate, value); }

        public bool IsEmpty => Nodes.Count == 0;
        public bool ShowEmptyHint => HasSelectedMap && Nodes.Count == 0;

        public ReactiveCommand<Unit, Unit> CreateMapCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteMapCommand { get; }
        public ReactiveCommand<Unit, Unit> RenameMapCommand { get; }
        public event Func<string, string, Task<bool>>? ConfirmAsync;
        public ReactiveCommand<Unit, Unit> AddNodeCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveNodeCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteNodeCommand { get; }
        public ReactiveCommand<string, Unit> SetNodeColorCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteLinkCommand { get; }
        public ReactiveCommand<string, Unit> SetLinkRelationCommand { get; }
        public ReactiveCommand<MindShareTarget, Unit> ToggleShareCommand { get; }
        public ReactiveCommand<MindmapListItem, Unit> OpenMapCommand { get; }
        public ReactiveCommand<Unit, Unit> CloseMapCommand { get; }

        public MindmapViewModel()
        {
            CreateMapCommand = ReactiveCommand.CreateFromTask(CreateMapAsync);
            DeleteMapCommand = ReactiveCommand.CreateFromTask(DeleteMapAsync);
            RenameMapCommand = ReactiveCommand.CreateFromTask(RenameMapAsync);
            AddNodeCommand = ReactiveCommand.CreateFromTask(AddNodeAsync);
            SaveNodeCommand = ReactiveCommand.CreateFromTask(SaveSelectedNodeAsync);
            DeleteNodeCommand = ReactiveCommand.CreateFromTask(DeleteSelectedNodeAsync);
            SetNodeColorCommand = ReactiveCommand.Create<string>(hex => { if (SelectedNode != null) { SelectedNode.ColorHex = hex; _ = SaveSelectedNodeAsync(); } });
            DeleteLinkCommand = ReactiveCommand.CreateFromTask(DeleteSelectedLinkAsync);
            SetLinkRelationCommand = ReactiveCommand.Create<string>(rel => { if (SelectedLink != null) { SelectedLink.RelationType = string.Equals(SelectedLink.RelationType, rel, StringComparison.OrdinalIgnoreCase) ? "" : rel ?? ""; _ = SaveSelectedLinkAsync(); } });
            ToggleShareCommand = ReactiveCommand.CreateFromTask<MindShareTarget>(ToggleShareAsync);
            OpenMapCommand = ReactiveCommand.Create<MindmapListItem>(m => { if (m != null) { SelectedMap = m; IsMapOpen = true; } });
            CloseMapCommand = ReactiveCommand.Create(() => { IsMapOpen = false; });
            PickedTemplate = NodeTemplates.FirstOrDefault();
        }

        public async Task LoadAsync()
        {
            if (App.PM == null) return;
            var maps = await App.PM.MindmapRepo.ListVisibleMapsAsync(App.PM.GetCampaignId(), App.PM.GetUID());
            var uid = App.PM.GetUID();
            var priorId = SelectedMap?.Id;
            Maps.Clear();
            foreach (var m in maps) Maps.Add(new MindmapListItem(m, m.OwnerUserId == uid));
            RefreshBuckets();
            var reselect = priorId != null ? Maps.FirstOrDefault(m => m.Id == priorId) : null;
            if (reselect != null) SelectedMap = reselect;
            else if (!IsMapOpen) SelectedMap = null;
        }

        public async Task OpenMapByIdAsync(string mapId)
        {
            if (string.IsNullOrEmpty(mapId)) return;
            var mapItem = Maps.FirstOrDefault(m => m.Id == mapId);
            if (mapItem == null) return;
            if (SelectedMap?.Id != mapItem.Id) SelectedMap = mapItem;
            IsMapOpen = true;
            await LoadMapContentAsync();
        }

        public async Task SelectNodeBySlugAsync(string slug)
        {
            if (App.PM == null || string.IsNullOrEmpty(slug)) return;
            var node = await App.PM.MindmapRepo.GetNodeBySlugAsync(App.PM.GetCampaignId(), slug);
            if (node == null) return;
            var mapItem = Maps.FirstOrDefault(m => m.Id == node.MindmapId);
            if (mapItem == null) return;
            if (SelectedMap?.Id != mapItem.Id) SelectedMap = mapItem;
            IsMapOpen = true;
            await LoadMapContentAsync();
            var vm = Nodes.FirstOrDefault(n => n.Id == node.Id);
            if (vm != null) SelectedNode = vm;
        }

        private CancellationTokenSource? _loadCts;

        public bool Interacting { get; set; }

        private async Task LoadMapContentAsync()
        {
            // Clicking through maps fast used to leave the slow one to win.
            _loadCts?.Cancel();
            _loadCts?.Dispose();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            Nodes.Clear();
            Links.Clear();
            SelectedNode = null;
            SelectedLink = null;
            if (App.PM == null || SelectedMap == null) { this.RaisePropertyChanged(nameof(IsEmpty)); this.RaisePropertyChanged(nameof(ShowEmptyHint)); return; }

            List<MindmapNode> nodes;
            List<MindmapLink> links;
            try
            {
                nodes = await App.PM.MindmapRepo.LoadNodesAsync(SelectedMap.Id, ct);
                links = await App.PM.MindmapRepo.LoadLinksAsync(SelectedMap.Id, ct);
            }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            foreach (var n in nodes) Nodes.Add(MindNodeViewModel.FromModel(n));
            foreach (var l in links) Links.Add(MindLinkViewModel.FromModel(l));
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(ShowEmptyHint));
        }

        private async Task CreateMapAsync()
        {
            if (App.PM == null) return;
            var m = new Mindmap
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                OwnerUserId = App.PM.GetUID(),
                Scope = NewMapShared ? "shared" : "private",
                Title = string.IsNullOrWhiteSpace(NewMapTitle) ? "New mindmap" : NewMapTitle.Trim(),
                ColorHex = _palette[Maps.Count % _palette.Length]
            };
            await App.PM.MindmapRepo.SaveMapAsync(m);
            var item = new MindmapListItem(m, true);
            Maps.Add(item);
            RefreshBuckets();
            NewMapTitle = "";
            NewMapShared = false;
            SelectedMap = item;
            IsMapOpen = true;
        }

        private async Task DeleteMapAsync()
        {
            if (App.PM == null || SelectedMap == null || !SelectedMap.IsOwner) return;
            if (ConfirmAsync != null && !await ConfirmAsync("Delete mindmap", $"Delete mindmap \"{SelectedMap.Title}\"?\n\nEvery node and connection on it goes with it, this cannot be undone.")) return;
            var id = SelectedMap.Id;
            await App.PM.MindmapRepo.DeleteMapAsync(id);
            var gone = Maps.FirstOrDefault(m => m.Id == id);
            if (gone != null) Maps.Remove(gone);
            RefreshBuckets();
            SelectedMap = null;
            IsMapOpen = false;
        }

        private async Task RenameMapAsync()
        {
            if (App.PM == null || SelectedMap == null || !SelectedMap.IsOwner) return;
            var map = await App.PM.MindmapRepo.GetMapAsync(SelectedMap.Id);
            if (map == null) return;
            map.Title = string.IsNullOrWhiteSpace(SelectedMap.Title) ? "Mindmap" : SelectedMap.Title.Trim();
            map.UpdatedAt = DateTime.UtcNow;
            await App.PM.MindmapRepo.SaveMapAsync(map);
            MaybePush();
        }

        private async Task AddNodeAsync()
        {
            if (App.PM == null || SelectedMap == null) return;
            var tmpl = PickedTemplate ?? NodeTemplates.First();
            var body = Templates.FirstOrDefault(t => t.Kind == tmpl.Kind).Body ?? "";
            var (sx, sy) = SpawnPointProvider?.Invoke() ?? (0.0, 0.0);
            var spread = (Nodes.Count % 6) * 24.0;
            var node = new MindNodeViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                MindmapId = SelectedMap.Id,
                CampaignId = App.PM.GetCampaignId(),
                Kind = tmpl.Kind,
                Title = tmpl.Label + " " + (Nodes.Count + 1),
                Body = body,
                ColorHex = _palette[Nodes.Count % _palette.Length],
                X = sx + spread,
                Y = sy + spread
            };
            node.Slug = SlugFor(node.Title, node.Id);
            Nodes.Add(node);
            SelectedNode = node;
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(ShowEmptyHint));
            await App.PM.MindmapRepo.SaveNodeAsync(node.ToModel());
            _ = PushNodeUpsertAsync(node);
        }

        public async Task PersistNodePositionAsync(MindNodeViewModel node)
        {
            if (App.PM == null) return;
            await App.PM.MindmapRepo.UpdateNodePositionAsync(node.Id, node.X, node.Y);
            _ = PushNodeMoveAsync(node);
        }

        private Task SaveSelectedNodeAsync() => SelectedNode != null ? SaveNodeAsync(SelectedNode) : Task.CompletedTask;
        private Task DeleteSelectedNodeAsync() => SelectedNode != null ? DeleteNodeAsync(SelectedNode) : Task.CompletedTask;

        public async Task SaveNodeAsync(MindNodeViewModel node)
        {
            if (App.PM == null || node == null) return;
            if (string.IsNullOrEmpty(node.Slug)) node.Slug = SlugFor(node.Title, node.Id);
            await App.PM.MindmapRepo.SaveNodeAsync(node.ToModel());
            _ = PushNodeUpsertAsync(node);
        }

        public async Task SetNodeColorAsync(MindNodeViewModel node, string hex)
        {
            if (node == null) return;
            node.ColorHex = hex;
            await SaveNodeAsync(node);
        }

        public async Task DeleteNodeAsync(MindNodeViewModel node)
        {
            if (App.PM == null || node == null) return;
            var id = node.Id;
            for (int i = Links.Count - 1; i >= 0; i--)
                if (Links[i].FromNodeId == id || Links[i].ToNodeId == id) Links.RemoveAt(i);
            Nodes.Remove(node);
            if (SelectedNode == node) SelectedNode = null;
            await App.PM.MindmapRepo.DeleteNodeAsync(id);
            _ = PushNodeDeleteAsync(node.MindmapId, id);
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(ShowEmptyHint));
        }

        public async Task CreateLinkAsync(MindNodeViewModel from, MindNodeViewModel to)
        {
            if (App.PM == null || SelectedMap == null) return;
            if (from.Id == to.Id) return;
            if (Links.Any(l => (l.FromNodeId == from.Id && l.ToNodeId == to.Id) || (l.FromNodeId == to.Id && l.ToNodeId == from.Id))) return;
            var link = new MindLinkViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                MindmapId = SelectedMap.Id,
                CampaignId = App.PM.GetCampaignId(),
                FromNodeId = from.Id,
                ToNodeId = to.Id
            };
            Links.Add(link);
            SelectedLink = link;
            await App.PM.MindmapRepo.SaveLinkAsync(link.ToModel());
            _ = PushLinkUpsertAsync(link);
        }

        private async Task SaveSelectedLinkAsync()
        {
            if (App.PM == null || SelectedLink == null) return;
            await App.PM.MindmapRepo.SaveLinkAsync(SelectedLink.ToModel());
            _ = PushLinkUpsertAsync(SelectedLink);
        }

        private async Task DeleteSelectedLinkAsync()
        {
            if (App.PM == null || SelectedLink == null) return;
            var id = SelectedLink.Id;
            var mapId = SelectedLink.MindmapId;
            Links.Remove(SelectedLink);
            SelectedLink = null;
            await App.PM.MindmapRepo.DeleteLinkAsync(id);
            _ = PushLinkDeleteAsync(mapId, id);
        }

        private CancellationTokenSource? _shareCts;

        private void RefreshShareTargets()
        {
            ShareTargets.Clear();
            if (App.PM == null || SelectedMap == null || !SelectedMap.IsOwner) return;
            _ = LoadSharesAsync(App.PM.ComController, App.PM.GetUID());
        }

        private async Task LoadSharesAsync(CommunicationController com, string me)
        {
            if (App.PM == null || SelectedMap == null) return;
            _shareCts?.Cancel();
            _shareCts?.Dispose();
            _shareCts = new CancellationTokenSource();
            var ct = _shareCts.Token;

            var mapId = SelectedMap.Id;
            HashSet<string> shared;
            try { shared = new HashSet<string>(await App.PM.MindmapRepo.ListShareUserIdsAsync(mapId, ct), StringComparer.OrdinalIgnoreCase); }
            catch (OperationCanceledException) { return; }
            if (ct.IsCancellationRequested) return;
            ShareTargets.Clear();
            foreach (var m in com.Members.Where(m => !string.Equals(m.UserId, me, StringComparison.OrdinalIgnoreCase)))
                ShareTargets.Add(new MindShareTarget(m.UserId, string.IsNullOrWhiteSpace(m.Username) ? m.UserId : m.Username!, shared.Contains(m.UserId)));
        }

        private async Task ToggleShareAsync(MindShareTarget target)
        {
            if (App.PM == null || SelectedMap == null || target == null) return;
            var mapId = SelectedMap.Id;
            if (target.IsShared)
            {
                await App.PM.MindmapRepo.ShareMapAsync(mapId, target.UserId);
                if (SelectedMap.Scope != "shared")
                {
                    await SetScopeAsync(mapId, "shared");
                    SelectedMap.Scope = "shared";
                    this.RaisePropertyChanged(nameof(SelectedMapIsShared));
                    RefreshBuckets();
                }
                await BuildAndPushToAsync(mapId, target.UserId);
            }
            else
            {
                await App.PM.MindmapRepo.UnshareMapAsync(mapId, target.UserId);
                await App.PM.ComController.RevokeMindmapAsync(target.UserId, mapId);
                if ((await App.PM.MindmapRepo.ListShareUserIdsAsync(mapId)).Count == 0)
                {
                    await SetScopeAsync(mapId, "private");
                    SelectedMap.Scope = "private";
                    this.RaisePropertyChanged(nameof(SelectedMapIsShared));
                    RefreshBuckets();
                }
            }
        }

        private async Task SetScopeAsync(string mapId, string scope)
        {
            var map = await App.PM!.MindmapRepo.GetMapAsync(mapId);
            if (map == null) return;
            map.Scope = scope;
            map.UpdatedAt = DateTime.UtcNow;
            await App.PM.MindmapRepo.SaveMapAsync(map);
        }

        public void RemoveRevokedMap(string mapId)
        {
            if (App.PM == null) return;
            _ = App.PM.MindmapRepo.DeleteMapAsync(mapId);
            var item = Maps.FirstOrDefault(m => m.Id == mapId);
            if (item != null)
            {
                Maps.Remove(item);
                RefreshBuckets();
                if (SelectedMap?.Id == mapId) { SelectedMap = null; IsMapOpen = false; }
            }
        }

        private void MaybePush()
        {
            if (SelectedMap?.Scope == "shared") _ = PushSharedMapAsync(SelectedMap.Id);
        }

        private async Task PushSharedMapAsync(string mapId)
        {
            if (App.PM == null) return;
            var map = await App.PM.MindmapRepo.GetMapAsync(mapId);
            if (map == null || map.Scope != "shared") return;
            var audience = await App.PM.MindmapRepo.ResolveAudienceAsync(mapId);
            audience.RemoveAll(u => string.Equals(u, App.PM.GetUID(), StringComparison.OrdinalIgnoreCase));
            if (audience.Count == 0) return;
            map.RevisionNumber = await App.PM.MindmapRepo.BumpMapRevisionAsync(mapId);
            await App.PM.ComController.PushMindmapAsync(audience, await BuildPayloadAsync(map));
        }

        private async Task BuildAndPushToAsync(string mapId, string targetUid)
        {
            if (App.PM == null) return;
            var map = await App.PM.MindmapRepo.GetMapAsync(mapId);
            if (map == null || map.Scope != "shared") return;
            map.RevisionNumber = await App.PM.MindmapRepo.BumpMapRevisionAsync(mapId);
            await App.PM.ComController.PushMindmapAsync(new[] { targetUid }, await BuildPayloadAsync(map));
        }

        private async Task<MindmapSyncPayload> BuildPayloadAsync(Mindmap map) => new()
        {
            Map = map,
            Nodes = await App.PM!.MindmapRepo.LoadNodesAsync(map.Id),
            Links = await App.PM.MindmapRepo.LoadLinksAsync(map.Id)
        };

        public async Task RepushSharedMapsToAsync(string joinerUid)
        {
            if (App.PM == null || string.IsNullOrEmpty(joinerUid)) return;
            var uid = App.PM.GetUID();
            if (string.Equals(joinerUid, uid, StringComparison.OrdinalIgnoreCase)) return;
            var maps = await App.PM.MindmapRepo.ListVisibleMapsAsync(App.PM.GetCampaignId(), uid);
            foreach (var m in maps)
            {
                if (!string.Equals(m.OwnerUserId, uid, StringComparison.OrdinalIgnoreCase) || m.Scope != "shared") continue;
                var shares = await App.PM.MindmapRepo.ListShareUserIdsAsync(m.Id);
                if (shares.Any(s => string.Equals(s, joinerUid, StringComparison.OrdinalIgnoreCase)))
                    await BuildAndPushToAsync(m.Id, joinerUid);
            }
        }

        public async Task ApplyRemoteSync(MindmapSyncPayload payload)
        {
            if (App.PM == null || payload?.Map == null) return;
            var repo = App.PM.MindmapRepo;
            var local = await repo.GetMapAsync(payload.Map.Id);
            // Equal revisions reapply on purpose, node ops do not bump the revision so a rejoin serve at the same number can still carry fresher nodes.
            if (local != null && payload.Map.RevisionNumber < local.RevisionNumber) return;

            await repo.SaveMapAsync(payload.Map);
            foreach (var l in await repo.LoadLinksAsync(payload.Map.Id)) await repo.DeleteLinkAsync(l.Id);
            foreach (var n in await repo.LoadNodesAsync(payload.Map.Id)) await repo.DeleteNodeAsync(n.Id);
            foreach (var n in payload.Nodes) await repo.SaveNodeAsync(n);
            foreach (var l in payload.Links) await repo.SaveLinkAsync(l);

            var existing = Maps.FirstOrDefault(m => m.Id == payload.Map.Id);
            if (existing == null)
            {
                Maps.Add(new MindmapListItem(payload.Map, string.Equals(payload.Map.OwnerUserId, App.PM.GetUID(), StringComparison.OrdinalIgnoreCase)));
                RefreshBuckets();
            }
            else
                existing.Title = payload.Map.Title;

            if (SelectedMap?.Id == payload.Map.Id && !Interacting && SelectedNode == null) await LoadMapContentAsync();
        }

        private async Task<List<string>?> ShareAudienceAsync(string mapId)
        {
            if (App.PM == null) return null;
            var map = await App.PM.MindmapRepo.GetMapAsync(mapId);
            if (map == null || map.Scope != "shared") return null;
            var audience = await App.PM.MindmapRepo.ResolveAudienceAsync(mapId);
            audience.RemoveAll(u => string.Equals(u, App.PM.GetUID(), StringComparison.OrdinalIgnoreCase));
            return audience.Count == 0 ? null : audience;
        }

        private async Task PushNodeUpsertAsync(MindNodeViewModel node)
        {
            var audience = await ShareAudienceAsync(node.MindmapId);
            if (audience == null) return;
            await App.PM!.ComController.PushMindNodeUpsertAsync(audience, new MindmapNodeOp(node.MindmapId, node.ToModel()));
        }

        private async Task PushNodeMoveAsync(MindNodeViewModel node)
        {
            var audience = await ShareAudienceAsync(node.MindmapId);
            if (audience == null) return;
            await App.PM!.ComController.PushMindNodeMoveAsync(audience, new MindmapNodeMoveOp(node.MindmapId, node.Id, node.X, node.Y));
        }

        private async Task PushNodeDeleteAsync(string mapId, string nodeId)
        {
            var audience = await ShareAudienceAsync(mapId);
            if (audience == null) return;
            await App.PM!.ComController.PushMindNodeDeleteAsync(audience, new MindmapNodeDeleteOp(mapId, nodeId));
        }

        private async Task PushLinkUpsertAsync(MindLinkViewModel link)
        {
            var audience = await ShareAudienceAsync(link.MindmapId);
            if (audience == null) return;
            await App.PM!.ComController.PushMindLinkUpsertAsync(audience, new MindmapLinkOp(link.MindmapId, link.ToModel()));
        }

        private async Task PushLinkDeleteAsync(string mapId, string linkId)
        {
            var audience = await ShareAudienceAsync(mapId);
            if (audience == null) return;
            await App.PM!.ComController.PushMindLinkDeleteAsync(audience, new MindmapLinkDeleteOp(mapId, linkId));
        }

        public async Task ApplyNodeUpsert(MindmapNodeOp op)
        {
            if (App.PM == null || op?.Node == null) return;
            if (SelectedMap?.Id == op.MapId)
            {
                var vm = Nodes.FirstOrDefault(n => n.Id == op.Node.Id);
                if (vm != null)
                {
                    // Don't stomp a node the local user is mid-edit on, their copy wins here and syncs back on release
                    if (!(SelectedNode?.Id == vm.Id && Interacting))
                    {
                        vm.Kind = op.Node.Kind;
                        vm.Title = op.Node.Title;
                        vm.Body = op.Node.Body;
                        vm.ColorHex = string.IsNullOrEmpty(op.Node.ColorHex) ? "#4F81BD" : op.Node.ColorHex!;
                        vm.X = op.Node.NodeX;
                        vm.Y = op.Node.NodeY;
                        vm.Slug = op.Node.Slug ?? "";
                    }
                }
                else
                {
                    Nodes.Add(MindNodeViewModel.FromModel(op.Node));
                    this.RaisePropertyChanged(nameof(IsEmpty));
                    this.RaisePropertyChanged(nameof(ShowEmptyHint));
                }
            }
            await App.PM.MindmapRepo.SaveNodeAsync(op.Node);
        }

        public async Task ApplyNodeMove(MindmapNodeMoveOp op)
        {
            if (App.PM == null || op == null) return;
            if (SelectedMap?.Id == op.MapId)
            {
                var vm = Nodes.FirstOrDefault(n => n.Id == op.NodeId);
                if (vm != null && !(SelectedNode?.Id == vm.Id && Interacting)) { vm.X = op.X; vm.Y = op.Y; }
            }
            await App.PM.MindmapRepo.UpdateNodePositionAsync(op.NodeId, op.X, op.Y);
        }

        public async Task ApplyNodeDelete(MindmapNodeDeleteOp op)
        {
            if (App.PM == null || op == null) return;
            if (SelectedMap?.Id == op.MapId)
            {
                for (int i = Links.Count - 1; i >= 0; i--)
                    if (Links[i].FromNodeId == op.NodeId || Links[i].ToNodeId == op.NodeId) Links.RemoveAt(i);
                var vm = Nodes.FirstOrDefault(n => n.Id == op.NodeId);
                if (vm != null)
                {
                    if (SelectedNode == vm) SelectedNode = null;
                    Nodes.Remove(vm);
                    this.RaisePropertyChanged(nameof(IsEmpty));
                    this.RaisePropertyChanged(nameof(ShowEmptyHint));
                }
            }
            await App.PM.MindmapRepo.DeleteNodeAsync(op.NodeId);
        }

        public async Task ApplyLinkUpsert(MindmapLinkOp op)
        {
            if (App.PM == null || op?.Link == null) return;
            if (SelectedMap?.Id == op.MapId)
            {
                var vm = Links.FirstOrDefault(l => l.Id == op.Link.Id);
                if (vm != null)
                {
                    vm.Label = op.Link.Label ?? "";
                    vm.RelationType = op.Link.RelationType ?? "";
                }
                else Links.Add(MindLinkViewModel.FromModel(op.Link));
            }
            await App.PM.MindmapRepo.SaveLinkAsync(op.Link);
        }

        public async Task ApplyLinkDelete(MindmapLinkDeleteOp op)
        {
            if (App.PM == null || op == null) return;
            if (SelectedMap?.Id == op.MapId)
            {
                var vm = Links.FirstOrDefault(l => l.Id == op.LinkId);
                if (vm != null) Links.Remove(vm);
            }
            await App.PM.MindmapRepo.DeleteLinkAsync(op.LinkId);
        }

        private static string SlugFor(string title, string id)
        {
            var basePart = new string((title ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
            if (basePart.Length == 0) basePart = "node";
            return basePart + "-" + id[..6];
        }

        public static SolidColorBrush BrushFromHex(string hex)
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { return new SolidColorBrush(Color.Parse("#4F81BD")); }
        }
    }

    public class MindmapListItem : ViewModelBase
    {
        public string Id { get; }
        public bool IsOwner { get; }

        private string _title;
        public string Title { get => _title; set => this.RaiseAndSetIfChanged(ref _title, value); }

        private string _scope;
        public string Scope { get => _scope; set { this.RaiseAndSetIfChanged(ref _scope, value); this.RaisePropertyChanged(nameof(ScopeLabel)); } }
        public string ScopeLabel => _scope == "shared" ? "shared" : "private";

        public MindmapListItem(Mindmap m, bool isOwner)
        {
            Id = m.Id;
            IsOwner = isOwner;
            _title = m.Title;
            _scope = m.Scope;
        }
    }

    public class MindNodeViewModel : ViewModelBase
    {
        public string Id { get; set; } = "";
        public string MindmapId { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string Slug { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        private string _kind = "blank";
        public string Kind { get => _kind; set { this.RaiseAndSetIfChanged(ref _kind, value); this.RaisePropertyChanged(nameof(KindLabel)); } }
        public string KindLabel => MindmapViewModel.Templates.FirstOrDefault(t => t.Kind == _kind).Label ?? "Note";

        private string _title = "";
        public string Title { get => _title; set => this.RaiseAndSetIfChanged(ref _title, value); }

        private string _body = "";
        public string Body { get => _body; set { this.RaiseAndSetIfChanged(ref _body, value); this.RaisePropertyChanged(nameof(BodyPreview)); } }
        public string BodyPreview => _body.Length <= 140 ? _body : _body[..140] + "...";

        private string _colorHex = "#4F81BD";
        public string ColorHex { get => _colorHex; set { this.RaiseAndSetIfChanged(ref _colorHex, value); this.RaisePropertyChanged(nameof(FillBrush)); } }
        public SolidColorBrush FillBrush => MindmapViewModel.BrushFromHex(_colorHex);

        private double _x;
        public double X { get => _x; set => this.RaiseAndSetIfChanged(ref _x, value); }
        private double _y;
        public double Y { get => _y; set => this.RaiseAndSetIfChanged(ref _y, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

        public static MindNodeViewModel FromModel(MindmapNode n) => new()
        {
            Id = n.Id, MindmapId = n.MindmapId, CampaignId = n.CampaignId, Kind = n.Kind,
            Title = n.Title, Body = n.Body, ColorHex = string.IsNullOrEmpty(n.ColorHex) ? "#4F81BD" : n.ColorHex!,
            X = n.NodeX, Y = n.NodeY, Slug = n.Slug ?? "", CreatedAt = n.CreatedAt
        };

        public MindmapNode ToModel() => new()
        {
            Id = Id, MindmapId = MindmapId, CampaignId = CampaignId, Kind = Kind,
            Title = Title, Body = Body, ColorHex = ColorHex, NodeX = X, NodeY = Y, Slug = Slug, CreatedAt = CreatedAt
        };
    }

    public class MindLinkViewModel : ViewModelBase
    {
        public string Id { get; set; } = "";
        public string MindmapId { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string FromNodeId { get; set; } = "";
        public string ToNodeId { get; set; } = "";

        private string _label = "";
        public string Label { get => _label; set => this.RaiseAndSetIfChanged(ref _label, value); }

        private string _relationType = "";
        public string RelationType { get => _relationType; set => this.RaiseAndSetIfChanged(ref _relationType, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

        public static MindLinkViewModel FromModel(MindmapLink l) => new()
        {
            Id = l.Id, MindmapId = l.MindmapId, CampaignId = l.CampaignId,
            FromNodeId = l.FromNodeId, ToNodeId = l.ToNodeId, Label = l.Label ?? "", RelationType = l.RelationType ?? ""
        };

        public MindmapLink ToModel() => new()
        {
            Id = Id, MindmapId = MindmapId, CampaignId = CampaignId,
            FromNodeId = FromNodeId, ToNodeId = ToNodeId, Label = Label, RelationType = RelationType
        };
    }

    public class MindSwatch
    {
        public string Hex { get; }
        public SolidColorBrush Brush { get; }
        public MindSwatch(string hex) { Hex = hex; Brush = MindmapViewModel.BrushFromHex(hex); }
    }

    public class MindTemplateOption
    {
        public string Kind { get; }
        public string Label { get; }
        public MindTemplateOption(string kind, string label) { Kind = kind; Label = label; }
        public override string ToString() => Label;
    }

    public class MindShareTarget : ViewModelBase
    {
        public string UserId { get; }
        public string Name { get; }
        private bool _isShared;
        public bool IsShared { get => _isShared; set => this.RaiseAndSetIfChanged(ref _isShared, value); }
        public MindShareTarget(string userId, string name, bool isShared) { UserId = userId; Name = name; _isShared = isShared; }
    }
}
