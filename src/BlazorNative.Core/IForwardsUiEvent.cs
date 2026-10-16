namespace BlazorNative.Core;

/// <summary>
/// Implemented by a component that forwards a UI event to a handler the app supplied,
/// so the renderer can name the app's handler rather than the wrapper's own.
/// </summary>
internal interface IForwardsUiEvent
{
    /// <summary>The app's delegate behind <paramref name="eventName"/>, when
    /// <paramref name="attached"/>, the delegate the renderer recorded for the event, is the
    /// component's own forwarding handler; otherwise null, as when the app supplied none.</summary>
    Delegate? ForwardedHandler(string eventName, Delegate attached);
}
