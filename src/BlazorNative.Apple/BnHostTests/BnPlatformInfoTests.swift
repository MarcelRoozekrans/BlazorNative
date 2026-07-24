// ─────────────────────────────────────────────────────────────────────────────
// BnPlatformInfoTests — Phase 10.0 (#121): the iOS shell must not report Android.
//
// The shared NativeAOT runtime is linked into BOTH native shells' static archives,
// and it used to HARDCODE PlatformKind.Android in NativeShellBridge — so an iOS app
// reported "Android" through the public IMobileBridge.PlatformInfo /
// GetPlatformInfoAsync surface. The fix routes the kind from the shell through the
// init options (bn_init_options.platformInfoKind), exactly like the os string: the
// iOS shell passes BnPlatformKind.iOS, so the runtime reports iOS.
//
// The .NET twin (NativeShellBridgeTests.PlatformInfo_ReportsTheShellsKind_iOS_*)
// proves the runtime SERVES the shell's kind. THIS proves the iOS half of the
// contract on device:
//   1. The ABI carries the kind — bn_init_options grew 24 → 32 bytes with
//      platformInfoKind at offset 24 (the third mirror of the .NET StructLayout +
//      the Kotlin JNA struct; twins: NativeShellBridgeTests.InitOptionsStruct_Is32Bytes
//      + BootSmokeNativeAndroidTest.struct_sizes_match_c_abi).
//   2. The ordinal the iOS shell sends is iOS (2), NOT Android (1) — the exact
//      integer the runtime's Exports.ToPlatformKind decodes.
//   3. The real linked runtime ACCEPTS an iOS-kind init end-to-end (status 0) — the
//      iOS shell boots reporting iOS, not Android.
//
// Pure C-ABI (no mapper / no mount) — fast and deterministic, the BootSmoke shape.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnPlatformInfoTests: XCTestCase {

    /// The ABI third-mirror + the ordinal contract the shell encodes against.
    func testInitOptionsCarriesTheShellsPlatformKind() {
        // The init-INPUT struct grew to 32 bytes; platformInfoKind lands at offset 24
        // (after os@0, apiLevel@8, note@16). Offsets 0/8/16 are unchanged, so an old
        // 24-byte caller still populates every prior field. This is NOT the frozen
        // 80-byte bn_bridge_callbacks — that stays 80 (see BnDriftTests).
        XCTAssertEqual(MemoryLayout<bn_init_options>.size, 32,
                       "bn_init_options must be 32 bytes (os + apiLevel + note + kind)")
        XCTAssertEqual(MemoryLayout<bn_init_options>.offset(of: \.platformInfoKind), 24,
                       "platformInfoKind must be appended at offset 24")

        // PHASE 11.4 GATE C (#155): logLevel at offset 28. THESE TWO ASSERTIONS
        // TOGETHER ARE THE PROOF THAT THE FIELD WAS FREE — the size line above is
        // UNCHANGED from Phase 10.0, because platformInfoKind at 24 was already
        // followed by 4 bytes of tail padding (the struct aligns to 8; it holds
        // pointers). The new int32 lands in bytes that were already allocated, so
        // the init-INPUT struct carries the shell's verbosity at a cost of ZERO
        // bytes and the frozen 80-byte callbacks bridge is not involved at all.
        // Twins: NativeShellBridgeTests.InitOptionsStruct_Is32Bytes… (C#) and
        // BootSmokeNativeAndroidTest.struct_sizes_match_c_abi (Kotlin/JNA).
        XCTAssertEqual(MemoryLayout<bn_init_options>.offset(of: \.logLevel), 28,
                       "logLevel must land at offset 28 — the tail padding that already existed")

        // The wire ordinals the shell encodes against (BlazorNative.Core.BnLogLevel).
        XCTAssertEqual(BnLogLevel.unset, 0, "ordinal 0 is 'unset' → the runtime default")
        XCTAssertEqual(BnLogLevel.error, 1)
        XCTAssertEqual(BnLogLevel.warn, 2)
        XCTAssertEqual(BnLogLevel.info, 3)
        XCTAssertEqual(BnLogLevel.debug, 4)
        XCTAssertEqual(BnLogLevel.verbose, 5)

        // The ordinal contract (BlazorNative.Core.PlatformKind: DevHost=0, Android=1,
        // iOS=2, …). The iOS shell sends iOS — and CRUCIALLY not Android.
        XCTAssertEqual(BnPlatformKind.iOS, 2, "iOS is ordinal 2")
        XCTAssertEqual(BnPlatformKind.android, 1)
        XCTAssertEqual(BnPlatformKind.devHost, 0)
        XCTAssertNotEqual(BnPlatformKind.iOS, BnPlatformKind.android,
                          "the whole bug: iOS must not be reported as Android")
    }

    /// The end-to-end acceptance: the real linked runtime boots with an iOS-kind
    /// init. This drives the SAME path BnRuntime.start uses (os "ios" +
    /// platformInfoKind iOS), proving the field is wired through the .a so the iOS
    /// app reports iOS rather than the old hardcoded Android.
    func testRealRuntimeAcceptsIosPlatformKind() {
        let result: bn_init_result = "ios".withCString { osPtr in
            "ios-shell".withCString { notePtr in
                var opts = bn_init_options(
                    platformInfoOs: osPtr,
                    platformInfoApiLevel: 0,
                    platformInfoNote: notePtr,
                    platformInfoKind: BnPlatformKind.iOS,
                    logLevel: BnLogLevel.unset)  // Phase 11.4: 0 → the runtime default (Warn)
                return blazornative_init(Int32(MemoryLayout<bn_init_options>.size), &opts)
            }
        }

        XCTAssertEqual(result.status, 0,
                       "blazornative_init must accept an iOS-kind init (status 0)")
        let version = result.version.map { String(cString: $0) } ?? "<null>"
        XCTAssertTrue(version.contains("BlazorNative.Runtime"),
                      "expected the runtime version string, got '\(version)'")
    }
}
