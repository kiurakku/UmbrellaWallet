using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Umbrella.Wallet.App.Controls;

/// <summary>Which Umbrella logo a <see cref="BrandMark"/> draws.</summary>
public enum BrandKind
{
    /// <summary>The folded umbrella-and-gem mark — the app icon's own artwork.</summary>
    Mark,

    /// <summary>The umbrella with the UMBRELLA name under it.</summary>
    Wordmark,

    /// <summary>The umbrella on its own.</summary>
    Glyph,

    /// <summary>The canopy seen from above: light panels in the accent, dark panels between them.</summary>
    Canopy,
}

/// <summary>
/// An Umbrella logo in the colours of whatever theme is on — gold on the default theme, blue on Navy,
/// and so on. Only the desktop icon (umbrella.ico) keeps fixed colours.
///
/// Each logo is a silhouette used as a mask over the theme's <c>UmMarkFill</c> gradient, plus, where
/// the artwork has them, a layer that stays dark (the canopy's dark panels) or shades it (the mark's
/// facets). Masks are drawn with high-quality scaling: the sources are large, and nearest-neighbour
/// sampling turned their edges into steps.
/// </summary>
public sealed class BrandMark : Panel
{
    public static readonly StyledProperty<bool> GlowProperty =
        AvaloniaProperty.Register<BrandMark, bool>(nameof(Glow), defaultValue: true);

    public static readonly StyledProperty<BrandKind> KindProperty =
        AvaloniaProperty.Register<BrandMark, BrandKind>(nameof(Kind), defaultValue: BrandKind.Mark);

    private static readonly Dictionary<string, Bitmap> Cache = [];

    private readonly DropShadowEffect _glow = new() { OffsetX = 0, OffsetY = 0, BlurRadius = 22, Opacity = 0.5 };

    public BrandMark()
    {
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        _glow.Bind(DropShadowEffect.ColorProperty, this.GetResourceObservable("UmGlow"));
        Effect = _glow;
        IsHitTestVisible = false;
        Build();
    }

    /// <summary>The accent glow behind the logo. Off where it sits on a busy surface or in a tile.</summary>
    public bool Glow
    {
        get => GetValue(GlowProperty);
        set => SetValue(GlowProperty, value);
    }

    public BrandKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GlowProperty) Effect = Glow ? _glow : null;
        else if (change.Property == KindProperty) Build();
    }

    private void Build()
    {
        Children.Clear();
        switch (Kind)
        {
            case BrandKind.Wordmark:
                AddFill("umbrella-wordmark-shape.png");
                break;
            case BrandKind.Glyph:
                AddFill("umbrella-glyph-shape.png");
                break;
            case BrandKind.Canopy:
                AddImage("umbrella-canopy-base.png");
                AddFill("umbrella-canopy-shape.png");
                break;
            default:
                AddFill("umbrella-mark-shape.png");
                AddImage("umbrella-mark-shade.png");
                break;
        }
    }

    private void AddFill(string mask)
    {
        var fill = new Border { OpacityMask = new ImageBrush(Load(mask)) { Stretch = Stretch.Uniform } };
        fill.Bind(Border.BackgroundProperty, fill.GetResourceObservable("UmMarkFill"));
        Children.Add(fill);
    }

    private void AddImage(string name) =>
        Children.Add(new Image { Source = Load(name), Stretch = Stretch.Uniform });

    private static Bitmap Load(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        var bitmap = new Bitmap(AssetLoader.Open(new Uri($"avares://Umbrella.Wallet.App/Assets/{name}")));
        Cache[name] = bitmap;
        return bitmap;
    }
}
