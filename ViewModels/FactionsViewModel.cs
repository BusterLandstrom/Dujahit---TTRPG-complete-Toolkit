using Dujahit.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Media;

namespace Dujahit.ViewModels
{
    public class FactionsViewModel : ViewModelBase
    {
        private static readonly string[] _palette = { "#C0504D", "#4F81BD", "#9BBB59", "#8064A2", "#4BACC6", "#F79646", "#7F6084", "#C8843C" };

        public ObservableCollection<FactionNodeViewModel> Nodes { get; } = new();
        public ObservableCollection<FactionRelationViewModel> Relations { get; } = new();
        public ObservableCollection<string> RelationSuggestions { get; } = new() { "ally", "enemy", "neutral", "rival", "vassal", "unknown" };
        public ObservableCollection<FactionSwatch> ColorSwatches { get; } = new(_palette.Select(h => new FactionSwatch(h)));

        public Func<(double X, double Y)>? SpawnPointProvider;

        private FactionNodeViewModel? _selectedNode;
        public FactionNodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set
            {
                if (_selectedNode != null) _selectedNode.IsSelected = false;
                this.RaiseAndSetIfChanged(ref _selectedNode, value);
                if (_selectedNode != null) _selectedNode.IsSelected = true;
                ConfirmingDelete = false;
                this.RaisePropertyChanged(nameof(HasSelectedNode));
            }
        }

        private bool _confirmingDelete;
        public bool ConfirmingDelete
        {
            get => _confirmingDelete;
            set => this.RaiseAndSetIfChanged(ref _confirmingDelete, value);
        }

        private FactionRelationViewModel? _selectedRelation;
        public FactionRelationViewModel? SelectedRelation
        {
            get => _selectedRelation;
            set
            {
                if (_selectedRelation != null) _selectedRelation.IsSelected = false;
                this.RaiseAndSetIfChanged(ref _selectedRelation, value);
                if (_selectedRelation != null) _selectedRelation.IsSelected = true;
                this.RaisePropertyChanged(nameof(HasSelectedRelation));
                this.RaisePropertyChanged(nameof(SelectedRelationEndpoints));
            }
        }

        private bool _linkMode;
        public bool LinkMode
        {
            get => _linkMode;
            set => this.RaiseAndSetIfChanged(ref _linkMode, value);
        }

        public bool HasSelectedNode => SelectedNode != null;
        public bool HasSelectedRelation => SelectedRelation != null;

        public string SelectedRelationEndpoints
        {
            get
            {
                var rel = SelectedRelation;
                if (rel == null) return "";
                var from = FindNodeById(rel.FromFactionId)?.Name ?? "?";
                var to = FindNodeById(rel.ToFactionId)?.Name ?? "?";
                return from + "  ->  " + to;
            }
        }

        public ReactiveCommand<Unit, Unit> AddFactionCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveSelectedFactionCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteSelectedFactionCommand { get; }
        public ReactiveCommand<string, Unit> SetNodeColorCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveSelectedRelationCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteSelectedRelationCommand { get; }
        public ReactiveCommand<string, Unit> SetRelationTypeCommand { get; }
        public ReactiveCommand<Unit, Unit> ArmDeleteFactionCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelDeleteFactionCommand { get; }

        public FactionsViewModel()
        {
            AddFactionCommand = ReactiveCommand.CreateFromTask(AddFactionAsync);
            SaveSelectedFactionCommand = ReactiveCommand.CreateFromTask(SaveSelectedFactionAsync);
            DeleteSelectedFactionCommand = ReactiveCommand.CreateFromTask(DeleteSelectedFactionAsync);
            ArmDeleteFactionCommand = ReactiveCommand.Create(() => { ConfirmingDelete = true; });
            CancelDeleteFactionCommand = ReactiveCommand.Create(() => { ConfirmingDelete = false; });
            SetNodeColorCommand = ReactiveCommand.Create<string>(hex => { if (SelectedNode != null && !string.IsNullOrWhiteSpace(hex)) SelectedNode.ColorHex = hex; });
            SaveSelectedRelationCommand = ReactiveCommand.CreateFromTask(SaveSelectedRelationAsync);
            DeleteSelectedRelationCommand = ReactiveCommand.CreateFromTask(DeleteSelectedRelationAsync);
            SetRelationTypeCommand = ReactiveCommand.Create<string>(t => { if (SelectedRelation != null && !string.IsNullOrWhiteSpace(t)) SelectedRelation.RelationType = t; });
        }

