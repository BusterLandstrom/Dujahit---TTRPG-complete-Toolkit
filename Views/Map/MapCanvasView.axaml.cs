using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Dujahit.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using Dujahit.Models.UI;
using Dujahit.Models;
using Dujahit.Models.Application;
using Avalonia.Layout;

namespace Dujahit.Views.Map
{
    public partial class MapCanvasView : UserControl
    {
        private readonly Dictionary<StrokeViewModel, List<Line>> _strokeLines = new();
        private readonly Dictionary<TokenViewModel, Image> _tokenImages = new();
        private readonly Dictionary<TokenViewModel, TextBlock> _tokenLabels = new();
        private readonly Dictionary<TokenViewModel, Ellipse> _tokenRings = new();
        private readonly List<Rectangle> _reachTiles = new();
        private readonly List<Control> _rulerVisuals = new();
        private readonly List<Control> _propGhost = new();
        private readonly Dictionary<PingViewModel, Control> _pingMarkers = new();
        private readonly List<Shape> _gridShapes = new();
        private readonly Dictionary<(int Col, int Row), Rectangle> _fogRects = new();
        private bool _isFogging;
        private readonly Dictionary<(int Col, int Row), Rectangle> _terrainRects = new();
        private bool _isPaintingTerrain;
        private bool _terrainErase;
        private readonly Dictionary<(int Col, int Row), Control> _objectVisuals = new();
        private bool _isPaintingObjects;
        private bool _objectErase;
        private static readonly IBrush _terrainFill = new SolidColorBrush(Color.FromArgb(90, 150, 95, 40));
        private static readonly IBrush _terrainStroke = new SolidColorBrush(Color.FromArgb(150, 170, 110, 50));
        private static readonly IBrush _objectFill = new SolidColorBrush(Color.FromArgb(110, 70, 90, 120));
        private static readonly IBrush _objectStroke = new SolidColorBrush(Color.FromArgb(180, 110, 140, 180));
        private static readonly IBrush _fogHostUnseen = new SolidColorBrush(Color.FromArgb(140, 8, 8, 14));
        private static readonly IBrush _fogHostSeen = new SolidColorBrush(Color.FromArgb(70, 8, 8, 14));
        private static readonly IBrush _fogPlayerUnseen = new SolidColorBrush(Color.FromArgb(255, 8, 8, 14));
        private static readonly IBrush _fogPlayerSeen = new SolidColorBrush(Color.FromArgb(185, 8, 8, 14));
        private static readonly IBrush _objectSightStroke = new SolidColorBrush(Color.FromArgb(210, 210, 160, 90));

        private readonly Dictionary<WallViewModel, Line> _wallLines = new();
        private bool _isWallDrawing;
        private bool _wallIsDoor;
        private Point _wallStart;
        private Line? _wallPreview;

        private readonly Dictionary<AoeTemplateViewModel, List<Control>> _aoeControls = new();
        private readonly List<Control> _aoePreviewControls = new();
        private bool _aoeAiming;
        private Point _aoeOrigin;

        private static readonly IBrush _friendlyRing = new SolidColorBrush(Color.FromArgb(235, 76, 141, 255));
        private static readonly IBrush _enemyRing = new SolidColorBrush(Color.FromArgb(235, 217, 83, 79));
        private static readonly IBrush _moveFill = new SolidColorBrush(Color.FromArgb(40, 255, 215, 0));
        private static readonly IBrush _moveStroke = new SolidColorBrush(Color.FromArgb(130, 255, 215, 0));

        private static readonly IBrush _wallStroke = new SolidColorBrush(Color.FromArgb(235, 210, 75, 75));
        private static readonly IBrush _doorClosedStroke = new SolidColorBrush(Color.FromArgb(235, 200, 162, 75));
        private static readonly IBrush _doorOpenStroke = new SolidColorBrush(Color.FromArgb(150, 160, 160, 168));

        private List<Point>? _currentStrokePoints;
        private List<Line>? _currentStrokeLines;
        private bool _isDrawing;

        private TokenViewModel? _draggingToken;
        private Point _dragOffset;
        private double _dragStartX;
        private double _dragStartY;

        private ContextMenu? _tokenMenu;

        private double _displayScale = 1.0;
        private double _offsetX;
        private double _offsetY;

        private double _userZoom = 1.0;
        private bool _panning;
        private bool _spaceHeld;
        private Point _panStart;
        private static readonly Cursor _panCursor = new(StandardCursorType.SizeAll);

        private const double MinZoom = 1.0;
        private const double MaxZoom = 16.0;
        private const double ZoomStep = 1.1;

        private Point _lastWorldPos;

        private MapCanvasViewModel? Vm => DataContext as MapCanvasViewModel;

        private bool _playerEyes;

        private bool HostEyes => (Vm?.IsHost ?? false) && !_playerEyes;

        public void UsePlayerEyes()
        {
            _playerEyes = true;
            if (ToolPanel.Parent is Panel toolHost) toolHost.Children.Remove(ToolPanel);
            if (ShowToolsButton.Parent is Panel btnHost) btnHost.Children.Remove(ShowToolsButton);
            CameraBar.IsVisible = true;
            DragDrop.SetAllowDrop(DrawCanvas, false);
            RebuildFog();
            RebuildWalls();
        }

        public void ReleaseViewModel()
        {
            if (Vm == null) return;
            Vm.ActivePings.CollectionChanged -= OnPingsChanged;
            Vm.Strokes.CollectionChanged -= OnStrokesChanged;
            Vm.Tokens.CollectionChanged -= OnTokensChanged;
            Vm.UploadTokenRequested -= OnUploadTokenRequested;
            Vm.LibraryRequested -= OnLibraryRequested;
            Vm.PropUploadRequested -= OnPropUploadRequested;
            Vm.PropertyChanged -= OnVmPropertyChanged;
            Vm.FogCellChanged -= OnFogCellChanged;
            Vm.FogBulkChanged -= RebuildFog;
            Vm.WallsChanged -= RebuildWalls;
            Vm.TerrainCellChanged -= OnTerrainCellChanged;
            Vm.TerrainChanged -= RebuildTerrain;
            Vm.ObjectCellChanged -= OnObjectCellChanged;
            Vm.ObjectsChanged -= RebuildObjects;
            Vm.AoeTemplatesChanged -= RebuildTemplates;
            Vm.ReachableChanged -= RebuildReachable;
            Vm.RulerChanged -= RebuildRuler;
            Vm.PropGhostChanged -= RebuildPropGhost;
            Vm.GridShifted -= OnGridShifted;
            foreach (var t in Vm.Tokens) t.PropertyChanged -= OnTokenPropertyChanged;
        }

        public MapCanvasView()
        {
            InitializeComponent();

            DrawCanvas.PointerPressed += OnPointerPressed;
            DrawCanvas.PointerReleased += OnPointerReleased;
            DrawCanvas.PointerMoved += OnPointerMoved;
            DrawCanvas.PointerWheelChanged += OnPointerWheel;

            DragDrop.SetAllowDrop(DrawCanvas, true);
            DrawCanvas.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            DrawCanvas.AddHandler(DragDrop.DropEvent, OnDrop);

            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);

            Focusable = true;
            PointerPressed += (_, _) => Focus();

            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (Vm == null) return;

            // Clear the world layer, not DrawCanvas, or WorldCanvas goes with it.
            WorldCanvas.Children.Clear();
            _strokeLines.Clear();
            _tokenImages.Clear();
            _tokenLabels.Clear();
            _tokenRings.Clear();
            _reachTiles.Clear();
            _rulerVisuals.Clear();
            _propGhost.Clear();
            _pingMarkers.Clear();
            _gridShapes.Clear();
            _fogRects.Clear();
            _terrainRects.Clear();
            _objectVisuals.Clear();
            _wallLines.Clear();
            _aoeControls.Clear();
            ResetView();

            foreach (var s in Vm.Strokes) RenderStroke(s);
            foreach (var t in Vm.Tokens) RenderToken(t);
            foreach (var p in Vm.ActivePings) RenderPing(p);

            ApplyLayout();

