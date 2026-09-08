using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Umbrella.Wallet.App.Controls;

/// <summary>
/// A lightweight looping frame animation that composites in the visual tree — so, unlike a native
/// video surface (VLC/GStreamer), it can be clipped to rounded corners and have content drawn on top.
/// It cycles a set of embedded PNG frames (Assets/portfolio/rain/frame_NNN.png, keyed to alpha so only
/// the rain streaks show) with a dispatcher timer. Frames are decoded once and shared across instances;
/// the timer runs only while the control is actually visible, so turning the animation off in settings
/// (which collapses the control) costs nothing.
/// </summary>
public sealed class FrameAnimation : Image
{
    private static Bitmap[]? _frames;
    private static readonly object _gate = new();
    private DispatcherTimer? _timer;
    private int _index;

    public FrameAnimation()
    {
        // Fill the card and crop, matching how the still photo behaves.
        Stretch = Avalonia.Media.Stretch.UniformToFill;
    }

    private static Bitmap[] LoadFrames()
    {
        if (_frames is not null) return _frames;
        lock (_gate)
        {
            if (_frames is not null) return _frames;
            var list = new List<Bitmap>(48);
            for (var n = 1; n <= 300; n++)
            {
                var uri = new Uri($"avares://Umbrella.Wallet.App/Assets/portfolio/rain/frame_{n:D3}.png");
                if (!AssetLoader.Exists(uri)) break;
                using var stream = AssetLoader.Open(uri);
                list.Add(new Bitmap(stream));
            }
            _frames = list.ToArray();
            return _frames;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var frames = LoadFrames();
        if (frames.Length == 0) return;
        Source ??= frames[0];
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(83) }; // ~12 fps
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // Do no work while hidden (e.g. the toggle is off, or the portfolio page isn't shown).
        if (!IsEffectivelyVisible) return;
        var frames = _frames;
        if (frames is null || frames.Length == 0) return;
        _index = (_index + 1) % frames.Length;
        Source = frames[_index];
    }
}
