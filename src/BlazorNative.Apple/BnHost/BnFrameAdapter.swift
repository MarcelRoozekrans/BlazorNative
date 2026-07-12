// ─────────────────────────────────────────────────────────────────────────────
// BnFrameAdapter — Phase 5.2 (M5 DoD #2): decodes a native `BlazorNativeFrame*`
// into the [BnFrame]/[BnPatch] value model. The Swift transliteration of the
// Kotlin io.blazornative.jni.NativeFrameAdapter — same offsets, same per-kind
// field decode, same throw-on-NULL-contractual-string, same 0..65536 count guard.
//
// Layout contract — mirror of src/BlazorNative.Runtime/PatchProtocolNative.cs
// (little-endian, 8-byte pointers). This is the THIRD offset/size guard alongside
// NativeFrameAdapterTest.kt (JVM) and PatchProtocolNativeTests.cs (.NET): if any
// field moves, all three shells must break loudly (the Swift drift test is the
// Gate-2 guard; the offset constants below are the load-bearing pins).
//
//   BlazorNativePatch — 48 bytes:
//     Kind@0  NodeId@4  ParentNodeId@8  NodeType@12  AuxInt@16  Reserved0@20(pad)
//     Text@24(ptr)  PropName@32(ptr)  PropValue@40(ptr)
//   BlazorNativeFrame — 24 bytes:
//     Patches@0(ptr)  PatchCount@8  FrameId@12  TimestampMs@16
//
// LIFETIME: everything the frame points at is arena memory owned by the native
// side, valid ONLY for the duration of the callback. [read] copies every string
// (String(cString:) copies) so the returned BnFrame is fully detached — the
// mapper can hand it to the main queue safely.
//
// Reads use loadUnaligned so an arena allocation that is not 8-aligned can never
// trap (the JNA Pointer.getInt/getLong equivalents are unaligned-safe too).
// ─────────────────────────────────────────────────────────────────────────────

import Foundation

/// The parsed patch model — the exact inverse of FrameEncoder.cs's table, and
/// the twin of Kotlin's `RenderPatch` sealed class. `Int32` matches the wire ids.
enum BnPatch {
    case createNode(nodeId: Int32, nodeType: String, parentId: Int32?, insertIndex: Int32)
    case removeNode(nodeId: Int32)
    case updateProp(nodeId: Int32, name: String, value: String?)
    case replaceText(nodeId: Int32, text: String)
    case setStyle(nodeId: Int32, property: String, value: String?)
    case attachEvent(nodeId: Int32, eventName: String, handlerId: Int32)
    case detachEvent(nodeId: Int32, handlerId: Int32, eventName: String)
    case commitFrame(frameId: Int32, timestampMs: Int64)
}

/// A fully decoded, arena-detached frame.
struct BnFrame {
    let frameId: Int32
    let timestampMs: Int64
    let patches: [BnPatch]
}

/// Thrown on malformed input — the twin of the Kotlin `require(...)` /
/// `requireNotNull(...)` failures. BnRuntime catches these and drops the frame
/// LOUDLY (never crashes inside the C callback).
enum BnDecodeError: Error, CustomStringConvertible {
    case corruptPatchCount(Int32)
    case nullContractualString(field: String, patchIndex: Int)
    case nullPatchesPointer(patchCount: Int32)

    var description: String {
        switch self {
        case .corruptPatchCount(let n):
            return "corrupt BlazorNativeFrame: patchCount=\(n) (allowed 0..\(BnFrameAdapter.maxPatches))"
        case .nullContractualString(let field, let i):
            return "\(field) NULL (patch \(i))"
        case .nullPatchesPointer(let n):
            return "BlazorNativeFrame.Patches NULL but patchCount=\(n)"
        }
    }
}

enum BnFrameAdapter {

    // BlazorNativePatch — 48 bytes.
    static let patchSize = 48
    static let patchKind = 0        // CommitFrame: (frameId lives on the envelope)
    static let patchNodeId = 4
    static let patchParent = 8      // -1 = none
    static let patchNodeType = 12   // CreateNode only
    static let patchAux = 16        // CreateNode: insertIndex (-1 = append); Attach/Detach: handlerId
    // offset 20: Reserved0 padding — the pointers below are 8-aligned.
    static let patchText = 24       // ReplaceText: text; Attach/DetachEvent: eventName
    static let patchPropName = 32   // UpdateProp/SetStyle: name
    static let patchPropValue = 40  // UpdateProp/SetStyle: value; NULL = null

    // BlazorNativeFrame — 24 bytes.
    static let frameSize = 24
    static let framePatches = 0
    static let framePatchCount = 8
    static let frameFrameId = 12
    static let frameTimestampMs = 16

    /// Index = BlazorNativeNodeType wire value (0 = None, never emitted for a
    /// CreateNode by the encoder). Twin of the Kotlin `nodeTypes` array.
    static let nodeTypes = ["?", "view", "text", "button", "input", "image", "scroll", "picker"]

    /// Sanity ceiling on patchCount — a corrupt frame pointer would otherwise
    /// have us chase garbage; take the documented dropped-frame path instead.
    static let maxPatches: Int32 = 65_536

