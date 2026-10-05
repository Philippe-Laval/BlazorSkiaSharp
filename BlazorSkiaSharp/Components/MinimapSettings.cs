using Microsoft.JSInterop;
using SkiaSharp;

namespace BlazorSkiaSharp.Components;

/// <summary>Which corner of the stage the minimap is anchored to.</summary>
public enum MinimapCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>Layout of the minimap: its size, the corner it is anchored to, and visibility.</summary>
public sealed record MinimapSettings(
    int Width = 260,
    int Height = 170,
    MinimapCorner Corner = MinimapCorner.BottomRight,
    bool Expanded = true)
{
    public const string StorageKey = "blazorskia.graph.minimap";

    /// <summary>Smallest size worth showing; below this the graph is unreadable.</summary>
    public const int MinWidth = 140;

    public const int MinHeight = 100;

    /// <summary>Upper bound, so a drag cannot grow the minimap over the whole stage.</summary>
    public const int MaxWidth = 720;

    public const int MaxHeight = 520;

    /// <summary>Forces the size inside the supported range.</summary>
    public MinimapSettings Clamped() => this with
    {
        Width = Math.Clamp(Width, MinWidth, MaxWidth),
        Height = Math.Clamp(Height, MinHeight, MaxHeight),
        Corner = Enum.IsDefined(Corner) ? Corner : MinimapCorner.BottomRight,
    };

    /// <summary>
    /// Compact form for local storage. Plain numbers rather than JSON to keep this free of
    /// a serializer dependency, and to make bad data trivial to reject.
    /// </summary>
    public string Serialize() => string.Join(',', Width, Height, (int)Corner, Expanded ? 1 : 0);

    public static MinimapSettings Deserialize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new MinimapSettings();

        var parts = value.Split(',');

        // Exactly four fields: this comes from local storage, so anything else is junk
        // rather than something to be lenient about.
        if (parts.Length != 4 ||
            !int.TryParse(parts[0], out var width) ||
            !int.TryParse(parts[1], out var height) ||
            !int.TryParse(parts[2], out var corner) ||
            !int.TryParse(parts[3], out var expanded) ||
            !Enum.IsDefined(typeof(MinimapCorner), corner) ||
            expanded is not (0 or 1))
        {
            return new MinimapSettings();
        }

        return new MinimapSettings(width, height, (MinimapCorner)corner, expanded == 1).Clamped();
    }

    /// <summary>Converts to a size, ready to hand to the viewport.</summary>
    public SKSize ToSize() => new(Width, Height);
}

/// <summary>
/// Persists <see cref="MinimapSettings"/> in local storage so the minimap comes back the
/// way the user left it. Storage can be unavailable (private mode, or a browser that
/// blocks it), in which case everything degrades to the defaults.
/// </summary>
public static class MinimapSettingsStore
{
    public static MinimapSettings Load(IJSRuntime js)
    {
        try
        {
            // Synchronous so the very first paint already uses the stored size, which
            // avoids a flash of the default layout. Only available in WebAssembly.
            if (js is not IJSInProcessRuntime sync)
                return new MinimapSettings();

            return MinimapSettings.Deserialize(
                sync.Invoke<string?>("localStorage.getItem", MinimapSettings.StorageKey));
        }
        catch (JSException)
        {
            return new MinimapSettings();
        }
        catch (InvalidOperationException)
        {
            // No JS runtime yet, for example during prerendering.
            return new MinimapSettings();
        }
    }

    public static void Save(IJSRuntime js, MinimapSettings settings) => _ = SaveAsync(js, settings);

    private static async Task SaveAsync(IJSRuntime js, MinimapSettings settings)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", MinimapSettings.StorageKey, settings.Serialize());
        }
        catch (JSException)
        {
            // Storage is full or blocked: the layout just will not be remembered.
        }
        catch (TaskCanceledException)
        {
        }
    }
}