            Vm.ActivePings.CollectionChanged += OnPingsChanged;
            DrawCanvas.SizeChanged += (_, _) => ApplyLayout();
            Vm.Strokes.CollectionChanged += OnStrokesChanged;
            Vm.Tokens.CollectionChanged += OnTokensChanged;
            Vm.UploadTokenRequested += OnUploadTokenRequested;
            Vm.LibraryRequested += OnLibraryRequested;
            Vm.PropUploadRequested += OnPropUploadRequested;
            Vm.PropertyChanged += OnVmPropertyChanged;
            Vm.FogCellChanged += OnFogCellChanged;
            Vm.FogBulkChanged += RebuildFog;
            Vm.WallsChanged += RebuildWalls;
            Vm.TerrainCellChanged += OnTerrainCellChanged;
            Vm.TerrainChanged += RebuildTerrain;
            Vm.ObjectCellChanged += OnObjectCellChanged;
            Vm.ObjectsChanged += RebuildObjects;
            Vm.AoeTemplatesChanged += RebuildTemplates;
            Vm.ReachableChanged += RebuildReachable;
            Vm.RulerChanged += RebuildRuler;
            Vm.PropGhostChanged += RebuildPropGhost;
            Vm.GridShifted += OnGridShifted;
            RebuildFog();
            RebuildWalls();
            RebuildTerrain();
            RebuildObjects();
            RebuildTemplates();
            RebuildReachable();
            RebuildRuler();
        }

        private void OnGridShifted()
        {
            RebuildGrid();
            RebuildFog();
            RebuildTerrain();
            RebuildObjects();
            RebuildTemplates();
            RebuildReachable();
        }

        private void RebuildPropGhost()
        {
            foreach (var v in _propGhost) WorldCanvas.Children.Remove(v);
            _propGhost.Clear();
            if (Vm?.PropGhost is not { } box || Vm.PropPreview == null) return;

            var thickness = _displayScale > 0 ? 2.0 / _displayScale : 2.0;
            var ghost = new Image
            {
                Source = Vm.PropPreview,
                Width = box.Width,
                Height = box.Height,
                Stretch = Stretch.Fill,
                Opacity = 0.55,
                IsHitTestVisible = false,
                ZIndex = 890
            };
            Canvas.SetLeft(ghost, box.X);
            Canvas.SetTop(ghost, box.Y);
            WorldCanvas.Children.Add(ghost);
            _propGhost.Add(ghost);

            var frame = new Rectangle
            {
                Width = box.Width,
                Height = box.Height,
                Stroke = ParseCombatBrush(App.PM?.CombatMoveHighlightColor, 220, _moveStroke),
                StrokeThickness = thickness,
                StrokeDashArray = new AvaloniaList<double> { 3, 3 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
                ZIndex = 891
            };
            Canvas.SetLeft(frame, box.X);
            Canvas.SetTop(frame, box.Y);
            WorldCanvas.Children.Add(frame);
            _propGhost.Add(frame);

            var anchor = new Ellipse
            {
                Width = box.Width * 0.16,
                Height = box.Width * 0.16,
                Fill = ParseCombatBrush(App.PM?.CombatMoveHighlightColor, 240, _moveStroke),
                IsHitTestVisible = false,
                ZIndex = 892
            };
            Canvas.SetLeft(anchor, box.Right - box.Width * 0.08);
            Canvas.SetTop(anchor, box.Y - box.Width * 0.08);
            WorldCanvas.Children.Add(anchor);
            _propGhost.Add(anchor);
        }

        private void RebuildRuler()
        {
            foreach (var v in _rulerVisuals) WorldCanvas.Children.Remove(v);
            _rulerVisuals.Clear();
            if (Vm?.RulerStart is not { } start) return;

            var cell = Vm.CellSize > 0 ? Vm.CellSize : GridOverlay.BaseCellPx;
            var thickness = _displayScale > 0 ? 2.0 / _displayScale : 2.0;
            var brush = ParseCombatBrush(App.PM?.CombatMoveHighlightColor, 220, _moveStroke);

            var pin = new Ellipse
            {
                Width = cell * 0.3,
                Height = cell * 0.3,
                Fill = brush,
                IsHitTestVisible = false,
                ZIndex = 880
            };
            Canvas.SetLeft(pin, start.X - cell * 0.15);
            Canvas.SetTop(pin, start.Y - cell * 0.15);
            WorldCanvas.Children.Add(pin);
            _rulerVisuals.Add(pin);

            if (Vm.RulerEnd is not { } end) return;

            var route = Vm.RulerPath.Count > 1 ? Vm.RulerPath : new List<Point> { start, end };
            var track = new Polyline
            {
                Points = new Points(route),
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeJoin = PenLineJoin.Round,
                StrokeLineCap = PenLineCap.Round,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false,
                ZIndex = 880
            };
            if (Vm.RulerPath.Count == 0) track.StrokeDashArray = new AvaloniaList<double> { 3, 3 };
            WorldCanvas.Children.Add(track);
            _rulerVisuals.Add(track);

            var pin2 = new Ellipse
            {
                Width = cell * 0.3,
                Height = cell * 0.3,
                Fill = brush,
                IsHitTestVisible = false,
                ZIndex = 880
            };
            Canvas.SetLeft(pin2, end.X - cell * 0.15);
            Canvas.SetTop(pin2, end.Y - cell * 0.15);
            WorldCanvas.Children.Add(pin2);
            _rulerVisuals.Add(pin2);

            var label = new TextBlock
            {
                Text = Vm.RulerLabel,
                Foreground = brush,
                FontSize = Math.Max(10, cell * 0.34),
                IsHitTestVisible = false,
                ZIndex = 881
            };
            Canvas.SetLeft(label, end.X + cell * 0.25);
            Canvas.SetTop(label, end.Y - cell * 0.55);
            WorldCanvas.Children.Add(label);
            _rulerVisuals.Add(label);
        }

        private void RebuildReachable()
        {
            foreach (var t in _reachTiles) WorldCanvas.Children.Remove(t);
            _reachTiles.Clear();
            if (Vm == null) return;
            foreach (var rect in Vm.ReachableCells)
            {
                var tile = new Rectangle
                {
                    Width = rect.Width,
                    Height = rect.Height,
                    Fill = ParseCombatBrush(App.PM?.CombatMoveHighlightColor, 40, _moveFill),
                    Stroke = ParseCombatBrush(App.PM?.CombatMoveHighlightColor, 130, _moveStroke),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(tile, rect.X);
                Canvas.SetTop(tile, rect.Y);
                WorldCanvas.Children.Insert(0, tile);
                _reachTiles.Add(tile);
            }
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MapCanvasViewModel.BackgroundImage):
                    ResetView();
                    goto case nameof(MapCanvasViewModel.MapScale);
                case nameof(MapCanvasViewModel.MapScale):
                case nameof(MapCanvasViewModel.GridKind):
                case nameof(MapCanvasViewModel.ShowGrid):
                    ApplyLayout();
                    RebuildFog();
                    RebuildTemplates();
                    break;
            }
        }

        private void ApplyLayout()
        {
            UpdateWorldTransform();
            RebuildGrid();
        }

        private void UpdateWorldTransform()
        {
            var cw = DrawCanvas.Bounds.Width;
            var ch = DrawCanvas.Bounds.Height;
            var bmp = Vm?.BackgroundImage;

            double iw = 0, ih = 0, fit = 1.0;
            var hasArt = false;

            if (bmp != null && bmp.PixelSize.Width > 0 && bmp.PixelSize.Height > 0 && cw > 0 && ch > 0)
            {
                iw = bmp.PixelSize.Width;
                ih = bmp.PixelSize.Height;
                fit = Math.Min(cw / iw, ch / ih);
                hasArt = true;
            }
            else if (Vm != null)
            {
                iw = Vm.MapPixelWidth;
                ih = Vm.MapPixelHeight;
            }

            _displayScale = fit * _userZoom;

            var spillX = iw * _displayScale - cw;
            var spillY = ih * _displayScale - ch;

            _offsetX = spillX > 0 ? Math.Clamp(_offsetX, -spillX, 0) : hasArt ? -spillX / 2.0 : 0;
            _offsetY = spillY > 0 ? Math.Clamp(_offsetY, -spillY, 0) : hasArt ? -spillY / 2.0 : 0;

            var camera = new Matrix(_displayScale, 0, 0, _displayScale, _offsetX, _offsetY);

            WorldCanvas.RenderTransformOrigin = RelativePoint.TopLeft;
            WorldCanvas.RenderTransform = new MatrixTransform(camera);

            // Fucked up here, the art was laid out by the grid on its own Uniform stretch and never saw the camera at all, so zoom and pan slid every other layer off it.
            MapArt.Width = iw;
            MapArt.Height = ih;
            MapArt.RenderTransformOrigin = RelativePoint.TopLeft;
            MapArt.RenderTransform = new MatrixTransform(camera);
        }

