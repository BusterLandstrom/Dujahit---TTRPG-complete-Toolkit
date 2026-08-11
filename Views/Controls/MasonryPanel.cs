using Avalonia;
using Avalonia.Controls;
using System;

namespace Dujahit.Views.Controls
{
    // No masonry in Avalonia, so this is it.
    public class MasonryPanel : Panel
    {
        public static readonly StyledProperty<double> ColumnWidthProperty =
            AvaloniaProperty.Register<MasonryPanel, double>(nameof(ColumnWidth), 316);

        public double ColumnWidth
        {
            get => GetValue(ColumnWidthProperty);
            set => SetValue(ColumnWidthProperty, value);
        }

        static MasonryPanel()
        {
            AffectsMeasure<MasonryPanel>(ColumnWidthProperty);
        }

        private int ColumnCount(double available)
        {
            var w = ColumnWidth <= 0 ? 316 : ColumnWidth;
            if (double.IsInfinity(available) || available <= 0) return 1;
            return Math.Max(1, (int)(available / w));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var cols = ColumnCount(availableSize.Width);
            var colW = ColumnWidth;
            var heights = new double[cols];
            foreach (var child in Children)
            {
                child.Measure(new Size(colW, double.PositiveInfinity));
                var c = Shortest(heights);
                heights[c] += child.DesiredSize.Height;
            }
            double tallest = 0;
            foreach (var h in heights) tallest = Math.Max(tallest, h);
            return new Size(cols * colW, tallest);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var cols = ColumnCount(finalSize.Width);
            var colW = ColumnWidth;
            var heights = new double[cols];
            foreach (var child in Children)
            {
                var c = Shortest(heights);
                child.Arrange(new Rect(c * colW, heights[c], colW, child.DesiredSize.Height));
                heights[c] += child.DesiredSize.Height;
            }
            return finalSize;
        }

        private static int Shortest(double[] heights)
        {
            var idx = 0;
            for (var i = 1; i < heights.Length; i++)
                if (heights[i] < heights[idx]) idx = i;
            return idx;
        }
    }
}
