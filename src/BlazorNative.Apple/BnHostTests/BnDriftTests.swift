// ─────────────────────────────────────────────────────────────────────────────
// BnDriftTests — Phase 5.2 (M5 DoD #2): the wire-layout drift guard. The THIRD
// offset/size guard, alongside the Kotlin NativeFrameAdapterTest and the .NET
// PatchProtocolNativeTests (Marshal.OffsetOf). It pins BnFrameAdapter's offset
// constants to the 48-byte BlazorNativePatch / 24-byte BlazorNativeFrame layout
// (src/BlazorNative.Runtime/PatchProtocolNative.cs), so any future protocol
// change breaks ALL THREE shells loudly instead of silently mis-decoding frames
// on iOS.
//
// Pure Swift (no runtime boot) — fast and deterministic. If PatchProtocolNative.cs
// moves a field, this test, NativeFrameAdapterTest.kt, and PatchProtocolNativeTests.cs
// must all be updated together (the contract's three-shell tripwire).
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
@testable import BnHost

final class BnDriftTests: XCTestCase {

    func testPatchLayoutOffsets() {
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
        // The three 8-byte pointer fields must be 8-aligned (arena safety) and
        // land inside the 48-byte record.
        for off in [BnFrameAdapter.patchText, BnFrameAdapter.patchPropName, BnFrameAdapter.patchPropValue] {
            XCTAssertEqual(off % 8, 0, "pointer field at \(off) must be 8-aligned")
            XCTAssertLessThanOrEqual(off + 8, BnFrameAdapter.patchSize)
        }
    }

    func testFrameLayoutOffsets() {
        // BlazorNativeFrame — 24 bytes:
        //   Patches@0 PatchCount@8 FrameId@12 TimestampMs@16
        XCTAssertEqual(BnFrameAdapter.frameSize, 24, "BlazorNativeFrame must be 24 bytes")
        XCTAssertEqual(BnFrameAdapter.framePatches, 0)
        XCTAssertEqual(BnFrameAdapter.framePatchCount, 8)
        XCTAssertEqual(BnFrameAdapter.frameFrameId, 12)
        XCTAssertEqual(BnFrameAdapter.frameTimestampMs, 16)
        // The Patches pointer (offset 0) is 8-aligned; TimestampMs (Int64@16) is 8-aligned.
        XCTAssertEqual(BnFrameAdapter.framePatches % 8, 0)
        XCTAssertEqual(BnFrameAdapter.frameTimestampMs % 8, 0)
    }

    func testNodeTypeTableMatchesWireValues() {
        // Index = BlazorNativeNodeType wire value (0=None never emitted for a
        // CreateNode). Mirror of PatchProtocolNative.cs's enum ordering.
        XCTAssertEqual(BnFrameAdapter.nodeTypes,
                       ["?", "view", "text", "button", "input", "image", "scroll", "picker"])
    }

    func testMaxPatchesGuard() {
        XCTAssertEqual(BnFrameAdapter.maxPatches, 65_536, "the count sanity ceiling must match the Kotlin/.NET guard")
    }
}