        private void OnPointerWheel(object? sender, PointerWheelEventArgs e)
        {
            if (Vm == null) return;
            var step = e.Delta.Y > 0 ? ZoomStep : 1.0 / ZoomStep;
            ZoomAround(e.GetPosition(DrawCanvas), _userZoom * step);
            e.Handled = true;
        }

        private void ZoomAround(Point view, double targetZoom)
        {
            targetZoom = Math.Clamp(targetZoom, MinZoom, MaxZoom);
            if (Math.Abs(targetZoom - _userZoom) < 0.0001) return;

            var world = ToWorld(view);
            _userZoom = targetZoom;
            UpdateWorldTransform();
            _offsetX = view.X - world.X * _displayScale;
            _offsetY = view.Y - world.Y * _displayScale;

            ApplyLayout();
            RebuildRuler();
            RebuildPropGhost();
        }

        private void ZoomToCenter(double targetZoom) =>
            ZoomAround(new Point(DrawCanvas.Bounds.Width / 2.0, DrawCanvas.Bounds.Height / 2.0), targetZoom);

        private void OnFitClicked(object? sender, RoutedEventArgs e) => ZoomToCenter(MinZoom);

        private void OnActualSizeClicked(object? sender, RoutedEventArgs e)
        {
            var bmp = Vm?.BackgroundImage;
            var cw = DrawCanvas.Bounds.Width;
            var ch = DrawCanvas.Bounds.Height;
            if (bmp == null || bmp.PixelSize.Width <= 0 || bmp.PixelSize.Height <= 0 || cw <= 0 || ch <= 0)
            {
                ZoomToCenter(1.0);
                return;
            }
            var fit = Math.Min(cw / bmp.PixelSize.Width, ch / bmp.PixelSize.Height);
            ZoomToCenter(fit > 0 ? 1.0 / fit : 1.0);
        }

        private void ResetView()
        {
            _userZoom = 1.0;
            _offsetX = _offsetY = 0;
        }

        private Point ToWorld(Point dip)
        {
            if (_displayScale <= 0) return dip;
            return new Point((dip.X - _offsetX) / _displayScale, (dip.Y - _offsetY) / _displayScale);
        }


        private void OnStrokesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (StrokeViewModel s in e.NewItems) RenderStroke(s);

