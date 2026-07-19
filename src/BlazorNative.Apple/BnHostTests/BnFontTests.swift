// ─────────────────────────────────────────────────────────────────────────────
// BnFontTests — font parity Gate A (#126): the iOS LOAD GUARD.
//
// Gate A bundles Inter (OFL, static Regular) on both shells and registers it so
// text CAN resolve it — no text-rendering change yet (that is Gate B). The one
// thing that can silently go wrong is the NAME: iOS resolves a bundled font by
// its PostScript name (nameID 6 = "Inter-Regular"), which is NOT the ttf's
// filename and NOT the family name; a wrong string returns nil from
// `UIFont(name:)` and every later `?? .systemFont(ofSize:)` fallback (Gate B)
// then silently renders the SYSTEM font, re-breaking the parity this feature
// exists to tighten. So this test asserts the real, registered font — not that a
// file exists, but that iOS hands back Inter for the exact name Gate B will use.
//
// It is a HOSTED XCTest (TEST_HOST = BnHost.app), so the app's Info.plist
// UIAppFonts registration is in effect exactly as it is at real launch — the
// test exercises the SAME registration path a user's app does, not a re-register
// of its own. Dispatch/CI-verified: the iOS lane runs it (this repo cannot run
// XCTest locally on Windows).
// ─────────────────────────────────────────────────────────────────────────────

import XCTest
import UIKit
@testable import BnHost

final class BnFontTests: XCTestCase {

    /// The PostScript name Gate B resolves Inter by. Recorded from the asset with
    /// fontTools (name table, nameID 6) — if the bundled ttf is ever swapped for a
    /// build whose PostScript name differs, this is the line that must move WITH it.
    private static let interPostScriptName = "Inter-Regular"
    private static let interFamilyName = "Inter"

    func testInterRegularIsRegisteredAndNotTheSystemFallback() throws {
        // 1. The bundled font resolves by its PostScript name. nil here means the
        //    UIAppFonts registration did not take (wrong filename in Info.plist,
        //    the ttf missing from Copy Bundle Resources, or a mismatched name).
        let inter = try XCTUnwrap(
            UIFont(name: Self.interPostScriptName, size: 17),
            "UIFont(name: \"\(Self.interPostScriptName)\") was nil — the bundled Inter did not "
            + "register. Check Info.plist UIAppFonts holds \"Inter-Regular.ttf\", that the ttf is "
            + "in the app bundle (BnHost/Fonts/Inter-Regular.ttf via the BnHost folder source), "
            + "and that \"\(Self.interPostScriptName)\" is the ttf's actual PostScript name.")

        // 2. It IS Inter, by both identities iOS exposes — so a fallback that
        //    happened to produce a non-nil UIFont could not pass this.
        XCTAssertEqual(inter.fontName, Self.interPostScriptName,
                       "resolved fontName must be the bundled Inter's PostScript name")
        XCTAssertEqual(inter.familyName, Self.interFamilyName,
                       "resolved familyName must be Inter, not a system family")

        // 3. And it is NOT the system font. This is the guard's teeth: if the name
        //    ever silently fell back, familyName would be the system family
        //    (".SF UI…"/".AppleSystemUIFont"), and Gate B's parity would be a lie
        //    that renders green.
        let system = UIFont.systemFont(ofSize: 17)
        XCTAssertNotEqual(inter.familyName, system.familyName,
                          "Inter resolved to the SYSTEM family — the registration/name is wrong")

        // 4. Inter is discoverable in the family table under exactly this name,
        //    which is what Gate B's UIFont(name:) lookup walks.
        XCTAssertTrue(UIFont.familyNames.contains(Self.interFamilyName),
                      "the \"\(Self.interFamilyName)\" family is not among the registered families")
        XCTAssertTrue(UIFont.fontNames(forFamilyName: Self.interFamilyName).contains(Self.interPostScriptName),
                      "\"\(Self.interPostScriptName)\" is not a registered font of the Inter family")
    }
}
