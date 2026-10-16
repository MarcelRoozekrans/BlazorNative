using BlazorNative.Components;
using BlazorNative.Core;
using Microsoft.AspNetCore.Components;

namespace BlazorNative.Runtime.Tests;

// Phase 17.4: the accessor that reads the app's delegate out of an EventCallback,
// so the slow-handler warning can name the app's method behind a Bn wrapper.
//
// WHAT THESE PINS DO NOT COVER:
//  - A multicast or wrapped delegate. The facts use a single unwrapped method group, so
//    Of returning the first-or-only target is not pinned for a combined delegate.
//  - Wiring the hook into the renderer (NativeRenderer.HandlerCallSite.Resolve), and the
//    positive half of each wrapper's own-handler check. SlowHandlerWarningTests pins both,
//    through the delegate the renderer records from each wrapper's EventCallback.
// The instantiations covered are string, bool, int and float: the ones the Bn wrappers use.
public sealed class ForwardingHookTests
{
    private sealed class Receiver : IHandleEvent
    {
        public Task HandleEventAsync(EventCallbackWorkItem item, object? arg) => Task.CompletedTask;
    }

    private static void AppMethod(string value) { }
    private static void AppBoolMethod(bool value) { }
    private static void AppIntMethod(int value) { }
    private static void AppFloatMethod(float value) { }

    [Fact]
    public void EventCallbackDelegate_ReturnsTheAppsMethod()
    {
        var callback = EventCallback.Factory.Create<string>(new Receiver(), AppMethod);

        Assert.Equal(nameof(AppMethod), EventCallbackDelegate.Of(callback)!.Method.Name);
    }

    [Fact]
    public void EventCallbackDelegate_IsNullForAnEmptyCallback()
    {
        Assert.Null(EventCallbackDelegate.Of(default(EventCallback<string>)));
    }

    [Fact]
    public void EventCallbackDelegate_WorksForBool()
    {
        var callback = EventCallback.Factory.Create<bool>(new Receiver(), AppBoolMethod);

        Assert.True(EventCallbackDelegate.Of(callback)?.Method.Name == nameof(AppBoolMethod), "positive half: a bool callback did not return the app's method");
        Assert.True(EventCallbackDelegate.Of(default(EventCallback<bool>)) is null, "empty half: a default bool callback did not return null");
    }

    [Fact]
    public void EventCallbackDelegate_WorksForInt()
    {
        var callback = EventCallback.Factory.Create<int>(new Receiver(), AppIntMethod);

        Assert.True(EventCallbackDelegate.Of(callback)?.Method.Name == nameof(AppIntMethod), "positive half: an int callback did not return the app's method");
        Assert.True(EventCallbackDelegate.Of(default(EventCallback<int>)) is null, "empty half: a default int callback did not return null");
    }

    [Fact]
    public void EventCallbackDelegate_WorksForFloat()
    {
        var callback = EventCallback.Factory.Create<float>(new Receiver(), AppFloatMethod);

        Assert.True(EventCallbackDelegate.Of(callback)?.Method.Name == nameof(AppFloatMethod), "positive half: a float callback did not return the app's method");
        Assert.True(EventCallbackDelegate.Of(default(EventCallback<float>)) is null, "empty half: a default float callback did not return null");
    }

    /// <summary>A wrapper hands over the app's delegate only for its own forwarding
    /// handler. Any other delegate attached to it, such as BnModal's content-box tap,
    /// gets null even though the app's callback is set.</summary>
    [Fact]
    public void TheHook_IsNullForADelegateThatIsNotTheWrappersOwn()
    {
        var receiver = new Receiver();
        var modal = new BnModal { VisibleChanged = EventCallback.Factory.Create<bool>(receiver, AppBoolMethod) };
        var input = new BnInput { ValueChanged = EventCallback.Factory.Create<string>(receiver, AppMethod) };
        var checkbox = new BnCheckbox { CheckedChanged = EventCallback.Factory.Create<bool>(receiver, AppBoolMethod) };
        var picker = new BnPicker { SelectedIndexChanged = EventCallback.Factory.Create<int>(receiver, AppIntMethod) };
        var slider = new BnSlider { ValueChanged = EventCallback.Factory.Create<float>(receiver, AppFloatMethod) };
        var toggle = new BnSwitch { CheckedChanged = EventCallback.Factory.Create<bool>(receiver, AppBoolMethod) };
        // Each probe has the SAME delegate type as the wrapper's own handler, so a check
        // that compared types alone would accept it: only identity tells them apart.
        Func<Task> notDismiss = () => Task.CompletedTask;
        Func<ChangeEventArgs, Task> notChange = _ => Task.CompletedTask;

        Assert.True(((IForwardsUiEvent)modal).ForwardedHandler("click", notDismiss) is null,
            "BnModal returned the app's delegate for a Func<Task> that is not its HandleDismissRequest");
        Assert.True(((IForwardsUiEvent)input).ForwardedHandler("change", notChange) is null,
            "BnInput returned the app's delegate for a Func<ChangeEventArgs, Task> that is not its HandleChange");
        Assert.True(((IForwardsUiEvent)checkbox).ForwardedHandler("change", notChange) is null,
            "BnCheckbox returned the app's delegate for a Func<ChangeEventArgs, Task> that is not its HandleChange");
        Assert.True(((IForwardsUiEvent)picker).ForwardedHandler("change", notChange) is null,
            "BnPicker returned the app's delegate for a Func<ChangeEventArgs, Task> that is not its HandleChange");
        Assert.True(((IForwardsUiEvent)slider).ForwardedHandler("change", notChange) is null,
            "BnSlider returned the app's delegate for a Func<ChangeEventArgs, Task> that is not its HandleChange");
        Assert.True(((IForwardsUiEvent)toggle).ForwardedHandler("change", notChange) is null,
            "BnSwitch returned the app's delegate for a Func<ChangeEventArgs, Task> that is not its HandleChange");
    }
}