            if (e.OldItems != null)
                foreach (StrokeViewModel s in e.OldItems) UnrenderStroke(s);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var entry in _strokeLines.Values)
                    foreach (var line in entry)
                        WorldCanvas.Children.Remove(line);
                _strokeLines.Clear();
            }
        }

        private void RenderStroke(StrokeViewModel stroke)
        {
            var lines = new List<Line>(stroke.Points.Count);
            var brush = Brush.Parse(stroke.Color);

            for (int i = 1; i < stroke.Points.Count; i++)
            {
                var line = new Line
                {
                    StartPoint = stroke.Points[i - 1],
                    EndPoint = stroke.Points[i],
                    Stroke = brush,
                    StrokeThickness = stroke.Thickness
                };
                WorldCanvas.Children.Add(line);
                lines.Add(line);
            }
            _strokeLines[stroke] = lines;
        }

        private void UnrenderStroke(StrokeViewModel stroke)
        {
            if (!_strokeLines.TryGetValue(stroke, out var lines)) return;
            foreach (var line in lines)
                WorldCanvas.Children.Remove(line);
            _strokeLines.Remove(stroke);
        }


        private void OnTokensChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (TokenViewModel t in e.NewItems) RenderToken(t);

            if (e.OldItems != null)
                foreach (TokenViewModel t in e.OldItems) UnrenderToken(t);

            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var img in _tokenImages.Values)
                    WorldCanvas.Children.Remove(img);
                _tokenImages.Clear();
                foreach (var lbl in _tokenLabels.Values)
                    WorldCanvas.Children.Remove(lbl);
                _tokenLabels.Clear();
                foreach (var ring in _tokenRings.Values)
                    WorldCanvas.Children.Remove(ring);
                _tokenRings.Clear();
            }
        }

        private void RenderToken(TokenViewModel token)
        {
            var img = new Image
            {
                Source = token.Image,
                Width = token.PixelSize,
                Height = token.PixelSize,
                RenderTransform = new RotateTransform(token.Rotation),
                RenderTransformOrigin = RelativePoint.Center,
                Classes = { "token" }
            };

            ApplyActiveClass(img, token.IsActiveCombatant);
            ApplySelectedClass(img, token.IsSelected);

            Canvas.SetLeft(img, token.X - (img.Width / 2));
            Canvas.SetTop(img, token.Y - (img.Height / 2));

            WorldCanvas.Children.Add(img);
            _tokenImages[token] = img;

            var label = new TextBlock
            {
                Text = token.FeetLabel,
                FontSize = 11,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
                Padding = new Thickness(4, 1, 4, 1),
                IsHitTestVisible = false,
                IsVisible = token.HasMoved
            };
            WorldCanvas.Children.Add(label);
            _tokenLabels[token] = label;
            PositionTokenLabel(token, img, label);

            UpdateTokenRing(token, img);

            token.PropertyChanged += OnTokenPropertyChanged;
        }

        // Placed ring underneath each token, blue for players and red for non players, idk just looks like halos
        private void UpdateTokenRing(TokenViewModel token, Image img)
        {
            if (token.Side == TokenSide.None)
            {
                if (_tokenRings.TryGetValue(token, out var old))
                {
                    WorldCanvas.Children.Remove(old);
                    _tokenRings.Remove(token);
                }
                return;
            }

            if (!_tokenRings.TryGetValue(token, out var ring))
            {
                ring = new Ellipse { Fill = null, StrokeThickness = 3, IsHitTestVisible = false };
                var idx = WorldCanvas.Children.IndexOf(img);
                if (idx >= 0) WorldCanvas.Children.Insert(idx, ring);
                else WorldCanvas.Children.Add(ring);
                _tokenRings[token] = ring;
            }
            ring.Stroke = token.Side == TokenSide.Friendly
                ? ParseCombatBrush(App.PM?.CombatFriendRingColor, 235, _friendlyRing)
                : ParseCombatBrush(App.PM?.CombatEnemyRingColor, 235, _enemyRing);
            PositionTokenRing(token, img, ring);
        }

        private static IBrush ParseCombatBrush(string? hex, byte alpha, IBrush fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try
            {
                var c = Color.Parse(hex);
                return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            }
            catch { return fallback; }
        }

        private static void PositionTokenRing(TokenViewModel token, Image img, Ellipse ring)
        {
            var size = img.Width + 6;
            ring.Width = size;
            ring.Height = size;
            Canvas.SetLeft(ring, token.X - (size / 2));
            Canvas.SetTop(ring, token.Y - (size / 2));
        }

        private static void PositionTokenLabel(TokenViewModel token, Image img, TextBlock label)
        {
            var w = label.Bounds.Width > 0 ? label.Bounds.Width : 24;
            Canvas.SetLeft(label, token.X - (w / 2));
            Canvas.SetTop(label, token.Y + (img.Height / 2) + 2);
        }

        private void UnrenderToken(TokenViewModel token)
        {
            if (_tokenLabels.TryGetValue(token, out var lbl))
            {
                WorldCanvas.Children.Remove(lbl);
                _tokenLabels.Remove(token);
            }
            if (_tokenRings.TryGetValue(token, out var ring))
            {
                WorldCanvas.Children.Remove(ring);
                _tokenRings.Remove(token);
            }
            if (!_tokenImages.TryGetValue(token, out var img)) return;
            WorldCanvas.Children.Remove(img);
            _tokenImages.Remove(token);
            token.PropertyChanged -= OnTokenPropertyChanged;
        }

        private void OnTokenPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not TokenViewModel token) return;
            if (!_tokenImages.TryGetValue(token, out var img)) return;
            _tokenLabels.TryGetValue(token, out var label);

            switch (e.PropertyName)
            {
                case nameof(TokenViewModel.X):
                    Canvas.SetLeft(img, token.X - (img.Width / 2));
                    if (label != null) PositionTokenLabel(token, img, label);
                    if (_tokenRings.TryGetValue(token, out var ringX)) PositionTokenRing(token, img, ringX);
                    break;
                case nameof(TokenViewModel.Y):
                    Canvas.SetTop(img, token.Y - (img.Height / 2));
                    if (label != null) PositionTokenLabel(token, img, label);
                    if (_tokenRings.TryGetValue(token, out var ringY)) PositionTokenRing(token, img, ringY);
                    break;
                case nameof(TokenViewModel.Scale):
                case nameof(TokenViewModel.Size):
                case nameof(TokenViewModel.CellSize):
                    img.Width = token.PixelSize;
                    img.Height = token.PixelSize;
                    Canvas.SetLeft(img, token.X - (img.Width / 2));
                    Canvas.SetTop(img, token.Y - (img.Height / 2));
                    if (label != null) PositionTokenLabel(token, img, label);
                    if (_tokenRings.TryGetValue(token, out var ringS)) PositionTokenRing(token, img, ringS);
                    break;
                case nameof(TokenViewModel.Rotation):
                    img.RenderTransform = new RotateTransform(token.Rotation);
                    break;
                case nameof(TokenViewModel.Side):
                    UpdateTokenRing(token, img);
                    break;
                case nameof(TokenViewModel.IsActiveCombatant):
                    ApplyActiveClass(img, token.IsActiveCombatant);
                    break;
                case nameof(TokenViewModel.IsSelected):
                    ApplySelectedClass(img, token.IsSelected);
                    break;
                case nameof(TokenViewModel.FeetMoved):
                case nameof(TokenViewModel.FeetLabel):
                    if (label != null)
                    {
                        label.Text = token.FeetLabel;
                        label.IsVisible = token.HasMoved;
                        PositionTokenLabel(token, img, label);
                    }
                    break;
            }
        }

        private static void ApplyActiveClass(Image img, bool isActive)
        {
            if (isActive)
            {
                if (!img.Classes.Contains("active")) img.Classes.Add("active");
            }
            else
            {
                img.Classes.Remove("active");
            }
        }

        private static void ApplySelectedClass(Image img, bool isSelected)
        {
            if (isSelected)
            {
                if (!img.Classes.Contains("selected")) img.Classes.Add("selected");
            }
            else
            {
                img.Classes.Remove("selected");
            }
        }


        private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (Vm == null) return;

            var pos = ToWorld(e.GetPosition(DrawCanvas));
            var props = e.GetCurrentPoint(DrawCanvas).Properties;
            _lastWorldPos = pos;

            if (props.IsMiddleButtonPressed || (_spaceHeld && props.IsLeftButtonPressed))
            {
                _panning = true;
                _panStart = e.GetPosition(DrawCanvas);
                e.Pointer.Capture(DrawCanvas);
                Cursor = _panCursor;
                return;
            }

            if (_playerEyes) return;

            if (PressedOnToken(pos, props)) return;
            if (PressedWithTool(pos, props)) return;
            if (!props.IsLeftButtonPressed) return;

            Vm.SelectedToken = null;

            if (Vm.Mode == CanvasToolMode.Token && Vm.SelectedTokenPreview != null)
            {
                _ = Vm.PlaceTokenAt(pos.X, pos.Y);
                return;
            }

            if (Vm.Mode == CanvasToolMode.Draw) BeginStroke(pos);
        }

        private void BeginStroke(Point pos)
        {
            _isDrawing = true;
            _currentStrokePoints = new List<Point> { pos };
            _currentStrokeLines = new List<Line>();
        }

        private bool PressedOnToken(Point pos, PointerPointProperties props)
        {
            if (Vm == null) return false;
            var hit = HitTestToken(pos);
            if (hit == null || !(props.IsLeftButtonPressed || props.IsRightButtonPressed)) return false;

            Vm.SelectedToken = hit;

            if (props.IsRightButtonPressed)
            {
                if (Vm.IsHost) ShowTokenContextMenu(hit);
                return true;
            }

            if (Vm.CanMoveToken(hit))
            {
                _draggingToken = hit;
                _dragStartX = hit.X;
                _dragStartY = hit.Y;
                _dragOffset = pos - new Point(hit.X, hit.Y);
            }
            return true;
        }

        // Everything here eats the press before the selection is cleared, which is why aiming a template does not drop the token it is centred on.
        private bool PressedWithTool(Point pos, PointerPointProperties props)
        {
            if (Vm == null) return false;
            var left = props.IsLeftButtonPressed;
            var right = props.IsRightButtonPressed;

            switch (Vm.Mode)
            {
                case CanvasToolMode.Ping when left:
                    _ = Vm.Ping(pos.X, pos.Y);
                    return true;

                case CanvasToolMode.Ping when right:
                    BeginStroke(pos);
                    return true;

                case CanvasToolMode.Draw when right:
                    _ = Vm.Ping(pos.X, pos.Y);
                    return true;

                case CanvasToolMode.Fog when left:
                    if (Vm.IsHost)
                    {
                        _isFogging = true;
                        PaintFogAt(pos);
                    }
                    return true;

                case CanvasToolMode.MapObject when left && Vm.HasPropArmed:
                    if (Vm.IsHost) _ = Vm.PlacePropAt(pos.X, pos.Y);
                    return true;

                case CanvasToolMode.MapObject when right && Vm.HasPropArmed:
                    Vm.DisarmProp();
                    return true;

                case CanvasToolMode.MapObject when left || right:
                    if (Vm.IsHost)
                    {
                        _isPaintingObjects = true;
                        _objectErase = right;
                        PaintObjectAt(pos);
                    }
                    return true;

                case CanvasToolMode.Terrain when left || right:
                    if (Vm.IsHost)
                    {
                        _isPaintingTerrain = true;
                        _terrainErase = right;
                        PaintTerrainAt(pos);
                    }
                    return true;

                case CanvasToolMode.Wall when left:
                    if (Vm.IsHost) BeginWall(pos, false);
                    return true;

                case CanvasToolMode.Wall when right:
                    if (Vm.IsHost)
                    {
                        var doomed = HitTestWall(pos);
                        if (doomed != null) Vm.DeleteWall(doomed);
                    }
                    return true;

                case CanvasToolMode.Door when left:
                    if (Vm.IsHost)
                    {
                        var target = HitTestWall(pos);
                        if (target != null) _ = Vm.MarkOrToggleDoor(target);
                        else BeginWall(pos, true);
                    }
                    return true;

                case CanvasToolMode.Ruler when left:
                    Vm.RulerClick(pos.X, pos.Y);
                    return true;

                case CanvasToolMode.Ruler when right:
                    Vm.ClearRuler();
                    return true;

                case CanvasToolMode.Template when left:
                    _aoeOrigin = Vm.AoeFromToken && Vm.SelectedToken != null
                        ? new Point(Vm.SelectedToken.X, Vm.SelectedToken.Y)
                        : pos;
                    _aoeAiming = true;
                    UpdateAoePreview(pos);
                    return true;

                default:
                    return false;
            }
        }

        private void BeginWall(Point pos, bool asDoor)
        {
            if (Vm == null) return;
            _isWallDrawing = true;
            _wallIsDoor = asDoor;
            _wallStart = Vm.SnapWallEnds(pos.X, pos.Y, pos.X, pos.Y).A;
            _wallPreview = new Line
            {
                StartPoint = _wallStart,
                EndPoint = _wallStart,
                Stroke = asDoor ? _doorClosedStroke : _wallStroke,
                StrokeThickness = 3,
                IsHitTestVisible = false,
                ZIndex = 860
            };
            WorldCanvas.Children.Add(_wallPreview);
        }

        private void OnPointerMoved(object? sender, PointerEventArgs e)
        {
            if (Vm == null) return;

            if (_panning)
            {
                var here = e.GetPosition(DrawCanvas);
                _offsetX += here.X - _panStart.X;
                _offsetY += here.Y - _panStart.Y;
                _panStart = here;
                UpdateWorldTransform();
                return;
            }

            if (_playerEyes) return;   // Or the tv drags my prop ghost about.

            var pos = ToWorld(e.GetPosition(DrawCanvas));
            _lastWorldPos = pos;
            Vm.TrackPropGhost(pos.X, pos.Y);

            if (_isFogging)
            {
                PaintFogAt(pos);
                return;
            }

            if (_isPaintingObjects)
            {
                PaintObjectAt(pos);
                return;
            }

            if (_isPaintingTerrain)
            {
                PaintTerrainAt(pos);
                return;
            }

            if (_isWallDrawing && _wallPreview != null)
            {
                var snapped = Vm.SnapWallEnds(_wallStart.X, _wallStart.Y, pos.X, pos.Y);
                _wallPreview.StartPoint = snapped.A;
                _wallPreview.EndPoint = snapped.B;
                return;
            }

            if (_aoeAiming)
            {
                UpdateAoePreview(pos);
                return;
            }

            if (_draggingToken != null)
            {
                var newCenter = pos - _dragOffset;
                _draggingToken.X = newCenter.X;
                _draggingToken.Y = newCenter.Y;
                return;
            }

            if (_isDrawing
                && _currentStrokePoints != null
                && _currentStrokeLines != null)
            {
                var prev = _currentStrokePoints[^1];
                var line = new Line
                {
                    StartPoint = prev,
                    EndPoint = pos,
                    Stroke = Brush.Parse(Vm.CurrentStrokeColor),
                    StrokeThickness = Vm.CurrentStrokeThickness
                };
                WorldCanvas.Children.Add(line);
                _currentStrokeLines.Add(line);
                _currentStrokePoints.Add(pos);
            }
        }

        private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (Vm == null) return;

            if (_panning)
            {
                _panning = false;
                e.Pointer.Capture(null);
                Cursor = Cursor.Default;
                return;
            }

            if (_playerEyes) return;

            if (_isFogging)
            {
                _isFogging = false;
                _ = Vm.FlushFogPaintAsync();
                return;
            }

            if (_isPaintingObjects)
            {
                _isPaintingObjects = false;
                _ = Vm.FlushMapObjectsAsync();
                return;
            }

            if (_isPaintingTerrain)
            {
                _isPaintingTerrain = false;
                _ = Vm.FlushDifficultTerrainAsync();
                return;
            }

            if (_isWallDrawing)
            {
                _isWallDrawing = false;
                if (_wallPreview != null)
                {
                    WorldCanvas.Children.Remove(_wallPreview);
                    _wallPreview = null;
                }
                var end = ToWorld(e.GetPosition(DrawCanvas));
                double wdx = end.X - _wallStart.X, wdy = end.Y - _wallStart.Y;
                if (Math.Sqrt(wdx * wdx + wdy * wdy) > 5)
                    Vm.AddWall(_wallStart.X, _wallStart.Y, end.X, end.Y, _wallIsDoor);
                return;
            }

            if (_aoeAiming)
            {
                _aoeAiming = false;
                RemoveAoePreview();
                var end = ToWorld(e.GetPosition(DrawCanvas));
                double dirDeg = Math.Atan2(end.Y - _aoeOrigin.Y, end.X - _aoeOrigin.X) * 180.0 / Math.PI;
                Vm.PlaceAoeTemplate(_aoeOrigin.X, _aoeOrigin.Y, dirDeg);
                return;
            }

            if (_draggingToken != null)
            {
                var snapped = Vm.SnapPointFor(_draggingToken, _draggingToken.X, _draggingToken.Y, _draggingToken.PixelSize);
                var refused = Vm.MoveRefusedReason(_draggingToken, snapped.X, snapped.Y, _dragStartX, _dragStartY);
                if (refused.Length > 0)
                {
                    _draggingToken.X = _dragStartX;
                    _draggingToken.Y = _dragStartY;
                    NavItem.NavError?.Invoke(refused);
                }
                else
                {
                    _draggingToken.X = snapped.X;
                    _draggingToken.Y = snapped.Y;
                    _ = Vm.NotifyTokenMoved(_draggingToken);
                    Vm.CheckOpportunityAttacks(_draggingToken, _dragStartX, _dragStartY);
                }
                _draggingToken = null;
                return;
            }

            if (_isDrawing && _currentStrokePoints != null && _currentStrokePoints.Count > 1)
            {
                if (_currentStrokeLines != null)
                    foreach (var l in _currentStrokeLines)
                        WorldCanvas.Children.Remove(l);

                _ = Vm.AddStroke(new StrokeViewModel(
                    Guid.NewGuid().ToString("N"),
                    new List<Point>(_currentStrokePoints),
                    Vm.CurrentStrokeColor,
                    Vm.CurrentStrokeThickness,
                    App.PM.GetUID()));
            }

            _isDrawing = false;
            _currentStrokePoints = null;
            _currentStrokeLines = null;
        }

        private void OnPingsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (PingViewModel p in e.NewItems) RenderPing(p);
            if (e.OldItems != null)
                foreach (PingViewModel p in e.OldItems) UnrenderPing(p);
        }

        private void RenderPing(PingViewModel ping)
        {
            var cell = Vm?.CellSize ?? GridOverlay.BaseCellPx;
            var endR = cell * 1.1;
            var startScale = 0.25 / 1.1;
            var thickness = _displayScale > 0 ? 3.0 / _displayScale : 3.0;

            var ring = new Ellipse
            {
                Width = endR * 2,
                Height = endR * 2,
                Fill = Brushes.Transparent,
                Stroke = SafeBrush(ping.Color),
                StrokeThickness = thickness,
                IsHitTestVisible = false,
                Opacity = 0,
                ZIndex = 999,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new ScaleTransform(startScale, startScale)
            };
            Canvas.SetLeft(ring, ping.X - endR);
            Canvas.SetTop(ring, ping.Y - endR);
            WorldCanvas.Children.Add(ring);
            _pingMarkers[ping] = ring;

            var pulse = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(1400),
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(OpacityProperty, 0.9),
                            new Setter(ScaleTransform.ScaleXProperty, startScale),
                            new Setter(ScaleTransform.ScaleYProperty, startScale)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(OpacityProperty, 0.0),
                            new Setter(ScaleTransform.ScaleXProperty, 1.0),
                            new Setter(ScaleTransform.ScaleYProperty, 1.0)
                        }
                    }
                }
            };
            _ = pulse.RunAsync(ring);
        }

        private static IBrush SafeBrush(string color)
        {
            try { return Brush.Parse(color); }
            catch { return Brush.Parse("#FFD700"); }
        }

        private void UnrenderPing(PingViewModel ping)
        {
            if (!_pingMarkers.TryGetValue(ping, out var marker)) return;
            WorldCanvas.Children.Remove(marker);
            _pingMarkers.Remove(ping);
        }

        private void RebuildGrid()
        {
            foreach (var s in _gridShapes) WorldCanvas.Children.Remove(s);
            _gridShapes.Clear();

            if (Vm == null || !Vm.ShowGrid) return;

            var gw = Vm.MapPixelWidth;
            var gh = Vm.MapPixelHeight;
            if (gw <= 0 || gh <= 0) return;

            var brush = new SolidColorBrush(Color.Parse("#55FFFFFF"));
            var thickness = _displayScale > 0 ? 1.0 / _displayScale : 1.0;
            foreach (var shape in GridOverlay.Build(Vm.GridKind, gw, gh, Vm.CellSize, brush, thickness, Vm.GridOffsetX, Vm.GridOffsetY))
            {
                shape.ZIndex = -1;
                WorldCanvas.Children.Add(shape);
                _gridShapes.Add(shape);
            }
        }

        private void RebuildFog()
        {
            foreach (var rect in _fogRects.Values)
                WorldCanvas.Children.Remove(rect);
            _fogRects.Clear();

            if (Vm == null || !Vm.FogEnabled) return;
            foreach (var (col, row) in Vm.FogHiddenCells)
                AddFogRect(col, row);
        }

        private IBrush FogBrushFor(int col, int row)
        {
            var seen = Vm?.IsFogCellSeen(col, row) ?? false;
            if (HostEyes) return seen ? _fogHostSeen : _fogHostUnseen;
            return seen ? _fogPlayerSeen : _fogPlayerUnseen;
        }

        private void AddFogRect(int col, int row)
        {
            if (Vm == null) return;
            var cell = Vm.CellSize;
            if (cell <= 0) return;

            var fill = FogBrushFor(col, row);
            if (_fogRects.TryGetValue((col, row), out var existing))
            {
                existing.Fill = fill;
                return;
            }

            var rect = new Rectangle
            {
                Width = cell,
                Height = cell,
                Fill = fill,
                IsHitTestVisible = false,
                ZIndex = 900
            };
            Canvas.SetLeft(rect, GridOverlay.CellEdge(col, Vm.GridOffsetX, cell));
            Canvas.SetTop(rect, GridOverlay.CellEdge(row, Vm.GridOffsetY, cell));
            WorldCanvas.Children.Add(rect);
            _fogRects[(col, row)] = rect;
        }

        private void RemoveFogRect(int col, int row)
        {
            if (!_fogRects.TryGetValue((col, row), out var rect)) return;
            WorldCanvas.Children.Remove(rect);
            _fogRects.Remove((col, row));
        }

        private void OnFogCellChanged(int col, int row, bool hidden)
        {
            if (Vm == null || !Vm.FogEnabled) return;
            if (hidden) AddFogRect(col, row);
            else RemoveFogRect(col, row);
        }

        private void PaintFogAt(Point worldPos)
        {
            if (Vm == null) return;
            var cell = Vm.CellSize;
            if (cell <= 0) return;
            int col = GridOverlay.CellIndex(worldPos.X, Vm.GridOffsetX, cell);
            int row = GridOverlay.CellIndex(worldPos.Y, Vm.GridOffsetY, cell);
            if (col < 0 || row < 0 || col >= Vm.FogCols || row >= Vm.FogRows) return;
            Vm.PaintFogCell(col, row, Vm.FogHide);
        }

        private void RebuildTerrain()
        {
            foreach (var rect in _terrainRects.Values)
                WorldCanvas.Children.Remove(rect);
            _terrainRects.Clear();
            if (Vm == null) return;
            foreach (var (col, row) in Vm.DifficultCells)
                AddTerrainRect(col, row);
        }

        private void AddTerrainRect(int col, int row)
        {
            if (Vm == null || _terrainRects.ContainsKey((col, row))) return;
            var cell = Vm.CellSize;
            if (cell <= 0) return;
            var rect = new Rectangle
            {
                Width = cell,
                Height = cell,
                Fill = _terrainFill,
                Stroke = _terrainStroke,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, GridOverlay.CellEdge(col, Vm.GridOffsetX, cell));
            Canvas.SetTop(rect, GridOverlay.CellEdge(row, Vm.GridOffsetY, cell));
            WorldCanvas.Children.Insert(0, rect);
            _terrainRects[(col, row)] = rect;
        }

        private void RemoveTerrainRect(int col, int row)
        {
            if (!_terrainRects.TryGetValue((col, row), out var rect)) return;
            WorldCanvas.Children.Remove(rect);
            _terrainRects.Remove((col, row));
        }

        private void OnTerrainCellChanged(int col, int row, bool difficult)
        {
            if (difficult) AddTerrainRect(col, row);
            else RemoveTerrainRect(col, row);
        }

        private void PaintTerrainAt(Point worldPos)
        {
            if (Vm == null) return;
            var cell = Vm.CellSize;
            if (cell <= 0) return;
            int col = GridOverlay.CellIndex(worldPos.X, Vm.GridOffsetX, cell);
            int row = GridOverlay.CellIndex(worldPos.Y, Vm.GridOffsetY, cell);
            if (col < 0 || row < 0 || col >= Vm.FogCols || row >= Vm.FogRows) return;
            Vm.PaintDifficultCell(col, row, !_terrainErase);
        }

        private void RebuildObjects()
        {
            foreach (var v in _objectVisuals.Values)
                WorldCanvas.Children.Remove(v);
            _objectVisuals.Clear();
            if (Vm == null) return;
            foreach (var kv in Vm.ObjectCells)
                AddObjectVisual(kv.Key.Col, kv.Key.Row, kv.Value);
        }

        private void AddObjectVisual(int col, int row, string itemId)
        {
            if (Vm == null || _objectVisuals.ContainsKey((col, row))) return;
            var cell = Vm.CellSize;
            if (cell <= 0) return;

            var blocksSight = Vm.ObjectBlocksSight(col, row);
            var label = App.PM?.Rules?.GridItemName(itemId) ?? itemId;

            var box = new Border
            {
                Width = cell,
                Height = cell,
                Background = _objectFill,
                BorderBrush = blocksSight ? _objectSightStroke : _objectStroke,
                BorderThickness = new Thickness(blocksSight ? 2 : 1),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = label,
                    FontSize = Math.Max(8, cell / 6.0),
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Canvas.SetLeft(box, GridOverlay.CellEdge(col, Vm.GridOffsetX, cell));
            Canvas.SetTop(box, GridOverlay.CellEdge(row, Vm.GridOffsetY, cell));
            WorldCanvas.Children.Insert(0, box);
            _objectVisuals[(col, row)] = box;
        }

        private void RemoveObjectVisual(int col, int row)
        {
            if (!_objectVisuals.TryGetValue((col, row), out var v)) return;
            WorldCanvas.Children.Remove(v);
            _objectVisuals.Remove((col, row));
        }

        private void OnObjectCellChanged(int col, int row, string? itemId)
        {
            RemoveObjectVisual(col, row);
            if (itemId != null) AddObjectVisual(col, row, itemId);
        }

        private void PaintObjectAt(Point worldPos)
        {
            if (Vm == null) return;
            var cell = Vm.CellSize;
            if (cell <= 0) return;
            int col = GridOverlay.CellIndex(worldPos.X, Vm.GridOffsetX, cell);
            int row = GridOverlay.CellIndex(worldPos.Y, Vm.GridOffsetY, cell);
            if (col < 0 || row < 0 || col >= Vm.FogCols || row >= Vm.FogRows) return;
            Vm.PaintObjectCell(col, row, !_objectErase);
        }

        private void RebuildWalls()
        {
            foreach (var line in _wallLines.Values)
                WorldCanvas.Children.Remove(line);
            _wallLines.Clear();

            if (Vm == null || !Vm.WallsEnabled) return;
            foreach (var w in Vm.Walls)
            {
                if (!HostEyes && !w.IsDoor) continue;
                RenderWall(w);
            }
        }

        private void RenderWall(WallViewModel w)
        {
            IBrush brush;
            double thickness;
            bool dashed = false;
            if (w.IsDoor)
            {
                if (w.DoorOpen) { brush = _doorOpenStroke; thickness = 2.5; dashed = true; }
                else { brush = _doorClosedStroke; thickness = 4; }
            }
            else { brush = _wallStroke; thickness = 3.5; }

            var line = new Line
            {
                StartPoint = new Point(w.X1, w.Y1),
                EndPoint = new Point(w.X2, w.Y2),
                Stroke = brush,
                StrokeThickness = thickness,
                IsHitTestVisible = false,
                ZIndex = 850
            };
            if (dashed) line.StrokeDashArray = new AvaloniaList<double> { 2, 2 };

            WorldCanvas.Children.Add(line);
            _wallLines[w] = line;
        }

        private WallViewModel? HitTestWall(Point pos)
        {
            if (Vm == null) return null;
            WallViewModel? best = null;
            double bestDist = 8.0;
            foreach (var w in Vm.Walls)
            {
                double d = DistanceToSegment(pos, new Point(w.X1, w.Y1), new Point(w.X2, w.Y2));
                if (d < bestDist) { bestDist = d; best = w; }
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

        private void RebuildTemplates()
        {
            foreach (var list in _aoeControls.Values)
                foreach (var c in list)
                    WorldCanvas.Children.Remove(c);
            _aoeControls.Clear();

            if (Vm == null) return;
            foreach (var t in Vm.AoeTemplates)
            {
                var visuals = BuildAoeVisuals(t.Shape, t.OriginX, t.OriginY, t.DirectionDeg, t.SizeFt, t.WidthFt, t.Color);
                foreach (var c in visuals) WorldCanvas.Children.Add(c);
                _aoeControls[t] = visuals;
            }
        }

        private void UpdateAoePreview(Point cursorWorld)
        {
            RemoveAoePreview();
            if (Vm == null) return;
            double dirDeg = Math.Atan2(cursorWorld.Y - _aoeOrigin.Y, cursorWorld.X - _aoeOrigin.X) * 180.0 / Math.PI;
            var visuals = BuildAoeVisuals(Vm.AoeShape, _aoeOrigin.X, _aoeOrigin.Y, dirDeg, (double)Vm.AoeSizeFt, (double)Vm.AoeWidthFt, Vm.AoeColor);
            foreach (var c in visuals)
            {
                WorldCanvas.Children.Add(c);
                _aoePreviewControls.Add(c);
            }
        }

        private void RemoveAoePreview()
        {
            foreach (var c in _aoePreviewControls)
                WorldCanvas.Children.Remove(c);
            _aoePreviewControls.Clear();
        }

        private List<Control> BuildAoeVisuals(string shape, double ox, double oy, double dirDeg, double sizeFt, double widthFt, string colorHex)
        {
            var result = new List<Control>();
            if (Vm == null) return result;
            double feetPerSquare = App.PM?.Rules.FeetPerSquare ?? 5.0;
            double defaultLineFt = App.PM?.Rules.DefaultLineWidthFeet ?? 5.0;
            double pxPerFoot = Vm.CellSize / feetPerSquare;
            if (pxPerFoot <= 0 || sizeFt <= 0) return result;

            var baseColor = ParseColorOr(colorHex, Color.FromRgb(79, 129, 189));
            var fill = new SolidColorBrush(Color.FromArgb(80, baseColor.R, baseColor.G, baseColor.B));
            var stroke = new SolidColorBrush(Color.FromArgb(220, baseColor.R, baseColor.G, baseColor.B));

            double rad = dirDeg * Math.PI / 180.0;
            double ux = Math.Cos(rad), uy = Math.Sin(rad);
            double perpx = -uy, perpy = ux;

            bool clip = Vm.HasAnySightBlocker;
            if (clip)
                return BuildClippedCells(shape, ox, oy, ux, uy, perpx, perpy, sizeFt * pxPerFoot, widthFt > 0 ? widthFt * pxPerFoot : defaultLineFt * pxPerFoot, fill);

            switch (shape)
            {
                case "circle":
                {
                    double r = sizeFt * pxPerFoot;
                    var ell = new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = 1.5, IsHitTestVisible = false, ZIndex = 870 };
                    Canvas.SetLeft(ell, ox - r);
                    Canvas.SetTop(ell, oy - r);
                    result.Add(ell);
                    break;
                }
                case "cube":
                {
                    double s = sizeFt * pxPerFoot;
                    double half = s / 2;
                    double a0 = (App.PM?.Rules?.CubeOriginOnFace ?? true) ? 0 : -half;
                    double a1 = (App.PM?.Rules?.CubeOriginOnFace ?? true) ? s : half;
                    result.Add(PolyPath(new[]
                    {
                        new Point(ox + ux * a0 + perpx * half, oy + uy * a0 + perpy * half),
                        new Point(ox + ux * a0 - perpx * half, oy + uy * a0 - perpy * half),
                        new Point(ox + ux * a1 - perpx * half, oy + uy * a1 - perpy * half),
                        new Point(ox + ux * a1 + perpx * half, oy + uy * a1 + perpy * half)
                    }, fill, stroke));
                    break;
                }
                case "line":
                {
                    double len = sizeFt * pxPerFoot;
                    double w = (widthFt > 0 ? widthFt : defaultLineFt) * pxPerFoot;
                    double ex = ox + ux * len, ey = oy + uy * len;
                    result.Add(PolyPath(new[]
                    {
                        new Point(ox + perpx * w / 2, oy + perpy * w / 2),
                        new Point(ox - perpx * w / 2, oy - perpy * w / 2),
                        new Point(ex - perpx * w / 2, ey - perpy * w / 2),
                        new Point(ex + perpx * w / 2, ey + perpy * w / 2)
                    }, fill, stroke));
                    break;
                }
                default:
                {
                    double len = sizeFt * pxPerFoot;
                    double half = len * (App.PM?.Rules?.ConeWidthRatio ?? 0.5);
                    double bx = ox + ux * len, by = oy + uy * len;
                    result.Add(PolyPath(new[]
                    {
                        new Point(ox, oy),
                        new Point(bx + perpx * half, by + perpy * half),
                        new Point(bx - perpx * half, by - perpy * half)
                    }, fill, stroke));
                    break;
                }
            }
            return result;
        }

        private List<Control> BuildClippedCells(string shape, double ox, double oy, double ux, double uy, double perpx, double perpy, double sizePx, double widthPx, IBrush fill)
        {
            var cells = new List<Control>();
            if (Vm == null) return cells;
            double cell = Vm.CellSize;
            if (cell <= 0) return cells;

            double minX, minY, maxX, maxY;
            if (shape == "circle")
            {
                minX = ox - sizePx; maxX = ox + sizePx;
                minY = oy - sizePx; maxY = oy + sizePx;
            }
            else if (shape == "cube")
            {
                double half = sizePx / 2;
                double a0 = (App.PM?.Rules?.CubeOriginOnFace ?? true) ? 0 : -half;
                double a1 = (App.PM?.Rules?.CubeOriginOnFace ?? true) ? sizePx : half;
                var xs = new[] { ox + ux * a0 + perpx * half, ox + ux * a0 - perpx * half, ox + ux * a1 + perpx * half, ox + ux * a1 - perpx * half };
                var ys = new[] { oy + uy * a0 + perpy * half, oy + uy * a0 - perpy * half, oy + uy * a1 + perpy * half, oy + uy * a1 - perpy * half };
                minX = xs.Min(); maxX = xs.Max(); minY = ys.Min(); maxY = ys.Max();
            }
            else if (shape == "line")
            {
                double ex = ox + ux * sizePx, ey = oy + uy * sizePx;
                var xs = new[] { ox + perpx * widthPx / 2, ox - perpx * widthPx / 2, ex + perpx * widthPx / 2, ex - perpx * widthPx / 2 };
                var ys = new[] { oy + perpy * widthPx / 2, oy - perpy * widthPx / 2, ey + perpy * widthPx / 2, ey - perpy * widthPx / 2 };
                minX = xs.Min(); maxX = xs.Max(); minY = ys.Min(); maxY = ys.Max();
            }
            else
            {
                double half = sizePx * (App.PM?.Rules?.ConeWidthRatio ?? 0.5);
                double bx = ox + ux * sizePx, by = oy + uy * sizePx;
                var xs = new[] { ox, bx + perpx * half, bx - perpx * half };
                var ys = new[] { oy, by + perpy * half, by - perpy * half };
                minX = xs.Min(); maxX = xs.Max(); minY = ys.Min(); maxY = ys.Max();
            }

            int col0 = GridOverlay.CellIndex(minX, Vm.GridOffsetX, cell);
            int row0 = GridOverlay.CellIndex(minY, Vm.GridOffsetY, cell);
            int col1 = GridOverlay.CellIndex(maxX, Vm.GridOffsetX, cell);
            int row1 = GridOverlay.CellIndex(maxY, Vm.GridOffsetY, cell);

            for (int col = col0; col <= col1; col++)
            {
                for (int row = row0; row <= row1; row++)
                {
                    double cx = GridOverlay.CellEdge(col, Vm.GridOffsetX, cell) + cell / 2.0;
                    double cy = GridOverlay.CellEdge(row, Vm.GridOffsetY, cell) + cell / 2.0;
                    if (!PointInAoe(shape, cx, cy, ox, oy, ux, uy, perpx, perpy, sizePx, widthPx)) continue;
                    if (!CellVisible(ox, oy, cx, cy)) continue;

                    var rect = new Rectangle { Width = cell, Height = cell, Fill = fill, IsHitTestVisible = false, ZIndex = 870 };
                    Canvas.SetLeft(rect, GridOverlay.CellEdge(col, Vm.GridOffsetX, cell));
                    Canvas.SetTop(rect, GridOverlay.CellEdge(row, Vm.GridOffsetY, cell));
                    cells.Add(rect);
                }
            }
            return cells;
        }

        private static bool PointInAoe(string shape, double cx, double cy, double ox, double oy, double ux, double uy, double perpx, double perpy, double sizePx, double widthPx)
        {
            double dx = cx - ox, dy = cy - oy;
            if (shape == "circle")
                return dx * dx + dy * dy <= sizePx * sizePx;
            if (shape == "cube")
            {
                double calong = dx * ux + dy * uy;
                double cside = dx * perpx + dy * perpy;
                if (Math.Abs(cside) > sizePx / 2) return false;
                return (App.PM?.Rules?.CubeOriginOnFace ?? true)
                    ? calong >= 0 && calong <= sizePx
                    : Math.Abs(calong) <= sizePx / 2;
            }
            if (shape == "line")
            {
                double along = dx * ux + dy * uy;
                double side = dx * perpx + dy * perpy;
                return along >= 0 && along <= sizePx && Math.Abs(side) <= widthPx / 2;
            }
            double half = sizePx * (App.PM?.Rules?.ConeWidthRatio ?? 0.5);
            var b1 = new Point(ox + ux * sizePx + perpx * half, oy + uy * sizePx + perpy * half);
            var b2 = new Point(ox + ux * sizePx - perpx * half, oy + uy * sizePx - perpy * half);
            return PointInTriangle(new Point(cx, cy), new Point(ox, oy), b1, b2);
        }

        private bool CellVisible(double ox, double oy, double cx, double cy)
        {
            if (Vm == null) return true;
            if (Math.Abs(cx - ox) < 0.001 && Math.Abs(cy - oy) < 0.001) return true;
            return !Vm.SightBlocked(new Point(ox, oy), new Point(cx, cy));
        }

        private static bool WallBlocksSight(WallViewModel w) => w.BlocksSight && !(w.IsDoor && w.DoorOpen);

        private static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        private static bool PointInTriangle(Point p, Point a, Point b, Point c)
        {
            double d1 = Cross(p, a, b);
            double d2 = Cross(p, b, c);
            double d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0;
            bool pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static bool SegmentsIntersect(Point p1, Point p2, Point p3, Point p4)
        {
            double d1 = Cross(p3, p4, p1);
            double d2 = Cross(p3, p4, p2);
            double d3 = Cross(p1, p2, p3);
            double d4 = Cross(p1, p2, p4);
            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;
            return false;
        }

        private static Path PolyPath(Point[] pts, IBrush fill, IBrush stroke)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(pts[0], true);
                for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i]);
                ctx.EndFigure(true);
            }
            return new Path { Data = geo, Fill = fill, Stroke = stroke, StrokeThickness = 1.5, IsHitTestVisible = false, ZIndex = 870 };
        }

        private static Color ParseColorOr(string? hex, Color fallback)
        {
            if (!string.IsNullOrWhiteSpace(hex))
            {
                try { return Color.Parse(hex); }
                catch (FormatException) { }
            }
            return fallback;
        }

        private TokenViewModel? HitTestToken(Point pos)
        {
            if (Vm == null) return null;

            for (int i = Vm.Tokens.Count - 1; i >= 0; i--)
            {
                var t = Vm.Tokens[i];
                if (!_tokenImages.TryGetValue(t, out var img)) continue;

                var left = Canvas.GetLeft(img);
                var top = Canvas.GetTop(img);
                var rect = new Rect(left, top, img.Width, img.Height);
                if (rect.Contains(pos)) return t;
            }
            return null;
        }


        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (Vm == null) return;

            if (TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox) return;

            if (_playerEyes)
            {
                switch (e.Key)
                {
                    case Key.Space:
                        _spaceHeld = true;
                        e.Handled = true;
                        break;
                    case Key.OemPlus:
                    case Key.Add:
                        ZoomToCenter(_userZoom * ZoomStep);
                        e.Handled = true;
                        break;
                    case Key.OemMinus:
                    case Key.Subtract:
                        ZoomToCenter(_userZoom / ZoomStep);
                        e.Handled = true;
                        break;
                }
                return;
            }

            if (e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Vm.UndoCommand.Execute().Subscribe();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Y && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                Vm.RedoCommand.Execute().Subscribe();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.Space:
                    _spaceHeld = true;
                    e.Handled = true;
                    break;
                case Key.I:
                    Vm.UndoCommand.Execute().Subscribe();
                    e.Handled = true;
                    break;
                case Key.Tab:
                    Vm.CycleTokenSelection();
                    e.Handled = true;
                    break;
                case Key.R:
                    _ = Vm.RotateSelection(15);
                    e.Handled = true;
                    break;
                case Key.P:
                    _ = Vm.Ping(_lastWorldPos.X, _lastWorldPos.Y);
                    e.Handled = true;
                    break;
                case Key.OemPlus:
                case Key.Add:
                    ZoomToCenter(_userZoom * ZoomStep);
                    e.Handled = true;
                    break;
                case Key.OemMinus:
                case Key.Subtract:
                    ZoomToCenter(_userZoom / ZoomStep);
                    e.Handled = true;
                    break;
                case Key.OemCloseBrackets:
                    Vm.AdjustScale(+0.1);
                    e.Handled = true;
                    break;
                case Key.OemOpenBrackets:
                    Vm.AdjustScale(-0.1);
                    e.Handled = true;
                    break;
                case Key.Delete:
                case Key.Back:
                    if (Vm.SelectedToken != null)
                    {
                        _ = Vm.RemoveToken(Vm.SelectedToken);
                        Vm.SelectedToken = null;
                    }
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.Right:
                case Key.Up:
                case Key.Down:
                    if (Vm.SelectedToken != null)
                    {
                        var step = Vm.CellSize > 0 ? Vm.CellSize : 25;
                        var t = Vm.SelectedToken;
                        var oldX = t.X;
                        var oldY = t.Y;
                        if (e.Key == Key.Left) t.X -= step;
                        else if (e.Key == Key.Right) t.X += step;
                        else if (e.Key == Key.Up) t.Y -= step;
                        else t.Y += step;
                        var refused = Vm.MoveRefusedReason(t, t.X, t.Y, oldX, oldY);
                        if (refused.Length > 0)
                        {
                            t.X = oldX;
                            t.Y = oldY;
                            NavItem.NavError?.Invoke(refused);
                        }
                        else
                        {
                            _ = Vm.NotifyTokenMoved(t);
                            Vm.CheckOpportunityAttacks(t, oldX, oldY);
                        }
                        e.Handled = true;
                    }
                    break;
            }
        }

        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space) return;
            _spaceHeld = false;
            if (_panning)
            {
                _panning = false;
                Cursor = Cursor.Default;
            }
        }


        private async void OnLibraryRequested()
        {
            try
            {
                if (Vm == null) return;
                var owner = TopLevel.GetTopLevel(this) as Window;
                if (owner == null) return;
                await new MapLibraryDialog(Vm, Vm.Hub).ShowDialog(owner);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnLibraryRequested", ex); }
        }

        private async void OnUploadTokenRequested()
        {
            try
            {
                if (Vm == null) return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Upload Token Images",
                    AllowMultiple = true,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images")
                        {
                            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }
                        }
                    }
                });

                if (files.Count == 0) return;
                await Vm.ImportTokenFilesAsync(files);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnUploadTokenRequested", ex); }
        }

        private async void OnPropUploadRequested()
        {
            try
            {
                if (Vm == null) return;

                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel == null) return;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Pick map object images",
                    AllowMultiple = true,
                    FileTypeFilter = new[]
                    {
                        new FilePickerFileType("Images")
                        {
                            Patterns = new[] { "*.png", "*.webp", "*.jpg", "*.jpeg", "*.gif", "*.bmp" }
                        }
                    }
                });

                if (files.Count == 0) return;
                await Vm.ImportPropFilesAsync(files);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnPropUploadRequested", ex); }
        }

        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = (Vm?.IsHost ?? false) && e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private async void OnDrop(object? sender, DragEventArgs e)
        {
            try
            {
                if (Vm == null || !Vm.IsHost) return;
                e.Handled = true;

                var dropped = e.Data.GetFiles();
                if (dropped == null) return;

                var pos = ToWorld(e.GetPosition(DrawCanvas));
                if (Vm.Mode == CanvasToolMode.MapObject)
                {
                    if (await Vm.ImportPropFilesAsync(dropped) > 0) await Vm.PlacePropAt(pos.X, pos.Y);
                    return;
                }

                if (await Vm.ImportTokenFilesAsync(dropped) > 0) await Vm.PlaceTokenAt(pos.X, pos.Y);
            }
            catch (Exception ex) { ErrorLog.Log("Unhandled in OnDrop", ex); }
        }

        private void ShowTokenContextMenu(TokenViewModel token)
        {
            if (Vm == null) return;

            _tokenMenu?.Close();

            var menu = new ContextMenu();

            if (!string.IsNullOrEmpty(token.CharacterId))
            {
                var open = new MenuItem { Header = "Open character sheet" };
                open.Click += (_, _) => Vm.OpenSheetCommand.Execute().Subscribe();
                menu.Items.Add(open);
                menu.Items.Add(new Separator());
            }

            foreach (var size in Vm.CreatureSizes)
            {
                var captured = size;
                var item = new MenuItem
                {
                    Header = $"{captured}  ({FootprintLabel(captured)})"
                };
                if (token.Size == captured) item.FontWeight = FontWeight.Bold;
                item.Click += (_, _) => _ = Vm.SetTokenSize(token, captured);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());

            var rescue = new MenuItem { Header = "Send to the nearest free square" };
            rescue.Click += (_, _) => _ = Vm.NudgeIntoBounds(token);
            menu.Items.Add(rescue);

            var remove = new MenuItem { Header = "Remove token" };
            remove.Classes.Add("danger");
            remove.Click += (_, _) =>
            {
                _ = Vm.RemoveToken(token);
                Vm.SelectedToken = null;
            };
            menu.Items.Add(remove);

            _tokenMenu = menu;
            menu.Open(DrawCanvas);
        }

        private static string FootprintLabel(CreatureSize size)
        {
            var n = (App.PM?.Rules ?? new GameRules()).SquaresForSize(size.ToString());
            string s = n == 0.5 ? "1/2"
                : n == Math.Floor(n) ? ((int)n).ToString()
                : n.ToString("0.##", CultureInfo.InvariantCulture);
            return s + "x" + s;
        }
    }
}
