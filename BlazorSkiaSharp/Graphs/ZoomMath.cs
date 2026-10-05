namespace BlazorSkiaSharp.Graphs;

/// <summary>
/// Converts wheel and trackpad scrolls into zoom factors, shared by the main canvas and
/// the minimap so both feel the same.
/// </summary>
public static class ZoomMath
{
    /// <summary>Mouse wheel: a factor close to 1 applied to a large delta.</summary>
    private const float MouseNotchExponent = 1.0015f;

    /// <summary>Trackpad pinch: small deltas arrive with the control key held down.</summary>
    private const float PinchExponent = 1.01f;

    /// <summary>Wheel units for a "line", used when the browser reports line scrolling.</summary>
    private const float LineHeight = 16f;

    /// <summary>Wheel units for a "page".</summary>
    private const float PageHeight = 400f;

    /// <summary>Zoom factor to apply for a wheel event. Greater than 1 zooms in.</summary>
    /// <param name="deltaY">Vertical scroll amount, positive when scrolling down.</param>
    /// <param name="deltaMode">0 for pixels, 1 for lines, 2 for pages.</param>
    /// <param name="ctrlKey">True for the trackpad pinch gesture.</param>
    public static float FactorFromWheel(double deltaY, long deltaMode, bool ctrlKey)
    {
        // Normalise line and page scrolling into pixels.
        if (deltaMode == 1)
            deltaY *= LineHeight;
        else if (deltaMode == 2)
            deltaY *= PageHeight;

        var exponent = ctrlKey ? PinchExponent : MouseNotchExponent;

        return MathF.Pow(exponent, (float)-deltaY);
    }
}
