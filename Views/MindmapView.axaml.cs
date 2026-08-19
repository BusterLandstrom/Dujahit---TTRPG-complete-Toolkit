using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Dujahit.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Automation;

namespace Dujahit.Views
{
    public partial class MindmapView : UserControl
    {
        private const double NodeW = 210;
        private const double NodeH = 120;
        private const double MinScale = 0.25;
        private const double MaxScale = 3.5;

        private static readonly string[] _quickColors = { "#4F81BD", "#C0504D", "#9BBB59", "#F79646", "#8064A2", "#4BACC6" };
        private static readonly IBrush _nodeStroke = new SolidColorBrush(Color.Parse("#33FFFFFF"));
        private static readonly IBrush _selectedStroke = new SolidColorBrush(Color.Parse("#FFD700"));
        private static readonly IBrush _linkStroke = new SolidColorBrush(Color.Parse("#8A8A99"));
        private static readonly IBrush _allyStroke = new SolidColorBrush(Color.Parse("#5A9E4F"));
        private static readonly IBrush _enemyStroke = new SolidColorBrush(Color.Parse("#BB4444"));
        private static readonly IBrush _rivalStroke = new SolidColorBrush(Color.Parse("#D4663A"));

        private static IBrush BrushForRelation(string? rel) => (rel ?? "").ToLowerInvariant() switch
        {
            "ally" => _allyStroke,
            "enemy" => _enemyStroke,
            "rival" => _rivalStroke,
            _ => _linkStroke
        };

        private double _scale = 1.0;
        private double _offsetX;
        private double _offsetY;
        private bool _centered;

        private readonly Dictionary<MindNodeViewModel, Border> _nodeVisuals = new();
        private readonly Dictionary<MindLinkViewModel, RelationVisual> _linkVisuals = new();

        private MindmapViewModel? _vm;
        private MindNodeViewModel? _draggingNode;
        private Point _dragOffset;
        private bool _panning;
        private Point _panStartScreen;
        private double _panStartOffX;
        private double _panStartOffY;
        private MindNodeViewModel? _linkFrom;
        private Line? _pendingLink;

        private sealed class RelationVisual
        {
            public Line Line = new();
            public Line ArrowA = new();
            public Line ArrowB = new();
        }

        public MindmapView()
        {
            InitializeComponent();
            GraphCanvas.PointerPressed += OnCanvasPressed;
            GraphCanvas.PointerMoved += OnCanvasMoved;
            GraphCanvas.PointerReleased += OnCanvasReleased;
            GraphCanvas.PointerWheelChanged += OnPointerWheel;
            GraphCanvas.LayoutUpdated += (_, _) => EnsureCentered();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm != null)
            {
                _vm.Nodes.CollectionChanged -= OnNodesChanged;
                _vm.Links.CollectionChanged -= OnLinksChanged;
            }
            _vm = DataContext as MindmapViewModel;
            if (_vm == null) return;

