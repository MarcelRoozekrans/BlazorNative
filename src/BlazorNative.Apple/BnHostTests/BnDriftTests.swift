// ─────────────────────────────────────────────────────────────────────────────
// BnDriftTests — Phase 5.2 (M5 DoD #2) + Phase 5.4 (M5 DoD #6): the iOS layout
// drift guards. The THIRD mirror of two struct contracts, alongside the Kotlin +
// .NET pins, so a layout change breaks ALL THREE shells loudly instead of silently
// mis-reading structs on iOS:
//
//   1. testWireLayoutMatchesContract — BnFrameAdapter's offset constants vs the
//      48-byte BlazorNativePatch / 24-byte BlazorNativeFrame layout
//      (PatchProtocolNative.cs; twins: NativeFrameAdapterTest.kt +
//      PatchProtocolNativeTests.cs).
//   2. testBridgeCallbacksStructLayout — the `bn_bridge_callbacks` C struct
//      (BlazorNativeRuntimeC.h) vs the 72-byte, 9-callback BridgeProtocolNative.cs
//      layout (twins: BridgeProtocolNativeTests.cs's offset pins + ShellBridgeTest's
//      callbacks_struct_is_72_bytes). This is what docs/bridge-extension.md's
//      "three mirrors, byte-exact, pinned by drift tests" promises — adding a bridge
//      slot must bump this test.
//
// Pure Swift (no runtime boot) — fast and deterministic.
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnDriftTests: XCTestCase {

    func testWireLayoutMatchesContract() {
        // BlazorNativePatch — 48 bytes:
        //   Kind@0 NodeId@4 ParentNodeId@8 NodeType@12 AuxInt@16 Reserved0@20(pad)
        //   Text@24 PropName@32 PropValue@40
        XCTAssertEqual(BnFrameAdapter.patchSize, 48, "BlazorNativePatch must be 48 bytes")
        XCTAssertEqual(BnFrameAdapter.patchKind, 0)
        XCTAssertEqual(BnFrameAdapter.patchNodeId, 4)
        XCTAssertEqual(BnFrameAdapter.patchParent, 8)
        XCTAssertEqual(BnFrameAdapter.patchNodeType, 12)
        XCTAssertEqual(BnFrameAdapter.patchAux, 16)
        // offset 20 = Reserved0 padding (no constant — the pointers below are 8-aligned)
        XCTAssertEqual(BnFrameAdapter.patchText, 24)
        XCTAssertEqual(BnFrameAdapter.patchPropName, 32)
        XCTAssertEqual(BnFrameAdapter.patchPropValue, 40)

        // BlazorNativeFrame — 24 bytes:
        //   Patches@0 PatchCount@8 FrameId@12 TimestampMs@16
        XCTAssertEqual(BnFrameAdapter.frameSize, 24, "BlazorNativeFrame must be 24 bytes")
        XCTAssertEqual(BnFrameAdapter.framePatches, 0)
        XCTAssertEqual(BnFrameAdapter.framePatchCount, 8)
        XCTAssertEqual(BnFrameAdapter.frameFrameId, 12)
        XCTAssertEqual(BnFrameAdapter.frameTimestampMs, 16)

        // The 8-byte pointer fields must be 8-aligned (arena-read safety) and land
        // inside the 48-byte record.
        for off in [BnFrameAdapter.patchText, BnFrameAdapter.patchPropName, BnFrameAdapter.patchPropValue] {
            XCTAssertEqual(off % 8, 0, "pointer field at \(off) must be 8-aligned")
            XCTAssertLessThanOrEqual(off + 8, BnFrameAdapter.patchSize)
        }
        XCTAssertEqual(BnFrameAdapter.framePatches % 8, 0)
        XCTAssertEqual(BnFrameAdapter.frameTimestampMs % 8, 0)

        // Index = BlazorNativeNodeType wire value (0=None never emitted for a
        // CreateNode). Mirror of PatchProtocolNative.cs's enum ordering.
        XCTAssertEqual(BnFrameAdapter.nodeTypes,
                       ["?", "view", "text", "button", "input", "image", "scroll", "picker"])

        // The count sanity ceiling must match the Kotlin/.NET guard.
        XCTAssertEqual(BnFrameAdapter.maxPatches, 65_536)
    }

    /// The `bn_bridge_callbacks` C struct (BlazorNativeRuntimeC.h) — the third
    /// mirror of the 72-byte, 9 × 8-byte-pointer BlazorNativeBridgeCallbacks layout
    /// (BridgeProtocolNative.cs). Twins: BridgeProtocolNativeTests.cs's offset pins
    /// + ShellBridgeTest's callbacks_struct_is_72_bytes. A bridge-slot add/remove or
    /// reorder must break this test (and the doc's "bump BnDriftTests" instruction).
    func testBridgeCallbacksStructLayout() {
        // Total size = 9 fn pointers × 8 bytes (the size negotiation copies
        // min(structSize, this); BnRuntime passes exactly this as structSize).
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.size, 72,
                       "bn_bridge_callbacks must be 72 bytes (9 × 8-byte callbacks)")
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.stride, 72)

        // Field offsets (0…64), the exact mirror of BridgeProtocolNative.cs. Existing
        // 6 offsets (0…40) are frozen by the size-negotiation contract; the 5.4
        // clipboard/share slots append at 48/56/64. MemoryLayout.offset(of:) reads
        // the C-imported struct's stored-property offsets via KeyPath.
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.navigate), 0)
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.currentRoute), 8)
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.storageRead), 16)
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.storageWrite), 24)
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.storageDelete), 32)
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.fetchBegin), 40)
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.clipboardRead), 48)
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.clipboardWrite), 56)
        XCTAssertEqual(MemoryLayout<bn_bridge_callbacks>.offset(of: \.share), 64)
    }
}
