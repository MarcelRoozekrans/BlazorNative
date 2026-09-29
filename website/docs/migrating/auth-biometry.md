---
id: auth-biometry
title: requireAuth means biometry
sidebar_label: requireAuth means biometry
---

# `requireAuth: true` now means biometry — on both shells

**Applies to:** consumers on **0.14.0 or earlier**, upgrading to the first release that contains
this change.
**Package:** `BlazorNative.Device` — `ISecureStorage`. No signature moved.
**Kind of change:** a **runtime behaviour change on iOS only**. Nothing to recompile, nothing to
retype, no analyzer error to chase. Your code builds exactly as it did; what changes is which
authenticator the operating system accepts when it opens an auth-bound secret.

:::tip The short version
`requireAuth: true` means **biometry** — Face ID or Touch ID on Apple, a Class 3 biometric on
Android. It does **not** mean biometry-or-passcode.

On **Android** nothing changes; it was already biometry-only. On **iOS**,
`GetWithAuthAsync` used to accept the **device passcode** as well, and no longer does.
:::

---

## 1. What changed

One token in the Apple shell. `BnSecureStorage`'s authorized read evaluates

```swift
.deviceOwnerAuthenticationWithBiometrics   // biometry
```

where it previously evaluated

```swift
.deviceOwnerAuthentication                 // passcode OR biometry
```

Everything else — the API, the statuses, the wire, the C ABI, the Android shell — is untouched.

## 2. Why

**The Apple shell disagreed with itself.** Writing a secret with `requireAuth: true` attaches a
`kSecAccessControlBiometryCurrentSet` access control to the keychain item: biometry only, and
invalidated if the enrolled set changes. That is the strongest binding the platform offers, and it
is what the write has always declared.

The read then asked the OS for `.deviceOwnerAuthentication`, which is satisfied by the device
passcode *or* biometry. So the effective gate was **weaker than the stored access control
declared**, and nothing in the API surface, the returned `SecureStorageStatus`, or the demo's echo
said so.

This is not a theory about what the OS might do. It was **confirmed on hardware** during the
device-verification run — an iPhone 17 Pro Max on iOS 26, where the `coreauthd` trace showed the
system honouring the read exactly as it was written: a k-of-n mechanism with k = 1 over
`{Passcode, Biometry}`. A secret stored under a biometry-only access control came back after a
passcode entry.

The fix makes the read demand the same authenticator class the write already declared.

## 3. What breaks

**An iOS app that relied on passcode fallback.** If your users could previously satisfy a
`GetWithAuthAsync` prompt by entering the device passcode — because biometry was unavailable,
unenrolled, or locked out — they now cannot. The call resolves
`SecureStorageStatus.AuthFailed` with no value, the same denial-as-data it has always returned for
a cancelled or failed prompt. It does not throw, and it does not hang.

**Nothing changes on Android.** `provisionKey` has always bound the key with
`AUTH_BIOMETRIC_STRONG` and the prompt has always offered `Authenticators.BIOMETRIC_STRONG`, so
Android was biometry-only before this change and is biometry-only after it. If your app behaves
correctly on Android today, this is what iOS now does too.

**Stored secrets do not need re-provisioning.** The *write* side is unchanged — items written by
an older build already carry the biometry-only access control. Only the read caught up with it.

There is deliberately **no passcode fallback option**, and none is planned. A per-call or
per-app switch would put the two shells back into disagreement, which is the defect this change
exists to remove.

## 4. Lockout — what happens, and why it is not a trap

If biometry locks out — too many failed attempts — an auth-bound secret is **unreadable until the
device is unlocked with the passcode**, which is what resets biometry. After that, the secret
reads normally.

This is worth stating plainly because the obvious worry is the wrong one: removing the passcode
fallback does **not** make a secret permanently unreachable. The passcode still recovers access;
it just recovers it by unlocking the *device* rather than by opening the *item*. That is exactly
how Android already behaves, so the recovery story is now the same on both platforms.

The one case that genuinely loses data is unchanged by this release and predates it: changing the
enrolled biometric set invalidates the item, because `BiometryCurrentSet` is what the write
attaches. Treat an auth-bound secret as re-creatable — a cached token, not the only copy of
something.

## 5. What to do

1. **Nothing, if you already test on Android.** The iOS behaviour now matches it.
2. **Check any user-facing copy** that promises a passcode alternative on iOS, and any support
   flow that told users to fall back to the passcode.
3. **Handle `AuthFailed` as a first-class outcome** — it was always the documented result of a
   refused gate, and it now arrives in one more situation.
4. **Have a recovery path that does not depend on the secret**, for the lockout window and for
   re-enrolment. This was already true; it is more visible now.

## 6. Why this lands before 1.0

The device surface **freezes at 1.0**. Changing what `requireAuth: true` means after that point
would be a breaking change to a stable API, and the realistic outcome would be that it never
changes at all — leaving a gate that is quietly weaker than the access control it advertises.
Pre-1.0 is the cheap window, and this is what it is for.

## 7. How it is held

The one-token fix is the small half. The guard is `src/auth-semantics.json`, which declares every
site in both shells that gates or probes authenticated access, each with the authenticator token
it must use and a written reason, and `AuthSemanticsDriftTests`, which reads the shell sources and
asserts, among other things:

- every declared site still carries its declared token;
- every authenticator token occurring anywhere in either shell's non-test source is declared, or
  explicitly ignored with a reason — so **one more opinion** about what counts as authentication
  fails the build rather than joining quietly;
- no shell widens the gate through a platform call that accepts the device credential without
  naming an authenticator token at all;
- the scan is not vacuous: it saw both shells and found occurrences.

No name generator could have caught the original defect, because the names were already fine and
the **meanings** differed. That is why this one is pinned by a differential check over declared
semantics rather than by codegen.