    static func read(_ framePtr: UnsafeRawPointer) throws -> BnFrame {
        let patchesRaw = framePtr.loadUnaligned(fromByteOffset: framePatches, as: UInt.self)
        let patchCount = framePtr.loadUnaligned(fromByteOffset: framePatchCount, as: Int32.self)
        guard patchCount >= 0 && patchCount <= maxPatches else {
            throw BnDecodeError.corruptPatchCount(patchCount)
        }
        let frameId = framePtr.loadUnaligned(fromByteOffset: frameFrameId, as: Int32.self)
        let timestampMs = framePtr.loadUnaligned(fromByteOffset: frameTimestampMs, as: Int64.self)

        if patchCount > 0 && patchesRaw == 0 {
            throw BnDecodeError.nullPatchesPointer(patchCount: patchCount)
        }

        var patches: [BnPatch] = []
        patches.reserveCapacity(Int(patchCount))

        if patchCount > 0, let patchesPtr = UnsafeRawPointer(bitPattern: patchesRaw) {
            for i in 0..<Int(patchCount) {
                let base = i * patchSize
                let kind = patchesPtr.loadUnaligned(fromByteOffset: base + patchKind, as: Int32.self)
                let nodeId = patchesPtr.loadUnaligned(fromByteOffset: base + patchNodeId, as: Int32.self)
                let parent = patchesPtr.loadUnaligned(fromByteOffset: base + patchParent, as: Int32.self)
                let aux = patchesPtr.loadUnaligned(fromByteOffset: base + patchAux, as: Int32.self)

                switch kind {
                case 1: // CreateNode
                    let nodeTypeIdx = patchesPtr.loadUnaligned(fromByteOffset: base + patchNodeType, as: Int32.self)
                    let nodeType = (nodeTypeIdx >= 0 && Int(nodeTypeIdx) < nodeTypes.count)
                        ? nodeTypes[Int(nodeTypeIdx)] : "?"
                    patches.append(.createNode(
                        nodeId: nodeId,
                        nodeType: nodeType,
                        parentId: parent == -1 ? nil : parent,
                        insertIndex: aux))
                // kind 2 (AppendChild) is reserved-dormant since Phase 3.3 —
                // never emitted; falls into the unknown-kind log+skip arm below.
                case 3: // RemoveNode
                    patches.append(.removeNode(nodeId: nodeId))
                case 4: // UpdateProp
                    guard let name = readUtf8(patchesPtr, base + patchPropName) else {
                        throw BnDecodeError.nullContractualString(field: "UpdateProp.propName", patchIndex: i)
                    }
                    patches.append(.updateProp(nodeId: nodeId, name: name,
                                               value: readUtf8(patchesPtr, base + patchPropValue)))
                case 5: // ReplaceText
                    guard let text = readUtf8(patchesPtr, base + patchText) else {
                        throw BnDecodeError.nullContractualString(field: "ReplaceText.text", patchIndex: i)
                    }
                    patches.append(.replaceText(nodeId: nodeId, text: text))
                case 6: // SetStyle
                    guard let property = readUtf8(patchesPtr, base + patchPropName) else {
                        throw BnDecodeError.nullContractualString(field: "SetStyle.propName", patchIndex: i)
                    }
                    patches.append(.setStyle(nodeId: nodeId, property: property,
                                             value: readUtf8(patchesPtr, base + patchPropValue)))
                case 7: // AttachEvent
                    guard let eventName = readUtf8(patchesPtr, base + patchText) else {
                        throw BnDecodeError.nullContractualString(field: "AttachEvent.eventName", patchIndex: i)
                    }
                    patches.append(.attachEvent(nodeId: nodeId, eventName: eventName, handlerId: aux))
                case 8: // DetachEvent
                    guard let eventName = readUtf8(patchesPtr, base + patchText) else {
                        throw BnDecodeError.nullContractualString(field: "DetachEvent.eventName", patchIndex: i)
                    }
                    patches.append(.detachEvent(nodeId: nodeId, handlerId: aux, eventName: eventName))
                case 9: // CommitFrame — the envelope carries the truth.
                    patches.append(.commitFrame(frameId: frameId, timestampMs: timestampMs))
                default:
                    // Unknown wire id (incl. dormant 2/AppendChild): a newer
                    // runtime talking to an older shell. Skip, leave a trace.
                    NSLog("[BnFrameAdapter] skipping unknown patch kind \(kind) (patch \(i) of \(patchCount))")
                }
            }
        }

        return BnFrame(frameId: frameId, timestampMs: timestampMs, patches: patches)
    }

    /// Reads a `const char*` field: NULL → nil, else a COPIED UTF-8 String
    /// (String(cString:) copies the bytes out of the arena).
    private static func readUtf8(_ base: UnsafeRawPointer, _ offset: Int) -> String? {
        let raw = base.loadUnaligned(fromByteOffset: offset, as: UInt.self)
        guard raw != 0, let cptr = UnsafeRawPointer(bitPattern: raw) else { return nil }
        return String(cString: cptr.assumingMemoryBound(to: CChar.self))
    }
}
