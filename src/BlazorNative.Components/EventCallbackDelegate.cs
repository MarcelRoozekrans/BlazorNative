using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;

namespace BlazorNative.Components;

/// <summary>Reads the delegate Blazor keeps inside an <see cref="EventCallback{TValue}"/>.</summary>
internal static class EventCallbackDelegate
{
    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "DelegateMissing")]
    private static extern ref MulticastDelegate? DelegateField<T>(ref EventCallback<T> callback);

    /// <summary>The app's delegate, or null when the callback has none.</summary>
    internal static Delegate? Of<T>(EventCallback<T> callback)
        => callback.HasDelegate ? DelegateField(ref callback) : null;
}
