using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Collections.Generic;
using Dujahit.Models;
using Dujahit.Models.UI;
using Dujahit.Views;

namespace Dujahit.Views.Map.Dialogs
{
    public partial class CreateMapDialog : DialogWindow
    {
        public string MapName => NameInput.Text?.Trim() ?? "";
        public GridKind SelectedGridKind { get; private set; } = GridKind.Squares;
        public double CellScale => _cellPxSource / GridOverlay.BaseCellPx;
        public bool Accepted { get; private set; }

        private Border? _selectedCard;

        private Bitmap? _image;
        private double _cellPxSource = GridOverlay.BaseCellPx;
        private double _zoom = 1.0;
        private double _panX;
        private double _panY;

        private bool _panning;
        private Point _panStart;

        private bool _ready;
        private bool _suppressSlider;
        private bool _centeredOnce;

        private const double MinZoom = 1.0;
        private const double MaxZoom = 16.0;

        public CreateMapDialog()
        {
            InitializeComponent();

            ContentWidthCap = 900;
            _ready = true;
            UpdateScaleReadout();

            PreviewViewport.GetObservable(Visual.BoundsProperty).Subscribe(b =>
            {
                if (b.Width <= 0 || b.Height <= 0) return;
                if (!_centeredOnce) { CenterContent(); _centeredOnce = true; }
                Rebuild();
            });

            ZoomSlider.GetObservable(RangeBase.ValueProperty).Subscribe(OnZoomChanged);
        }

        public void SetPreviewImage(Bitmap image)
        {
            _image = image;
            _zoom = 1.0;
            _suppressSlider = true;
            ZoomSlider.Value = 1.0;
            _suppressSlider = false;
            _centeredOnce = false;
            CenterContent();
            Rebuild();
        }

        public void Prefill(string name)
        {
            NameInput.Text = name;
            SelectCard(SquaresCard, GridKind.Squares);
        }

        public int BlankCols => (int)Math.Round(ColsInput.Value ?? 32);
        public int BlankRows => (int)Math.Round(RowsInput.Value ?? 20);

        public void PrefillBlank(string name)
        {
            Prefill(name);
            Title = "New blank map";
            ArtPanel.IsVisible = false;
            BlankSizePanel.IsVisible = true;

            var rules = App.PM?.Rules;
            var cell = GridOverlay.BaseCellPx;
            ColsInput.Value = Math.Max(4, (int)Math.Round((rules?.BlankMapWidthPx ?? 2560) / cell));
            RowsInput.Value = Math.Max(4, (int)Math.Round((rules?.BlankMapHeightPx ?? 1600) / cell));

            var presets = new List<Button>();
            foreach (var p in rules?.BlankMapPresets ?? new List<BlankMapPreset>())
            {
                var button = new Button
                {
                    Content = p.Name + "  " + p.Cols + "x" + p.Rows,
                    Margin = new Thickness(0, 0, 6, 6)
                };
                button.Classes.Add("ghost");
                var preset = p;
                button.Click += (_, _) =>
                {
                    ColsInput.Value = preset.Cols;
                    RowsInput.Value = preset.Rows;
                };
                presets.Add(button);
            }
            PresetList.ItemsSource = presets;

            ColsInput.ValueChanged += (_, _) => UpdateBlankReadout();
            RowsInput.ValueChanged += (_, _) => UpdateBlankReadout();
            UpdateBlankReadout();
        }

        private void UpdateBlankReadout()
        {
            var feet = App.PM?.Rules?.FeetPerSquare ?? 5.0;
            BlankSizeReadout.Text = BlankCols + " by " + BlankRows + " squares, about "
                                    + (int)Math.Round(BlankCols * feet) + " by " + (int)Math.Round(BlankRows * feet) + " feet across.";
        }

        private void OnGridCardClicked(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border card) return;
            var kind = (card.Tag as string) == "Hexes" ? GridKind.Hexes : GridKind.Squares;
            SelectCard(card, kind);
        }

        private void SelectCard(Border card, GridKind kind)
        {
            if (_selectedCard != null) _selectedCard.Classes.Remove("selected");
            card.Classes.Add("selected");
            _selectedCard = card;
            SelectedGridKind = kind;
            Rebuild();
        }

        private void OnCellPxChanged(object? sender, NumericUpDownValueChangedEventArgs e)
        {
            if (!_ready) return;
            _cellPxSource = (double)(CellPxInput.Value ?? (decimal)GridOverlay.BaseCellPx);
            UpdateScaleReadout();
            Rebuild();
        }

        private void OnZoomChanged(double value)
        {
            if (!_ready || _suppressSlider) return;
            ZoomAround(PreviewViewport.Bounds.Width / 2.0, PreviewViewport.Bounds.Height / 2.0, value);
            Rebuild();
        }

        private void OnFitClicked(object? sender, RoutedEventArgs e)
        {
            _zoom = 1.0;
            _suppressSlider = true;
            ZoomSlider.Value = 1.0;
            _suppressSlider = false;
            CenterContent();
            Rebuild();
        }

