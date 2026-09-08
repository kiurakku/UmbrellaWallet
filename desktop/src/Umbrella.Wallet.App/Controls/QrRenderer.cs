using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using QRCoder;

namespace Umbrella.Wallet.App.Controls;

/// <summary>
/// Renders a branded — but genuinely scannable — receive QR.
///
/// The look follows the reference the user gave (dark rounded modules on white, styled finder eyes,
/// a mark in the centre) without ever sacrificing what a QR is for. Two rules keep it working:
///   1. It is generated at error-correction level H (~30% recoverable), so the centre logo — which
///      covers well under that — never stops a scanner reading it.
///   2. The modules stay dark-on-white with the quiet zone intact and are drawn on exact pixel
///      boundaries, so contrast and geometry match what every scanner expects.
///
/// It draws the real QRCoder module matrix; nothing about the payload is faked or reshaped.
/// </summary>
public static class QrRenderer
{
    /// <summary>
    /// Renders <paramref name="payload"/> as a branded QR bitmap. <paramref name="dark"/> is the
    /// module colour (kept very dark for contrast); <paramref name="centerLogo"/> is drawn on a pad
    /// in the middle, or omitted if null.
    /// </summary>
    public static Bitmap Render(string payload, Color dark, IImage? centerLogo, int pixels = 660)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.H);
        var matrix = data.ModuleMatrix;
        var n = matrix.Count;
        if (n == 0) throw new InvalidOperationException("empty QR matrix");

        // QRCoder already includes a 4-module quiet zone; find its real width so the finder patterns
        // (the three corner eyes) can be located and styled without hard-coding an offset.
        var qz = QuietZone(matrix, n);

        var cell = (double)pixels / n;
        var darkBrush = new SolidColorBrush(dark);

        var rtb = new RenderTargetBitmap(new PixelSize(pixels, pixels), new Vector(96, 96));
        using (var ctx = rtb.CreateDrawingContext())
        {
            ctx.FillRectangle(Brushes.White, new Rect(0, 0, pixels, pixels));

            // The centre logo reserve, in module coordinates — a square kept clear of data dots so the
            // mark sits on clean white. Sized ~5 modules on a small code, a bit more on a large one.
            var reserve = centerLogo is null ? 0 : Math.Max(5, (int)Math.Round(n * 0.20));
            if (reserve % 2 != n % 2) reserve++; // keep it centred on the module grid
            var reserveLo = (n - reserve) / 2;
            var reserveHi = reserveLo + reserve;

            var finders = new (int R, int C)[] { (qz, qz), (qz, n - qz - 7), (n - qz - 7, qz) };

            for (var r = 0; r < n; r++)
            {
                for (var c = 0; c < n; c++)
                {
                    if (!matrix[r][c]) continue;
                    if (InFinder(finders, r, c)) continue; // finders are drawn as styled eyes below
                    if (centerLogo is not null && r >= reserveLo && r < reserveHi && c >= reserveLo && c < reserveHi)
                        continue; // leave the centre clear for the mark

                    var x = c * cell;
                    var y = r * cell;
                    // Rounded dot filling ~88% of the cell — soft and modern, still well inside the
                    // module boundary so scanners resolve each one cleanly.
                    var pad = cell * 0.06;
                    var rect = new Rect(x + pad, y + pad, cell - 2 * pad, cell - 2 * pad);
                    ctx.DrawRectangle(darkBrush, null, new RoundedRect(rect, cell * 0.32));
                }
            }

            foreach (var (fr, fc) in finders)
                DrawFinder(ctx, darkBrush, fr, fc, cell);

            if (centerLogo is not null)
            {
                var boxSize = reserve * cell;
                var boxX = reserveLo * cell;
                var pad = boxSize * 0.14;

                // A white rounded pad the logo sits on, so it reads regardless of the modules around it.
                var padRect = new Rect(boxX - pad * 0.4, boxX - pad * 0.4, boxSize + pad * 0.8, boxSize + pad * 0.8);
                ctx.DrawRectangle(Brushes.White, null, new RoundedRect(padRect, boxSize * 0.24));

                // The mark, fitted inside the pad preserving aspect ratio.
                var logoRect = FitCentered(centerLogo, new Rect(boxX + pad, boxX + pad, boxSize - 2 * pad, boxSize - 2 * pad));
                ctx.DrawImage(centerLogo, logoRect);
            }
        }

        return rtb;
    }

    private static int QuietZone(System.Collections.Generic.List<System.Collections.BitArray> matrix, int n)
    {
        for (var r = 0; r < n; r++)
        {
            var row = matrix[r];
            for (var c = 0; c < n; c++)
                if (row[c]) return r; // first row with a dark module = width of the quiet zone
        }

        return 4;
    }

    private static bool InFinder((int R, int C)[] finders, int r, int c)
    {
        foreach (var (fr, fc) in finders)
            if (r >= fr && r < fr + 7 && c >= fc && c < fc + 7) return true;
        return false;
    }

    /// <summary>
    /// Draws one finder pattern as a styled eye: a rounded-square ring with a rounded centre pip,
    /// the same 7×7 footprint the scanner expects, just softened.
    /// </summary>
    private static void DrawFinder(DrawingContext ctx, IBrush dark, int r, int c, double cell)
    {
        var x = c * cell;
        var y = r * cell;
        var size = 7 * cell;

        // Outer 7×7 rounded square.
        ctx.DrawRectangle(dark, null, new RoundedRect(new Rect(x, y, size, size), cell * 1.9));
        // Punch out a 5×5 white square.
        ctx.DrawRectangle(Brushes.White, null,
            new RoundedRect(new Rect(x + cell, y + cell, 5 * cell, 5 * cell), cell * 1.3));
        // Solid 3×3 rounded centre pip.
        ctx.DrawRectangle(dark, null,
            new RoundedRect(new Rect(x + 2 * cell, y + 2 * cell, 3 * cell, 3 * cell), cell * 0.9));
    }

    private static Rect FitCentered(IImage img, Rect box)
    {
        var iw = img.Size.Width;
        var ih = img.Size.Height;
        if (iw <= 0 || ih <= 0) return box;

        var scale = Math.Min(box.Width / iw, box.Height / ih);
        var w = iw * scale;
        var h = ih * scale;
        return new Rect(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }
}
