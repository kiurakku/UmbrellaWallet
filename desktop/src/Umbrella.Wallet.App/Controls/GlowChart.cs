using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Umbrella.Wallet.App.Controls;

/// <summary>
/// A line of light (the gold design): a series drawn as a smooth curve in the theme's accent, with a
/// soft glow under it, a wash of the accent fading down to the baseline, a lit dot where it ends and
/// a small label beside that dot. Sizes itself to whatever space it is given.
///
/// It draws what it is handed and nothing else — no smoothing of the data, no invented points. The
/// curve passes through every value (see <see cref="ChartGeometry"/>).
/// </summary>
public sealed class GlowChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<GlowChart, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<Color> LineColorProperty =
        AvaloniaProperty.Register<GlowChart, Color>(nameof(LineColor), Colors.Gold);

    public static readonly StyledProperty<string?> EndLabelProperty =
        AvaloniaProperty.Register<GlowChart, string?>(nameof(EndLabel));

    public static readonly StyledProperty<string?> EndCaptionProperty =
        AvaloniaProperty.Register<GlowChart, string?>(nameof(EndCaption));

    public static readonly StyledProperty<IBrush?> LabelBackgroundProperty =
        AvaloniaProperty.Register<GlowChart, IBrush?>(nameof(LabelBackground));

    public static readonly StyledProperty<IBrush?> LabelBorderProperty =
        AvaloniaProperty.Register<GlowChart, IBrush?>(nameof(LabelBorder));

    public static readonly StyledProperty<IBrush?> LabelForegroundProperty =
        AvaloniaProperty.Register<GlowChart, IBrush?>(nameof(LabelForeground));

    public static readonly StyledProperty<IBrush?> CaptionForegroundProperty =
        AvaloniaProperty.Register<GlowChart, IBrush?>(nameof(CaptionForeground));

    private static readonly Typeface LabelFace = new(new FontFamily("Segoe UI Variable, Segoe UI"), FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface CaptionFace = new(new FontFamily("Segoe UI Variable, Segoe UI"));

    static GlowChart()
    {
        AffectsRender<GlowChart>(ValuesProperty, LineColorProperty, EndLabelProperty, EndCaptionProperty,
            LabelBackgroundProperty, LabelBorderProperty, LabelForegroundProperty, CaptionForegroundProperty);
    }

    public IReadOnlyList<double>? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Color LineColor { get => GetValue(LineColorProperty); set => SetValue(LineColorProperty, value); }
    public string? EndLabel { get => GetValue(EndLabelProperty); set => SetValue(EndLabelProperty, value); }
    public string? EndCaption { get => GetValue(EndCaptionProperty); set => SetValue(EndCaptionProperty, value); }
    public IBrush? LabelBackground { get => GetValue(LabelBackgroundProperty); set => SetValue(LabelBackgroundProperty, value); }
    public IBrush? LabelBorder { get => GetValue(LabelBorderProperty); set => SetValue(LabelBorderProperty, value); }
    public IBrush? LabelForeground { get => GetValue(LabelForegroundProperty); set => SetValue(LabelForegroundProperty, value); }
    public IBrush? CaptionForeground { get => GetValue(CaptionForegroundProperty); set => SetValue(CaptionForegroundProperty, value); }

    public override void Render(DrawingContext ctx)
    {
        var values = Values;
        if (values is null || values.Count < 2) return;

        var w = Bounds.Width;
        var h = Bounds.Height;
        const double padLeft = 4, padRight = 14, padTop = 14, padBottom = 6;
        if (w <= padLeft + padRight || h <= padTop + padBottom) return;

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var v in values)
        {
            min = Math.Min(min, v);
            max = Math.Max(max, v);
        }

        // A flat series (a wallet of stablecoins) is a flat line through the middle, not a divide by zero.
        var range = max - min;
        var flat = range <= Math.Abs(max) * 1e-9 || range <= 0;

        var points = new List<Point>(values.Count);
        var plotW = w - padLeft - padRight;
        var plotH = h - padTop - padBottom;
        for (var i = 0; i < values.Count; i++)
        {
            var x = padLeft + plotW * i / (values.Count - 1);
            var y = flat ? padTop + plotH * 0.5 : padTop + (1 - (values[i] - min) / range) * plotH;
            points.Add(new Point(x, y));
        }

        var c = LineColor;
        Color A(byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

        // The wash under the line.
        var area = ChartGeometry.Area(points, h);
        if (area is not null)
        {
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(A(0x50), 0),
                    new GradientStop(A(0x14), 0.6),
                    new GradientStop(A(0x00), 1),
                },
            };
            ctx.DrawGeometry(fill, null, area);
        }

        // The line: two soft wide strokes for the glow, then the bright one.
        var line = ChartGeometry.Line(points);
        if (line is not null)
        {
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(A(0x14)), 14, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(A(0x2E)), 6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(c), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
        }

        // Where it ends — now.
        var end = points[^1];
        ctx.DrawEllipse(new SolidColorBrush(A(0x38)), null, end, 11, 11);
        ctx.DrawEllipse(new SolidColorBrush(c), new Pen(LabelBackground ?? Brushes.Black, 2), end, 5.5, 5.5);

        if (string.IsNullOrEmpty(EndLabel)) return;

        var label = new FormattedText(EndLabel, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, LabelFace, 13,
            LabelForeground ?? Brushes.White);
        FormattedText? caption = string.IsNullOrEmpty(EndCaption)
            ? null
            : new FormattedText(EndCaption, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, CaptionFace, 10.5,
                CaptionForeground ?? Brushes.Gray);

        var boxW = Math.Max(label.Width, caption?.Width ?? 0) + 22;
        var boxH = label.Height + (caption?.Height ?? 0) + 14;
        var boxX = Math.Clamp(end.X - boxW - 16, 0, Math.Max(0, w - boxW));
        var boxY = end.Y - boxH - 12;
        if (boxY < 0) boxY = Math.Min(h - boxH, end.Y + 14);

        var box = new Rect(boxX, boxY, boxW, boxH);
        ctx.DrawRectangle(LabelBackground, LabelBorder is null ? null : new Pen(LabelBorder, 1), box, 9, 9);
        ctx.DrawText(label, new Point(boxX + 11, boxY + 7));
        if (caption is not null) ctx.DrawText(caption, new Point(boxX + 11, boxY + 7 + label.Height));
    }
}