        private void OnActualSizeClicked(object? sender, RoutedEventArgs e)
        {
            if (!TryMetrics(out var vw, out var vh, out _, out _, out _)) return;
            var fit = FitFactor(vw, vh);
            if (fit <= 0) return;
            var target = Math.Clamp(1.0 / fit, MinZoom, MaxZoom);
            ZoomAround(vw / 2.0, vh / 2.0, target);
            _suppressSlider = true;
            ZoomSlider.Value = _zoom;
            _suppressSlider = false;
            Rebuild();
        }

        private void OnViewportPressed(object? sender, PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(PreviewViewport).Properties;
            if (!props.IsLeftButtonPressed && !props.IsMiddleButtonPressed) return;
            _panning = true;
            _panStart = e.GetPosition(PreviewViewport);
            e.Pointer.Capture(PreviewViewport);
        }

        private void OnViewportMoved(object? sender, PointerEventArgs e)
        {
            if (!_panning) return;
            var p = e.GetPosition(PreviewViewport);
            _panX += p.X - _panStart.X;
            _panY += p.Y - _panStart.Y;
            _panStart = p;
            Rebuild();
        }

        private void OnViewportReleased(object? sender, PointerReleasedEventArgs e)
        {
            _panning = false;
            e.Pointer.Capture(null);
        }

        private void OnViewportWheel(object? sender, PointerWheelEventArgs e)
        {
            if (_image == null) return;
            var pos = e.GetPosition(PreviewViewport);
            var step = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
            var target = Math.Clamp(_zoom * step, MinZoom, MaxZoom);
            ZoomAround(pos.X, pos.Y, target);
            _suppressSlider = true;
            ZoomSlider.Value = _zoom;
            _suppressSlider = false;
            Rebuild();
            e.Handled = true;
        }

        private void ZoomAround(double vx, double vy, double targetZoom)
        {
            targetZoom = Math.Clamp(targetZoom, MinZoom, MaxZoom);
            if (!TryMetrics(out _, out _, out var fOld, out _, out _))
            {
                _zoom = targetZoom;
                return;
            }
            var sx = (vx - _panX) / fOld;
            var sy = (vy - _panY) / fOld;
            _zoom = targetZoom;
            if (TryMetrics(out _, out _, out var fNew, out _, out _))
            {
                _panX = vx - sx * fNew;
                _panY = vy - sy * fNew;
            }
        }

        private void CenterContent()
        {
            if (!TryMetrics(out var vw, out var vh, out _, out var dw, out var dh)) return;
            _panX = (vw - dw) / 2.0;
            _panY = (vh - dh) / 2.0;
        }

        private double FitFactor(double viewW, double viewH)
        {
            if (_image == null) return 0;
            double iw = _image.PixelSize.Width;
            double ih = _image.PixelSize.Height;
            if (iw <= 0 || ih <= 0) return 0;
            return Math.Min(viewW / iw, viewH / ih);
        }

        private bool TryMetrics(out double viewW, out double viewH, out double factor, out double dispW, out double dispH)
        {
            viewW = viewH = factor = dispW = dispH = 0;
            if (_image == null) return false;
            var vp = PreviewViewport.Bounds;
            viewW = vp.Width;
            viewH = vp.Height;
            if (viewW <= 0 || viewH <= 0) return false;
            double iw = _image.PixelSize.Width;
            double ih = _image.PixelSize.Height;
            if (iw <= 0 || ih <= 0) return false;
            var fit = Math.Min(viewW / iw, viewH / ih);
            factor = fit * _zoom;
            dispW = iw * factor;
            dispH = ih * factor;
            return true;
        }

        private void UpdateScaleReadout()
        {
            if (ScaleReadout == null) return;
            ScaleReadout.Text = $"scale {CellScale:0.00}x";
        }

        private void Rebuild()
        {
            if (!_ready) return;
            PreviewWorld.Children.Clear();
            if (!TryMetrics(out _, out _, out var factor, out var dispW, out var dispH)) return;

            var content = new Canvas { Width = dispW, Height = dispH };

            var img = new Image
            {
                Source = _image,
                Width = dispW,
                Height = dispH,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(img, 0);
            Canvas.SetTop(img, 0);
            content.Children.Add(img);

            var cellDisp = _cellPxSource * factor;
            if (cellDisp >= 3)
            {
                var brush = new SolidColorBrush(Color.Parse("#CCFFD700"));
                foreach (var shape in GridOverlay.Build(SelectedGridKind, dispW, dispH, cellDisp, brush, 1))
                {
                    shape.ZIndex = 1;
                    content.Children.Add(shape);
                }
            }

            Canvas.SetLeft(content, _panX);
            Canvas.SetTop(content, _panY);
            PreviewWorld.Children.Add(content);
        }

        private void OnCreate(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(MapName))
            {
                NameWarning.IsVisible = true;
                NameInput.Focus();
                return;
            }
            Accepted = true;
            Close();
        }

        private void OnCancel(object? sender, RoutedEventArgs e)
        {
            Accepted = false;
            Close();
        }
    }
}
