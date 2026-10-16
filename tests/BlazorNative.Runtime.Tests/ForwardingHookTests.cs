using BlazorNative.Components;
using Microsoft.AspNetCore.Components;

namespace BlazorNative.Runtime.Tests;

// Phase 17.4: the accessor that reads the app's delegate out of an EventCallback,
// so the slow-handler warning can name the app's method behind a Bn wrapper.
//
// WHAT THESE PINS DO NOT COVER:
//  - A multicast or wrapped delegate. The facts use a single unwrapped method group, so
//    Of returning the first-or-only target is not pinned for a combined delegate.
//  - Wiring the hook into the renderer (NativeRenderer.HandlerCallSite.Resolve). That is
//    Task 3's subject; nothing here proves the warning names the app's handler.
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
}
