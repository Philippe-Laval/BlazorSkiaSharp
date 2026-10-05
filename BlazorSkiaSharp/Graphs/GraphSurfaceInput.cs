using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SkiaSharp;

namespace BlazorSkiaSharp.Graphs;

/// <summary>Pointer position reported by the browser, relative to the tracked surface.</summary>
/// <param name="X">Horizontal position.</param>
/// <param name="Y">Vertical position.</param>
/// <param name="PointerId">Identifier of the pointer, so several fingers can be followed.</param>
public readonly record struct PointerSample(float X, float Y, long PointerId);

/// <summary>A wheel or trackpad scroll reported by the browser.</summary>
/// <param name="X">Horizontal position, relative to the surface.</param>
/// <param name="Y">Vertical position, relative to the surface.</param>
/// <param name="DeltaY">Vertical scroll amount, positive when scrolling down.</param>
/// <param name="DeltaMode">0 for pixels, 1 for lines, 2 for pages.</param>
/// <param name="CtrlKey">True for the trackpad pinch gesture.</param>
public readonly record struct WheelSample(float X, float Y, double DeltaY, long DeltaMode, bool CtrlKey);

/// <summary>
/// Forwards pointer, wheel and keyboard input from an HTML element to .NET.
/// </summary>
/// <remarks>
/// The input is wired up in JavaScript rather than with <c>@onclick</c> style attributes
/// because the drawing surface is a canvas rendered by <c>SKCanvasView</c>, which does not
/// expose event parameters, so unmatched attributes cannot carry Blazor event callbacks.
/// Going through JavaScript also gives what a canvas viewer needs: non passive wheel
/// listeners so the page never scrolls, window level move tracking so a drag survives
/// leaving the element, and one listener per finger so a second finger can pinch.
/// </remarks>
public sealed class GraphSurfaceInput : IAsyncDisposable
{
    private const string ModulePath = "./js/graph-interactions.js";

    private readonly IJSRuntime _js;
    private readonly ElementReference _element;

    private IJSObjectReference? _module;
    private DotNetObjectReference<GraphSurfaceInput>? _self;
    private bool _attached;
    private bool _disposed;

    public GraphSurfaceInput(IJSRuntime js, ElementReference element)
    {
        _js = js;
        _element = element;
    }

    /// <summary>Raised when a pointer goes down on the surface.</summary>
    public event Action<PointerSample>? PointerPressed;

    /// <summary>Raised while a drag is in progress, even outside the surface.</summary>
    public event Action<PointerSample>? PointerMoved;

    /// <summary>Raised when a drag finishes, with the id of the pointer that was released.</summary>
    public event Action<long>? PointerReleased;

    /// <summary>Raised on every wheel or trackpad scroll over the surface.</summary>
    public event Action<WheelSample>? Wheel;

    /// <summary>Raised for the shortcut keys the surfaces care about.</summary>
    public event Action<string>? KeyPressed;

    /// <summary>
    /// Starts listening. <paramref name="focusable"/> makes the element focusable so it
    /// receives keyboard shortcuts, <paramref name="stopPropagation"/> keeps nested
    /// surfaces from also handling the same gesture.
    /// </summary>
    public async Task AttachAsync(bool focusable = false, bool stopPropagation = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var module = await GetModuleAsync();
        _self ??= DotNetObjectReference.Create(this);

        await module.InvokeVoidAsync("attachSurface", _self, _element, stopPropagation, focusable);
        _attached = true;
    }

    /// <summary>Starts following the pointer at window level, so a drag can leave the surface.</summary>
    public async Task BeginDragAsync(string cursor = "grabbing")
    {
        if (!_attached)
            return;

        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("beginDrag", _element, cursor);
    }

    [JSInvokable]
    public void OnPointerDown(double x, double y, long pointerId)
        => PointerPressed?.Invoke(new PointerSample((float)x, (float)y, pointerId));

    [JSInvokable]
    public void OnPointerMove(double x, double y, long pointerId)
        => PointerMoved?.Invoke(new PointerSample((float)x, (float)y, pointerId));

    [JSInvokable]
    public void OnPointerUp(long pointerId) => PointerReleased?.Invoke(pointerId);

    [JSInvokable]
    public void OnWheel(double x, double y, double deltaY, long deltaMode, bool ctrlKey)
        => Wheel?.Invoke(new WheelSample((float)x, (float)y, deltaY, deltaMode, ctrlKey));

    [JSInvokable]
    public void OnKeyDown(string key) => KeyPressed?.Invoke(key);

    private async Task<IJSObjectReference> GetModuleAsync()
        => _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_module is not null)
        {
            try
            {
                if (_attached)
                    await _module.InvokeVoidAsync("detachSurface", _element);

                await _module.InvokeVoidAsync("endDrag");
                await _module.DisposeAsync();
            }
            catch (JSException)
            {
            }
            catch (ObjectDisposedException)
            {
            }

            _module = null;
        }

        _self?.Dispose();
        _self = null;
        _attached = false;
    }
}
