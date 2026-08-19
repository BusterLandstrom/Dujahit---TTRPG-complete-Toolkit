using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Dujahit.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Dujahit.Views
{
    public partial class FactionsView : UserControl
    {
        private const double NodeW = 132;
        private const double NodeH = 46;
        private const double MinScale = 0.25;
        private const double MaxScale = 4.0;

        private static readonly IBrush _selectedStroke = new SolidColorBrush(Color.Parse("#FFD700"));
        private static readonly IBrush _nodeStroke = new SolidColorBrush(Color.Parse("#22000000"));

        private double _scale = 1.0;
        private double _offsetX;
        private double _offsetY;
        private bool _centered;

        private readonly Dictionary<FactionNodeViewModel, Border> _nodeVisuals = new();
        private readonly Dictionary<FactionRelationViewModel, RelationVisual> _relationVisuals = new();

        private FactionNodeViewModel? _draggingNode;
        private Point _dragOffset;

        private bool _panning;
        private Point _panStartScreen;
        private double _panStartOffX;
        private double _panStartOffY;

        private FactionNodeViewModel? _linkFrom;
        private Line? _pendingLink;

        private FactionsViewModel? _vm;
        private FactionsViewModel? Vm => DataContext as FactionsViewModel;

        public FactionsView()
        {
            InitializeComponent();

            GraphCanvas.PointerPressed += OnPointerPressed;
            GraphCanvas.PointerMoved += OnPointerMoved;
            GraphCanvas.PointerReleased += OnPointerReleased;
            GraphCanvas.PointerWheelChanged += OnPointerWheel;
            GraphCanvas.SizeChanged += (_, _) => { EnsureCentered(); };

            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (_vm != null)
            {
                _vm.Nodes.CollectionChanged -= OnNodesChanged;
                _vm.Relations.CollectionChanged -= OnRelationsChanged;
                _vm.SpawnPointProvider = null;
            }

            _vm = Vm;
            if (_vm == null) return;

            _vm.Nodes.CollectionChanged += OnNodesChanged;
            _vm.Relations.CollectionChanged += OnRelationsChanged;
            _vm.SpawnPointProvider = () =>
            {
                var c = ToWorld(new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2));
                return (c.X, c.Y);
            };

            EnsureCentered();
            RebuildAll();
        }

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
            WorldCanvas.RenderTransform = new MatrixTransform(
                new Matrix(_scale, 0, 0, _scale, _offsetX, _offsetY));
        }

        private Point ToWorld(Point dip)
        {
            if (_scale <= 0) return dip;
            return new Point((dip.X - _offsetX) / _scale, (dip.Y - _offsetY) / _scale);
        }

        private void RebuildAll()
        {
            foreach (var b in _nodeVisuals.Keys.ToList()) b.PropertyChanged -= OnNodePropertyChanged;
            foreach (var r in _relationVisuals.Keys.ToList()) r.PropertyChanged -= OnRelationPropertyChanged;
            _nodeVisuals.Clear();
            _relationVisuals.Clear();
            WorldCanvas.Children.Clear();

            if (Vm == null) return;
            foreach (var rel in Vm.Relations) RenderRelation(rel);
            foreach (var node in Vm.Nodes) RenderNode(node);
        }

        private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) { RebuildAll(); return; }
            if (e.OldItems != null)
                foreach (FactionNodeViewModel n in e.OldItems) UnrenderNode(n);
            if (e.NewItems != null)
                foreach (FactionNodeViewModel n in e.NewItems) RenderNode(n);
        }

        private void OnRelationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset) { RebuildAll(); return; }
            if (e.OldItems != null)
                foreach (FactionRelationViewModel r in e.OldItems) UnrenderRelation(r);
            if (e.NewItems != null)
                foreach (FactionRelationViewModel r in e.NewItems) RenderRelation(r);
        }

        private void RenderNode(FactionNodeViewModel node)
        {
            if (_nodeVisuals.ContainsKey(node)) return;

            var label = new TextBlock
            {
                Text = node.Name,
                Foreground = Contrast(node.ColorHex),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = NodeW - 16,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8, 0)
            };

            var border = new Border
            {
                Width = NodeW,
                Height = NodeH,
                CornerRadius = new CornerRadius(10),
                Background = node.FillBrush,
                BorderBrush = node.IsSelected ? _selectedStroke : _nodeStroke,
                BorderThickness = new Thickness(node.IsSelected ? 3 : 1),
                Child = label
            };

            Canvas.SetLeft(border, node.X - NodeW / 2);
            Canvas.SetTop(border, node.Y - NodeH / 2);
            WorldCanvas.Children.Add(border);
            _nodeVisuals[node] = border;
            node.PropertyChanged += OnNodePropertyChanged;
        }

        private void UnrenderNode(FactionNodeViewModel node)
        {
            if (_nodeVisuals.TryGetValue(node, out var border))
            {
                WorldCanvas.Children.Remove(border);
                _nodeVisuals.Remove(node);
            }
            node.PropertyChanged -= OnNodePropertyChanged;
        }

        private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not FactionNodeViewModel node) return;
            if (!_nodeVisuals.TryGetValue(node, out var border)) return;

            switch (e.PropertyName)
            {
                case nameof(FactionNodeViewModel.X):
                    Canvas.SetLeft(border, node.X - NodeW / 2);
                    UpdateRelationsFor(node);
                    break;
                case nameof(FactionNodeViewModel.Y):
                    Canvas.SetTop(border, node.Y - NodeH / 2);
                    UpdateRelationsFor(node);
                    break;
                case nameof(FactionNodeViewModel.Name):
                    if (border.Child is TextBlock tb) tb.Text = node.Name;
                    break;
                case nameof(FactionNodeViewModel.FillBrush):
                case nameof(FactionNodeViewModel.ColorHex):
                    border.Background = node.FillBrush;
                    if (border.Child is TextBlock t2) t2.Foreground = Contrast(node.ColorHex);
                    break;
                case nameof(FactionNodeViewModel.IsSelected):
                    border.BorderBrush = node.IsSelected ? _selectedStroke : _nodeStroke;
                    border.BorderThickness = new Thickness(node.IsSelected ? 3 : 1);
                    break;
            }
        }

        private void RenderRelation(FactionRelationViewModel rel)
        {
            if (_relationVisuals.ContainsKey(rel)) return;

            var rv = new RelationVisual
            {
                Line = new Line { Stroke = rel.LineBrush, StrokeThickness = rel.IsSelected ? 4 : 2.5 },
                ArrowA = new Line { Stroke = rel.LineBrush, StrokeThickness = rel.IsSelected ? 4 : 2.5 },
                ArrowB = new Line { Stroke = rel.LineBrush, StrokeThickness = rel.IsSelected ? 4 : 2.5 }
            };

            WorldCanvas.Children.Insert(0, rv.ArrowB);
            WorldCanvas.Children.Insert(0, rv.ArrowA);
            WorldCanvas.Children.Insert(0, rv.Line);
            _relationVisuals[rel] = rv;
            rel.PropertyChanged += OnRelationPropertyChanged;
            UpdateRelationGeometry(rel, rv);
        }

        private void UnrenderRelation(FactionRelationViewModel rel)
        {
            if (_relationVisuals.TryGetValue(rel, out var rv))
            {
                WorldCanvas.Children.Remove(rv.Line);
                WorldCanvas.Children.Remove(rv.ArrowA);
                WorldCanvas.Children.Remove(rv.ArrowB);
                _relationVisuals.Remove(rel);
            }
            rel.PropertyChanged -= OnRelationPropertyChanged;
        }

        private void OnRelationPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not FactionRelationViewModel rel) return;
            if (!_relationVisuals.TryGetValue(rel, out var rv)) return;

            if (e.PropertyName == nameof(FactionRelationViewModel.LineBrush)
                || e.PropertyName == nameof(FactionRelationViewModel.RelationType))
            {
                rv.Line.Stroke = rel.LineBrush;
                rv.ArrowA.Stroke = rel.LineBrush;
                rv.ArrowB.Stroke = rel.LineBrush;
            }
            else if (e.PropertyName == nameof(FactionRelationViewModel.IsSelected))
            {
                var th = rel.IsSelected ? 4 : 2.5;
                rv.Line.StrokeThickness = th;
                rv.ArrowA.StrokeThickness = th;
                rv.ArrowB.StrokeThickness = th;
            }
        }

        private void UpdateRelationsFor(FactionNodeViewModel node)
        {
            foreach (var pair in _relationVisuals)
            {
                if (pair.Key.FromFactionId == node.Id || pair.Key.ToFactionId == node.Id)
                    UpdateRelationGeometry(pair.Key, pair.Value);
            }
        }

        private void UpdateRelationGeometry(FactionRelationViewModel rel, RelationVisual rv)
        {
            var from = Vm?.FindNodeById(rel.FromFactionId);
            var to = Vm?.FindNodeById(rel.ToFactionId);
            if (from == null || to == null)
            {
                rv.Line.IsVisible = false;
                rv.ArrowA.IsVisible = false;
                rv.ArrowB.IsVisible = false;
                return;
            }
            rv.Line.IsVisible = true;
            rv.ArrowA.IsVisible = true;
            rv.ArrowB.IsVisible = true;

            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1)
            {
                rv.Line.IsVisible = false;
                rv.ArrowA.IsVisible = false;
                rv.ArrowB.IsVisible = false;
                return;
            }
            double ux = dx / len, uy = dy / len;
            var s = new Point(from.X + ux * 24, from.Y + uy * 24);
            var e = new Point(to.X - ux * 30, to.Y - uy * 30);
            rv.Line.StartPoint = s;
            rv.Line.EndPoint = e;

            double al = 12, ah = 6;
            var b = new Point(e.X - ux * al, e.Y - uy * al);
            double px = -uy, py = ux;
            rv.ArrowA.StartPoint = e;
            rv.ArrowA.EndPoint = new Point(b.X + px * ah, b.Y + py * ah);
            rv.ArrowB.StartPoint = e;
            rv.ArrowB.EndPoint = new Point(b.X - px * ah, b.Y - py * ah);
        }

        private FactionNodeViewModel? HitTestNode(Point world)
        {
            if (Vm == null) return null;
            for (int i = Vm.Nodes.Count - 1; i >= 0; i--)
            {
                var n = Vm.Nodes[i];
                var rect = new Rect(n.X - NodeW / 2, n.Y - NodeH / 2, NodeW, NodeH);
                if (rect.Contains(world)) return n;
            }
            return null;
        }

        private FactionRelationViewModel? HitTestRelation(Point world)
        {
            if (Vm == null) return null;
            FactionRelationViewModel? best = null;
            double bestDist = 9.0;
            foreach (var rel in Vm.Relations)
            {
                var from = Vm.FindNodeById(rel.FromFactionId);
                var to = Vm.FindNodeById(rel.ToFactionId);
                if (from == null || to == null) continue;
                double d = DistanceToSegment(world, new Point(from.X, from.Y), new Point(to.X, to.Y));
                if (d < bestDist)
                {
                    bestDist = d;
                    best = rel;
                }
            }
            return best;
        }

        private static double DistanceToSegment(Point p, Point a, Point b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double lenSq = dx * dx + dy * dy;
            if (lenSq < 0.0001) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));
            double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            t = Math.Max(0, Math.Min(1, t));
            double cx = a.X + t * dx, cy = a.Y + t * dy;
            return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
        }

        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Vm == null) return;
            var screen = e.GetPosition(GraphCanvas);
            var props = e.GetCurrentPoint(GraphCanvas).Properties;
            if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed) return;

            if (props.IsMiddleButtonPressed)
            {
                _panning = true;
                _panStartScreen = screen;
                _panStartOffX = _offsetX;
                _panStartOffY = _offsetY;
                e.Handled = true;
                return;
            }

            var world = ToWorld(screen);
            var node = HitTestNode(world);
            if (node != null)
            {
                if (Vm.LinkMode)
                {
                    _linkFrom = node;
                    _pendingLink = new Line
                    {
                        Stroke = new SolidColorBrush(Color.Parse("#AAAAAA")),
                        StrokeThickness = 2,
                        StrokeDashArray = new AvaloniaList<double> { 4, 3 },
                        StartPoint = new Point(node.X, node.Y),
                        EndPoint = world
                    };
                    WorldCanvas.Children.Add(_pendingLink);
                }
                else
                {
                    Vm.SelectedRelation = null;
                    Vm.SelectedNode = node;
                    _draggingNode = node;
                    _dragOffset = world - new Point(node.X, node.Y);
                }
                e.Handled = true;
                return;
            }

            var rel = HitTestRelation(world);
            if (rel != null)
            {
                Vm.SelectedNode = null;
                Vm.SelectedRelation = rel;
                e.Handled = true;
                return;
            }

            Vm.SelectedNode = null;
            Vm.SelectedRelation = null;
            _panning = true;
            _panStartScreen = screen;
            _panStartOffX = _offsetX;
            _panStartOffY = _offsetY;
            e.Handled = true;
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (Vm == null) return;
            var screen = e.GetPosition(GraphCanvas);
            var world = ToWorld(screen);

            if (_draggingNode != null)
            {
                var c = world - _dragOffset;
                _draggingNode.X = c.X;
                _draggingNode.Y = c.Y;
                return;
            }
            if (_linkFrom != null && _pendingLink != null)
            {
                _pendingLink.EndPoint = world;
                return;
            }
            if (_panning)
            {
                _offsetX = _panStartOffX + (screen.X - _panStartScreen.X);
                _offsetY = _panStartOffY + (screen.Y - _panStartScreen.Y);
                ApplyTransform();
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (Vm == null) return;

            if (_draggingNode != null)
            {
                _ = Vm.PersistNodePositionAsync(_draggingNode);
                _draggingNode = null;
                return;
            }
            if (_linkFrom != null)
            {
                var world = ToWorld(e.GetPosition(GraphCanvas));
                var target = HitTestNode(world);
                if (_pendingLink != null)
                {
                    WorldCanvas.Children.Remove(_pendingLink);
                    _pendingLink = null;
                }
                if (target != null && !ReferenceEquals(target, _linkFrom))
                    _ = Vm.CreateRelationAsync(_linkFrom, target);
                _linkFrom = null;
                return;
            }
            _panning = false;
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
            e.Handled = true;
        }

        private static IBrush Contrast(string? hex)
        {
            try
            {
                var c = Color.Parse(string.IsNullOrWhiteSpace(hex) ? "#4F81BD" : hex);
                double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                return lum > 145 ? Brushes.Black : Brushes.White;
            }
            catch (FormatException)
            {
                return Brushes.White;
            }
        }

        private sealed class RelationVisual
        {
            public Line Line = null!;
            public Line ArrowA = null!;
            public Line ArrowB = null!;
        }
    }
}
