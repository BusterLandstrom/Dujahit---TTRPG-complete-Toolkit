using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Dujahit.Models.UI
{
    public enum GridKind { Squares, Hexes }

    public static class GridOverlay
    {
        public const double BaseCellPx = 50.0;

        public static double CellFor(double mapScale) => BaseCellPx * (mapScale <= 0 ? 1.0 : mapScale);

        public static Point SnapCenter(double x, double y, double footprintPx, double cell)
        {
            if (cell <= 0) return new Point(x, y);
            var half = footprintPx / 2.0;
            var left = Math.Round((x - half) / cell) * cell;
            var top = Math.Round((y - half) / cell) * cell;
            return new Point(left + half, top + half);
        }

        public static IEnumerable<Shape> Build(GridKind kind, double width, double height, double cell, IBrush brush, double thickness) =>
            kind == GridKind.Hexes
                ? BuildHexes(width, height, cell, brush, thickness)
                : BuildSquares(width, height, cell, brush, thickness);

        private static IEnumerable<Shape> BuildSquares(double width, double height, double cell, IBrush brush, double thickness)
        {
            for (double x = 0; x <= width; x += cell)
                yield return VLine(x, height, brush, thickness);
            for (double y = 0; y <= height; y += cell)
                yield return HLine(y, width, brush, thickness);
        }

        private static IEnumerable<Shape> BuildHexes(double width, double height, double cell, IBrush brush, double thickness)
        {
            var r = cell / 2.0;
            var hStep = r * 1.5;
            var vStep = Math.Sqrt(3) * r;

            int col = 0;
            for (double cx = r; cx - r <= width; cx += hStep, col++)
            {
                var yOffset = (col % 2 == 0) ? 0 : vStep / 2.0;
                for (double cy = r + yOffset; cy - r <= height; cy += vStep)
                    yield return Hexagon(cx, cy, r, brush, thickness);
            }
        }

        private static Polygon Hexagon(double cx, double cy, double r, IBrush brush, double thickness)
        {
            var pts = new Points();
            for (int i = 0; i < 6; i++)
            {
                var ang = Math.PI / 180.0 * (60 * i);
                pts.Add(new Point(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang)));
            }
            return new Polygon { Points = pts, Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false };
        }

        private static Line VLine(double x, double height, IBrush brush, double thickness) =>
            new Line { StartPoint = new Point(x, 0), EndPoint = new Point(x, height), Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false };

        private static Line HLine(double y, double width, IBrush brush, double thickness) =>
            new Line { StartPoint = new Point(0, y), EndPoint = new Point(width, y), Stroke = brush, StrokeThickness = thickness, IsHitTestVisible = false };
    }
}