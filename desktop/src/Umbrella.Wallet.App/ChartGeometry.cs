using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Umbrella.Wallet.App;

/// <summary>
/// Chart lines as smooth curves (the gold design) instead of straight segments between samples.
///
/// A Catmull-Rom spline through the points, written as cubic Béziers. The control points are kept
/// inside the band of the two samples they join, so the curve never overshoots — it cannot draw a
/// price above the high or below the low that the data actually reached. It passes through every
/// sample, so the crosshair's dot still sits on the line.
/// </summary>
public static class ChartGeometry
{
    /// <summary>The line through <paramref name="points"/>, or null for fewer than two.</summary>
    public static Geometry? Line(IReadOnlyList<Point> points)
    {
        if (points.Count < 2) return null;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(points[0], false);
            Curve(ctx, points);
            ctx.EndFigure(false);
        }

        return g;
    }

    /// <summary>
    /// The filled area under a line: the curve through <paramref name="points"/>, then straight down to
    /// <paramref name="baseline"/> and back along it.
    /// </summary>
    public static Geometry? Area(IReadOnlyList<Point> points, double baseline)
    {
        if (points.Count < 2) return null;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(points[0].X, baseline), true);
            ctx.LineTo(points[0]);
            Curve(ctx, points);
            ctx.LineTo(new Point(points[^1].X, baseline));
            ctx.EndFigure(true);
        }

        return g;
    }

    private static void Curve(StreamGeometryContext ctx, IReadOnlyList<Point> p)
    {
        for (var i = 0; i < p.Count - 1; i++)
        {
            var p0 = p[Math.Max(i - 1, 0)];
            var p1 = p[i];
            var p2 = p[i + 1];
            var p3 = p[Math.Min(i + 2, p.Count - 1)];

            var lo = Math.Min(p1.Y, p2.Y);
            var hi = Math.Max(p1.Y, p2.Y);
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, Math.Clamp(p1.Y + (p2.Y - p0.Y) / 6, lo, hi));
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, Math.Clamp(p2.Y - (p3.Y - p1.Y) / 6, lo, hi));
            ctx.CubicBezierTo(c1, c2, p2);
        }
    }
}

/// <summary>A point list → its smooth line (see <see cref="ChartGeometry"/>).</summary>
public sealed class SmoothLineConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable<Point> points ? ChartGeometry.Line(points.ToList()) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// An area polygon as the charts build it — the line's points followed by the two baseline corners —
/// → the same shape with a smooth top edge.
/// </summary>
public sealed class SmoothAreaConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<Point> all) return null;
        var points = all.ToList();
        if (points.Count < 4) return null;
        var line = points.Take(points.Count - 2).ToList();
        return ChartGeometry.Area(line, points[^1].Y);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