            _vm.Nodes.CollectionChanged += OnNodesChanged;
            _vm.Links.CollectionChanged += OnLinksChanged;
            _vm.ConfirmAsync -= ConfirmDelete;
            _vm.ConfirmAsync += ConfirmDelete;
            _vm.SpawnPointProvider = () =>
            {
                var c = ToWorld(new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2));
                return (c.X, c.Y);
            };
            RebuildAll();
        }

        private Task<bool> ConfirmDelete(string title, string message)
            => DialogWindow.ConfirmAsync(TopLevel.GetTopLevel(this) as Window, title, message);

        private void EnsureCentered()
        {
            if (_centered || GraphCanvas.Bounds.Width <= 0) return;
            _offsetX = GraphCanvas.Bounds.Width / 2;
            _offsetY = GraphCanvas.Bounds.Height / 2;
            _centered = true;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            WorldCanvas.RenderTransformOrigin = RelativePoint.TopLeft;
            WorldCanvas.RenderTransform = new MatrixTransform(new Matrix(_scale, 0, 0, _scale, _offsetX, _offsetY));
        }

        private Point ToWorld(Point dip)
        {
            if (_scale <= 0) return dip;
            return new Point((dip.X - _offsetX) / _scale, (dip.Y - _offsetY) / _scale);
        }

        private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildAll();
        private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildAll();

        private void RebuildAll()
        {
            foreach (var kv in _nodeVisuals) kv.Key.PropertyChanged -= OnNodePropertyChanged;
            foreach (var kv in _linkVisuals) kv.Key.PropertyChanged -= OnLinkPropertyChanged;
            WorldCanvas.Children.Clear();
            _nodeVisuals.Clear();
            _linkVisuals.Clear();
            _draggingNode = null;
            _linkFrom = null;
            _pendingLink = null;
            if (_vm == null) return;
            foreach (var l in _vm.Links) AddLinkVisual(l);
            foreach (var n in _vm.Nodes) AddNodeVisual(n);
        }

        private void AddNodeVisual(MindNodeViewModel node)
        {
            var kind = new TextBlock { Text = node.KindLabel, FontSize = 10, Opacity = 0.7, Foreground = Contrast(node.ColorHex) };
            var title = new TextBlock
            {
                Text = node.Title,
                FontWeight = FontWeight.SemiBold,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Contrast(node.ColorHex)
            };
            var body = new TextBlock
            {
                Text = node.BodyPreview,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxLines = 3,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = 0.85,
                Foreground = Contrast(node.ColorHex)
            };
            var stack = new StackPanel { Spacing = 2, Margin = new Thickness(10, 8) };
            stack.Children.Add(kind);
            stack.Children.Add(title);
            stack.Children.Add(body);

            var overlay = BuildHoverOverlay(node);
            var grid = new Grid();
            grid.Children.Add(stack);
            grid.Children.Add(overlay);

            var border = new Border
            {
                Width = NodeW,
                Height = NodeH,
                CornerRadius = new CornerRadius(12),
                Background = node.FillBrush,
                BorderBrush = node.IsSelected ? _selectedStroke : _nodeStroke,
                BorderThickness = new Thickness(node.IsSelected ? 3 : 1),
                ClipToBounds = true,
                Child = grid
            };
            Canvas.SetLeft(border, node.X - NodeW / 2);
            Canvas.SetTop(border, node.Y - NodeH / 2);

            border.PointerEntered += (_, _) => overlay.IsVisible = true;
            border.PointerExited += (_, _) => overlay.IsVisible = false;
            border.PointerPressed += (_, e) => OnNodePressed(node, border, e);
            border.PointerMoved += (_, e) => OnNodeMoved(node, e);
            border.PointerReleased += (_, e) => OnNodeReleased(node, e);

            WorldCanvas.Children.Add(border);
            _nodeVisuals[node] = border;
            node.PropertyChanged += OnNodePropertyChanged;
        }

        private Control BuildHoverOverlay(MindNodeViewModel node)
        {
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            foreach (var hex in _quickColors)
            {
                var h = hex;
                var dot = new Button
                {
                    Width = 15,
                    Height = 15,
                    Padding = new Thickness(0),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(0),
                    Background = MindmapViewModel.BrushFromHex(hex)
                };
                dot.Click += (_, _) => { if (_vm != null) _ = _vm.SetNodeColorAsync(node, h); };
                bar.Children.Add(dot);
            }
            var edit = new Button { Content = "✎", Padding = new Thickness(6, 1), FontSize = 12, Flyout = BuildEditFlyout(node) };
            AutomationProperties.SetName(edit, "Edit node");
            bar.Children.Add(edit);
            var del = new Button { Content = "×", Padding = new Thickness(6, 1), FontSize = 13 };
            AutomationProperties.SetName(del, "Delete node");
            del.Click += (_, _) => { if (_vm != null) _ = _vm.DeleteNodeAsync(node); };
            bar.Children.Add(del);

            return new Border
            {
                Child = bar,
                Background = new SolidColorBrush(Color.Parse("#CC1B1B26")),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(4, 3),
                Margin = new Thickness(4),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                IsVisible = false
            };
        }

        private Flyout BuildEditFlyout(MindNodeViewModel node)
        {
            var titleBox = new TextBox { Watermark = "Title", CornerRadius = new CornerRadius(8) };
            titleBox.Bind(TextBox.TextProperty, new Binding(nameof(MindNodeViewModel.Title)) { Source = node, Mode = BindingMode.TwoWay });
            var bodyBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150, CornerRadius = new CornerRadius(8) };
            bodyBox.Bind(TextBox.TextProperty, new Binding(nameof(MindNodeViewModel.Body)) { Source = node, Mode = BindingMode.TwoWay });
            var refLabel = new TextBlock { Text = "Reference from notes", FontSize = 11, Opacity = 0.7 };
            var refBox = new TextBox { IsReadOnly = true, CornerRadius = new CornerRadius(8), FontSize = 12, Text = $"<ref type=\"mindmap\" id=\"{node.Slug}\"/>" };

            var panel = new StackPanel { Spacing = 8, Width = 270 };
            panel.Children.Add(titleBox);
            panel.Children.Add(bodyBox);
            panel.Children.Add(refLabel);
            panel.Children.Add(refBox);

            var flyout = new Flyout { Content = panel };
            flyout.Closed += (_, _) => { if (_vm != null) _ = _vm.SaveNodeAsync(node); };
            return flyout;
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MindNodeViewModel node || !_nodeVisuals.TryGetValue(node, out var border)) return;
            switch (e.PropertyName)
            {
                case nameof(MindNodeViewModel.X):
                    Canvas.SetLeft(border, node.X - NodeW / 2);
                    UpdateLinksFor(node);
                    break;
                case nameof(MindNodeViewModel.Y):
                    Canvas.SetTop(border, node.Y - NodeH / 2);
                    UpdateLinksFor(node);
                    break;
                case nameof(MindNodeViewModel.FillBrush):
                case nameof(MindNodeViewModel.ColorHex):
                    border.Background = node.FillBrush;
                    if (border.Child is Grid g && g.Children.Count > 0 && g.Children[0] is StackPanel sp)
                        foreach (var c in sp.Children)
                            if (c is TextBlock tb) tb.Foreground = Contrast(node.ColorHex);
                    break;
                case nameof(MindNodeViewModel.IsSelected):
                    border.BorderBrush = node.IsSelected ? _selectedStroke : _nodeStroke;
                    border.BorderThickness = new Thickness(node.IsSelected ? 3 : 1);
                    break;
                case nameof(MindNodeViewModel.Title):
                    if (border.Child is Grid g2 && g2.Children[0] is StackPanel sp2 && sp2.Children.Count > 1 && sp2.Children[1] is TextBlock t1) t1.Text = node.Title;
                    break;
                case nameof(MindNodeViewModel.BodyPreview):
                    if (border.Child is Grid g3 && g3.Children[0] is StackPanel sp3 && sp3.Children.Count > 2 && sp3.Children[2] is TextBlock t2) t2.Text = node.BodyPreview;
                    break;
            }
        }

        private void AddLinkVisual(MindLinkViewModel link)
        {
            var rv = new RelationVisual();
            var relStroke = BrushForRelation(link.RelationType);
            rv.Line.Stroke = relStroke; rv.Line.StrokeThickness = 2; rv.Line.IsHitTestVisible = false;
            rv.ArrowA.Stroke = relStroke; rv.ArrowA.StrokeThickness = 2; rv.ArrowA.IsHitTestVisible = false;
            rv.ArrowB.Stroke = relStroke; rv.ArrowB.StrokeThickness = 2; rv.ArrowB.IsHitTestVisible = false;
            WorldCanvas.Children.Insert(0, rv.Line);
            WorldCanvas.Children.Insert(0, rv.ArrowA);
            WorldCanvas.Children.Insert(0, rv.ArrowB);
            _linkVisuals[link] = rv;
            link.PropertyChanged += OnLinkPropertyChanged;
            UpdateLinkGeometry(link);
        }

        private void OnLinkPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is MindLinkViewModel link && _linkVisuals.TryGetValue(link, out var rv) &&
                (e.PropertyName == nameof(MindLinkViewModel.IsSelected) || e.PropertyName == nameof(MindLinkViewModel.RelationType)))
            {
                var stroke = link.IsSelected ? _selectedStroke : BrushForRelation(link.RelationType);
                rv.Line.Stroke = stroke; rv.ArrowA.Stroke = stroke; rv.ArrowB.Stroke = stroke;
                rv.Line.StrokeThickness = link.IsSelected ? 3 : 2;
            }
        }

        private void UpdateLinksFor(MindNodeViewModel node)
        {
            if (_vm == null) return;
            foreach (var l in _vm.Links)
                if (l.FromNodeId == node.Id || l.ToNodeId == node.Id) UpdateLinkGeometry(l);
        }

        private void UpdateLinkGeometry(MindLinkViewModel link)
        {
            if (_vm == null || !_linkVisuals.TryGetValue(link, out var rv)) return;
            MindNodeViewModel? from = null, to = null;
            foreach (var n in _vm.Nodes) { if (n.Id == link.FromNodeId) from = n; if (n.Id == link.ToNodeId) to = n; }
            if (from == null || to == null) return;

            double dx = to.X - from.X, dy = to.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            double ux = dx / len, uy = dy / len;
            var s = new Point(from.X + ux * 40, from.Y + uy * 40);
            var ept = new Point(to.X - ux * 46, to.Y - uy * 46);
            rv.Line.StartPoint = s; rv.Line.EndPoint = ept;
            double al = 12, ah = 6;
            var b = new Point(ept.X - ux * al, ept.Y - uy * al);
            double px = -uy, py = ux;
            rv.ArrowA.StartPoint = ept; rv.ArrowA.EndPoint = new Point(b.X + px * ah, b.Y + py * ah);
            rv.ArrowB.StartPoint = ept; rv.ArrowB.EndPoint = new Point(b.X - px * ah, b.Y - py * ah);
        }

        private MindNodeViewModel? HitTestNode(Point world)
        {
            if (_vm == null) return null;
            for (int i = _vm.Nodes.Count - 1; i >= 0; i--)
            {
                var n = _vm.Nodes[i];
                var rect = new Rect(n.X - NodeW / 2, n.Y - NodeH / 2, NodeW, NodeH);
                if (rect.Contains(world)) return n;
            }
            return null;
        }

        private MindLinkViewModel? HitTestLink(Point world)
        {
            if (_vm == null) return null;
            MindLinkViewModel? best = null;
            double bestDist = 9;
            foreach (var l in _vm.Links)
            {
                MindNodeViewModel? from = null, to = null;
                foreach (var n in _vm.Nodes) { if (n.Id == l.FromNodeId) from = n; if (n.Id == l.ToNodeId) to = n; }
                if (from == null || to == null) continue;
                double d = DistanceToSegment(world, new Point(from.X, from.Y), new Point(to.X, to.Y));
                if (d < bestDist) { bestDist = d; best = l; }
            }
            return best;
        }

        private static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double l2 = dx * dx + dy * dy;
            if (l2 < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
            double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l2));
            double projX = a.X + t * dx, projY = a.Y + t * dy;
            return Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
        }

        private void OnNodePressed(MindNodeViewModel node, Border border, PointerPressedEventArgs e)
        {
            if (_vm == null) return;
            if (!e.GetCurrentPoint(GraphCanvas).Properties.IsLeftButtonPressed) return;
            var world = ToWorld(e.GetPosition(GraphCanvas));
            if (_vm.LinkMode)
            {
                _linkFrom = node;
                _pendingLink = new Line
                {
                    Stroke = new SolidColorBrush(Color.Parse("#AAAAAA")),
                    StrokeThickness = 2,
                    IsHitTestVisible = false,
                    StrokeDashArray = new AvaloniaList<double> { 4, 3 },
                    StartPoint = new Point(node.X, node.Y),
                    EndPoint = world
                };
                WorldCanvas.Children.Add(_pendingLink);
            }
            else
            {
                _vm.SelectedLink = null;
                _vm.SelectedNode = node;
                _draggingNode = node;
                _dragOffset = world - new Point(node.X, node.Y);
                _vm.Interacting = true;
            }
            e.Pointer.Capture(border);
            e.Handled = true;
        }

        private void OnNodeMoved(MindNodeViewModel node, PointerEventArgs e)
        {
            if (_vm == null) return;
            var world = ToWorld(e.GetPosition(GraphCanvas));
            if (_draggingNode == node)
            {
                var c = world - _dragOffset;
                node.X = c.X;
                node.Y = c.Y;
            }
            else if (_linkFrom == node && _pendingLink != null)
            {
                _pendingLink.EndPoint = world;
            }
        }

        private void OnNodeReleased(MindNodeViewModel node, PointerReleasedEventArgs e)
        {
            if (_vm == null) return;
            e.Pointer.Capture(null);
            if (_draggingNode == node)
            {
                _vm.Interacting = false;
                _ = _vm.PersistNodePositionAsync(node);
                _draggingNode = null;
                e.Handled = true;
                return;
            }
            if (_linkFrom == node)
            {
                var world = ToWorld(e.GetPosition(GraphCanvas));
                var target = HitTestNode(world);
                if (_pendingLink != null) { WorldCanvas.Children.Remove(_pendingLink); _pendingLink = null; }
                if (target != null && !ReferenceEquals(target, _linkFrom)) _ = _vm.CreateLinkAsync(_linkFrom, target);
                _linkFrom = null;
                e.Handled = true;
            }
        }

        private void OnCanvasPressed(object? sender, PointerPressedEventArgs e)
        {
            if (_vm == null) return;
            var props = e.GetCurrentPoint(GraphCanvas).Properties;
            if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed) return;
            var screen = e.GetPosition(GraphCanvas);

            if (props.IsMiddleButtonPressed)
            {
                _panning = true;
                _panStartScreen = screen;
                _panStartOffX = _offsetX;
                _panStartOffY = _offsetY;
                e.Pointer.Capture(GraphCanvas);
                return;
            }

            var link = HitTestLink(ToWorld(screen));
            if (link != null)
            {
                _vm.SelectedNode = null;
                _vm.SelectedLink = link;
                return;
            }
            _vm.SelectedNode = null;
            _vm.SelectedLink = null;
            _panning = true;
            _panStartScreen = screen;
            _panStartOffX = _offsetX;
            _panStartOffY = _offsetY;
            e.Pointer.Capture(GraphCanvas);
        }

        private void OnCanvasMoved(object? sender, PointerEventArgs e)
        {
            if (!_panning) return;
            var screen = e.GetPosition(GraphCanvas);
            _offsetX = _panStartOffX + (screen.X - _panStartScreen.X);
            _offsetY = _panStartOffY + (screen.Y - _panStartScreen.Y);
            ApplyTransform();
        }

        private void OnCanvasReleased(object? sender, PointerReleasedEventArgs e)
        {
            _panning = false;
            e.Pointer.Capture(null);
        }

        private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
        {
            var pos = e.GetPosition(GraphCanvas);
            var before = ToWorld(pos);
            double factor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
            double newScale = Math.Max(MinScale, Math.Min(MaxScale, _scale * factor));
            if (Math.Abs(newScale - _scale) < 0.0001) return;
            _scale = newScale;
            _offsetX = pos.X - before.X * _scale;
            _offsetY = pos.Y - before.Y * _scale;
            ApplyTransform();
        }

        private static IBrush Contrast(string hex)
        {
            try
            {
                var c = Color.Parse(hex);
                double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
                return lum > 0.6 ? Brushes.Black : Brushes.White;
            }
            catch { return Brushes.White; }
        }
    }
}