        public async Task LoadAsync()
        {
            var factions = await App.PM.LoadFactionsAsync();
            var relations = await App.PM.LoadFactionRelationsAsync();

            SelectedNode = null;
            SelectedRelation = null;
            Nodes.Clear();
            Relations.Clear();

            foreach (var f in factions) Nodes.Add(FactionNodeViewModel.FromModel(f));
            foreach (var r in relations) Relations.Add(FactionRelationViewModel.FromModel(r));
        }

        public FactionNodeViewModel? FindNodeById(string id) => Nodes.FirstOrDefault(n => n.Id == id);

        private async Task AddFactionAsync()
        {
            var (sx, sy) = SpawnPointProvider?.Invoke() ?? (0.0, 0.0);
            var spread = (Nodes.Count % 5) * 22.0;
            var node = new FactionNodeViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                Name = "New faction",
                ColorHex = _palette[Nodes.Count % _palette.Length],
                X = sx + spread,
                Y = sy + spread,
                CreatedAt = DateTime.UtcNow
            };
            Nodes.Add(node);
            SelectedRelation = null;
            SelectedNode = node;
            await App.PM.SaveFactionAsync(node.ToModel());
        }

        private async Task SaveSelectedFactionAsync()
        {
            var node = SelectedNode;
            if (node == null) return;
            this.RaisePropertyChanged(nameof(SelectedRelationEndpoints));
            await App.PM.SaveFactionAsync(node.ToModel());
        }

        private async Task DeleteSelectedFactionAsync()
        {
            var node = SelectedNode;
            if (node == null) return;
            foreach (var r in Relations.Where(r => r.FromFactionId == node.Id || r.ToFactionId == node.Id).ToList())
                Relations.Remove(r);
            Nodes.Remove(node);
            SelectedNode = null;
            await App.PM.DeleteFactionAsync(node.Id);
        }

        public async Task<FactionRelationViewModel?> CreateRelationAsync(FactionNodeViewModel from, FactionNodeViewModel to)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return null;
            if (Relations.Any(r => r.FromFactionId == from.Id && r.ToFactionId == to.Id)) return null;

            var rel = new FactionRelationViewModel
            {
                Id = Guid.NewGuid().ToString("N"),
                CampaignId = App.PM.GetCampaignId(),
                FromFactionId = from.Id,
                ToFactionId = to.Id,
                RelationType = "neutral"
            };
            Relations.Add(rel);
            SelectedNode = null;
            SelectedRelation = rel;
            await App.PM.SaveFactionRelationAsync(rel.ToModel());
            return rel;
        }

        private async Task SaveSelectedRelationAsync()
        {
            var rel = SelectedRelation;
            if (rel == null) return;
            await App.PM.SaveFactionRelationAsync(rel.ToModel());
        }

        private async Task DeleteSelectedRelationAsync()
        {
            var rel = SelectedRelation;
            if (rel == null) return;
            Relations.Remove(rel);
            SelectedRelation = null;
            await App.PM.DeleteFactionRelationAsync(rel.Id);
        }

        public async Task PersistNodePositionAsync(FactionNodeViewModel node)
        {
            if (node == null) return;
            await App.PM.UpdateFactionPositionAsync(node.Id, node.X, node.Y);
        }

        public static IBrush BrushFromHex(string? hex)
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try { return new SolidColorBrush(Color.Parse(hex)); }
                catch (FormatException) { }
            }
            return new SolidColorBrush(Color.Parse("#6A6A78"));
        }
    }

    public class FactionSwatch
    {
        public string Hex { get; }
        public IBrush Brush { get; }
        public FactionSwatch(string hex)
        {
            Hex = hex;
            Brush = FactionsViewModel.BrushFromHex(hex);
        }
    }

    public class FactionNodeViewModel : ViewModelBase
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        private double _x;
        public double X { get => _x; set => this.RaiseAndSetIfChanged(ref _x, value); }

        private double _y;
        public double Y { get => _y; set => this.RaiseAndSetIfChanged(ref _y, value); }

        private string _name = "";
        public string Name { get => _name; set => this.RaiseAndSetIfChanged(ref _name, value); }

        private string? _description;
        public string? Description { get => _description; set => this.RaiseAndSetIfChanged(ref _description, value); }

        private string _colorHex = "#4F81BD";
        public string ColorHex
        {
            get => _colorHex;
            set { this.RaiseAndSetIfChanged(ref _colorHex, value); this.RaisePropertyChanged(nameof(FillBrush)); }
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

        public IBrush FillBrush => FactionsViewModel.BrushFromHex(ColorHex);

        public static FactionNodeViewModel FromModel(Faction f) => new()
        {
            Id = f.Id,
            CampaignId = f.CampaignId,
            Name = f.Name,
            Description = f.Description,
            ColorHex = string.IsNullOrWhiteSpace(f.Color) ? "#4F81BD" : f.Color!,
            X = f.NodeX,
            Y = f.NodeY,
            CreatedAt = f.CreatedAt
        };

        public Faction ToModel() => new()
        {
            Id = Id,
            CampaignId = CampaignId,
            Name = Name,
            Description = Description,
            Color = ColorHex,
            NodeX = X,
            NodeY = Y,
            CreatedAt = CreatedAt
        };
    }

    public class FactionRelationViewModel : ViewModelBase
    {
        public string Id { get; set; } = "";
        public string CampaignId { get; set; } = "";
        public string FromFactionId { get; set; } = "";
        public string ToFactionId { get; set; } = "";

        private string _relationType = "neutral";
        public string RelationType
        {
            get => _relationType;
            set { this.RaiseAndSetIfChanged(ref _relationType, value); this.RaisePropertyChanged(nameof(LineBrush)); }
        }

        private string? _notes;
        public string? Notes { get => _notes; set => this.RaiseAndSetIfChanged(ref _notes, value); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }

        public IBrush LineBrush => BrushForType(RelationType);

        public static IBrush BrushForType(string? type)
        {
            switch ((type ?? "").Trim().ToLowerInvariant())
            {
                case "ally":
                case "allied":
                case "friend": return new SolidColorBrush(Color.Parse("#3FA34D"));
                case "enemy":
                case "hostile":
                case "war": return new SolidColorBrush(Color.Parse("#BB4444"));
                case "rival": return new SolidColorBrush(Color.Parse("#C8843C"));
                case "vassal":
                case "subject": return new SolidColorBrush(Color.Parse("#4A78C8"));
                case "neutral": return new SolidColorBrush(Color.Parse("#6A6A78"));
                default: return new SolidColorBrush(Color.Parse("#7A7A88"));
            }
        }

        public static FactionRelationViewModel FromModel(FactionRelation r) => new()
        {
            Id = r.Id,
            CampaignId = r.CampaignId,
            FromFactionId = r.FromFactionId,
            ToFactionId = r.ToFactionId,
            RelationType = string.IsNullOrWhiteSpace(r.RelationType) ? "neutral" : r.RelationType,
            Notes = r.Notes
        };

        public FactionRelation ToModel() => new()
        {
            Id = Id,
            CampaignId = CampaignId,
            FromFactionId = FromFactionId,
            ToFactionId = ToFactionId,
            RelationType = RelationType,
            Notes = Notes
        };
    }
}
