namespace BlazorNative.Components;

// WHY THIS FILE EXISTS (8.4 decision 3, and the same for the five beside it):
// a `@* ... *@` header in a .razor file is a RAZOR comment. It is consumed by
// the Razor compiler and never reaches the assembly, so it never reaches the XML
// docs, so it never reaches the component reference — BnCheckbox.razor opens
// with forty lines of excellent prose and this type still rendered as a headless
// signature dump on the site.
//
// CS1591 cannot see it either: the Razor source generator emits
// `#pragma warning disable 1591` into the generated file, so Pin 2 — the
// compiler — is structurally blind to a missing type summary on a .razor
// component. That is why the measured coverage gap said 8 while the reference
// had six holes in it.
//
// A `///` on a partial class declaration is a C# doc comment on the real type,
// and it does reach the assembly. Nothing else belongs in this file: the markup,
// the parameters and the code stay in the .razor.
//
// The hole is now pinned as well as filled — ComponentReferenceDriftTests
// asserts every public component carries a type-level <summary> in the shipped
// XML, because the compiler cannot.

/// <summary>
/// A checkbox. Renders as a native <c>CheckBox</c> on Android — and as a
/// <c>UISwitch</c> on iOS, because <b>iOS has no native checkbox</b>.
/// </summary>
/// <remarks>
/// <para>
/// That difference is deliberate and worth understanding before you choose
/// between this and <see cref="BnSwitch"/>: on iOS the two are the same control,
/// so they look identical there and differ only on Android. Both keep the same
/// parameters, the same events and the same layout frames — a custom-drawn iOS
/// checkbox would buy matching pixels at the price of not being native, which is
/// the opposite of the point.
/// </para>
/// <para>
/// It is a leaf with the platform's own intrinsic size, so you do not give it a
/// width or a height. Use <see cref="BnView"/> around it to place or pad it.
/// </para>
/// <example>
/// <code>
/// &lt;BnCheckbox @bind-Checked="_accepted" /&gt;
///
/// @code {
///     private bool _accepted;
/// }
/// </code>
/// </example>
/// </remarks>
public partial class BnCheckbox
{
}
