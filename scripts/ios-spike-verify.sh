#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Phase 5.0 iOS spike — verification (the GREEN bar). Runs on macos-latest after
# the ladder. Given a RID, finds any produced NativeAOT artifact (.a / .dylib),
# verifies all NINE blazornative_* symbols (normalizing the Apple leading
# underscore), sanity-checks the arch (lipo/file), then LINK-PROBES a ~30-line C
# stub against it with the simulator SDK — an executable that BUILDS is the bar.
# Bonus (attempt, don't gate): boot a sim and spawn the stub to print version.
#
# Never hard-fails the CI step by itself (caller wraps it); it prints a clear
# PASS/FAIL verdict and exits 0 (evidence) / 1 (no artifact) / 2 (link failed).
# ─────────────────────────────────────────────────────────────────────────────
set -u

RID="${1:-iossimulator-arm64}"
EXPECTED=(
  blazornative_dispatch_event blazornative_fetch_complete
  blazornative_host_event blazornative_init blazornative_mount
  blazornative_register_bridge blazornative_register_frame_callback
  blazornative_shutdown blazornative_version
)

echo "=================================================================="
echo " iOS spike verification — RID=$RID"
echo "=================================================================="

# 1) Locate candidate artifacts for this RID (static archive preferred, dylib ok)
#    Portable to bash 3.2 (macOS /bin/bash) — no mapfile.
CANDIDATES=()
while IFS= read -r line; do
  [ -n "$line" ] && CANDIDATES+=("$line")
done < <(find src/BlazorNative.Runtime/bin spikes/ios-aot-probe/bin -type f \
  \( -name '*.a' -o -name '*.dylib' \) 2>/dev/null \
  | grep -F "$RID" | grep -v '\.dSYM/' | sort -u)

if [ "${#CANDIDATES[@]}" -eq 0 ]; then
  echo "RESULT: NO ARTIFACT produced for $RID — nothing to verify."
  echo "VERDICT: RED (no linkable artifact)"
  exit 1
fi

echo "Candidate artifacts (${#CANDIDATES[@]}):"
printf '  %s\n' "${CANDIDATES[@]}"

# GREEN requires ONE artifact that is BOTH complete (all 9 symbols) AND links.
# The link stub only references 2 of the 9 symbols, so link-pass alone cannot
# stand in for the symbol half of the bar — track both, gate on both. This keeps
# the script honest if Phase 5.2 promotes it to a real CI gate.
OVERALL_LINK_OK=0
OVERALL_SYMS_OK=0
OVERALL_GREEN=0

for ART in "${CANDIDATES[@]}"; do
  ART_LINK_OK=0
  ART_SYMS_OK=0
  echo
  echo "------------------------------------------------------------------"
  echo " Artifact: $ART"
  echo "------------------------------------------------------------------"
  ls -la "$ART"
  echo "-- file --";      file "$ART" || true
  echo "-- lipo -info --"; lipo -info "$ART" 2>/dev/null || echo "(lipo: not a fat/thin macho or archive)"

  # 2) Symbols — nm -gU lists external defined symbols; strip leading underscore.
  echo "-- nm -gU (defined external, blazornative_*) --"
  RAW=$(nm -gU "$ART" 2>/dev/null | grep -Eo '_?blazornative_[A-Za-z_]+' | sed 's/^_//' | sort -u)
  echo "$RAW" | sed 's/^/    /'
  MISSING=()
  for sym in "${EXPECTED[@]}"; do
    echo "$RAW" | grep -qx "$sym" || MISSING+=("$sym")
  done
  if [ "${#MISSING[@]}" -eq 0 ]; then
    echo "SYMBOLS: all 9 blazornative_* present."
    ART_SYMS_OK=1
    OVERALL_SYMS_OK=1
  else
    echo "SYMBOLS: MISSING ${#MISSING[@]} -> ${MISSING[*]}"
  fi

  # 3) Link probe — the honest bar. A ~30-line C stub linked with the sim SDK.
  STUB=$(mktemp -t bnstub.XXXXXX).c
  cat > "$STUB" <<'EOF'
#include <stdint.h>
#include <stdio.h>
/* Mirror of BlazorNativeInitResult: {int, void*, void*} -> 24 bytes on arm64. */
typedef struct { int32_t status; const char* error; const char* version; } bn_init_result;
extern bn_init_result blazornative_init(void* opts);
extern const char*    blazornative_version(void);
int main(void) {
    bn_init_result r = blazornative_init(0);
    const char* v = blazornative_version();
    printf("BN_STUB init.status=%d version=%s\n", r.status, v ? v : "(null)");
    return 0;
}
EOF

  SDKPATH=$(xcrun --sdk iphonesimulator --show-sdk-path 2>/dev/null)
  OUT="${ART}.stub-exe"
  echo "-- link probe (xcrun -sdk iphonesimulator clang) --"
  # NativeAOT static libs pull in system frameworks/libs; supply the usual set.
  # For a .dylib we link -L/-l; for a .a we pass the archive directly.
  # The NativeAOT dylib records its install name as @rpath/<name>.dylib, so add an
  # rpath to its own directory — lets the bonus simctl run actually dlopen it.
  ART_DIR=$(cd "$(dirname "$ART")" && pwd)
  set -x
  xcrun -sdk iphonesimulator clang -arch arm64 \
    -isysroot "$SDKPATH" \
    -mios-simulator-version-min=13.0 \
    "$STUB" "$ART" \
    -o "$OUT" \
    -Wl,-rpath,"$ART_DIR" \
    -lc++ -lz -licucore -lobjc \
    -framework Foundation -framework Security -framework CoreFoundation \
    2>&1
  LRC=$?
  set +x
  if [ "$LRC" -eq 0 ] && [ -f "$OUT" ]; then
    echo "LINK PROBE: PASS — built $OUT"
    file "$OUT" || true
    OVERALL_LINK_OK=1
    ART_LINK_OK=1

    # 4) BONUS — try to run it on a booted sim (don't gate on this).
    echo "-- bonus: simctl spawn --"
    ( set -e
      DEV=$(xcrun simctl create bn-spike-sim "iPhone 15" 2>/dev/null || true)
      if [ -n "${DEV:-}" ]; then
        xcrun simctl boot "$DEV" 2>/dev/null || true
        sleep 5
        xcrun simctl spawn "$DEV" "$OUT" 2>&1 | sed 's/^/    RUN: /' || true
        xcrun simctl shutdown "$DEV" 2>/dev/null || true
        xcrun simctl delete "$DEV" 2>/dev/null || true
      else
        echo "    (could not create a sim device; skipping run)"
      fi
    ) || echo "    (bonus run failed/skipped — not gating)"
  else
    echo "LINK PROBE: FAIL (clang exit $LRC) for $ART"
  fi

  # This artifact is fully good only if it carries ALL 9 symbols AND links.
  if [ "$ART_SYMS_OK" -eq 1 ] && [ "$ART_LINK_OK" -eq 1 ]; then
    OVERALL_GREEN=1
  fi
done

echo
echo "=================================================================="
if [ "$OVERALL_GREEN" -eq 1 ]; then
  echo "VERDICT: GREEN — at least one artifact carries all 9 exports AND link-probes."
  exit 0
else
  echo "VERDICT: RED — no single artifact met BOTH bars (symbols=$OVERALL_SYMS_OK link=$OVERALL_LINK_OK)."
  echo "  (GREEN requires ONE artifact with all 9 blazornative_* symbols AND a clean link probe.)"
  exit 2
fi
