using BlazorNative.Components;
using Microsoft.AspNetCore.Components;

namespace BlazorNative.Runtime.Tests;

// Phase 17.4: the accessor that reads the app's delegate out of an EventCallback,
// so the slow-handler warning can name the app's method behind a Bn wrapper.
public sealed class ForwardingHookTests
{
    private sealed class Receiver : IHandleEvent
    {
        public Task HandleEventAsync(EventCallbackWorkItem item, object? arg) => Task.CompletedTask;
    }

    private static void AppMethod(string value) { }
    private static void AppBoolMethod(bool value) { }

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
    public void EventCallbackDelegate_WorksForASecondInstantiation()
    {
        var callback = EventCallback.Factory.Create<bool>(new Receiver(), AppBoolMethod);

        Assert.Equal(nameof(AppBoolMethod), EventCallbackDelegate.Of(callback)!.Method.Name);
        Assert.Null(EventCallbackDelegate.Of(default(EventCallback<bool>)));
    }
}
