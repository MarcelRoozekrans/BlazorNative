#!/usr/bin/env bash
# Phase 18.12 (#718): run Gradle's connectedAndroidTest with a roughly once-a-second sampler of
# package and overlay changes beside it, so a recurrence of #407's relaunch records its
# own cause. See docs/plans/2026-11-15-phase-18.12-record.md, section 4.
# Usage: android-gradle-captured.sh <sampler-output-file> <gradle args...>
# Run from src/BlazorNative.Jni. The exit status is Gradle's and nothing else's: every
# read in the sampler is guarded, and the sampler is stopped by PID when Gradle returns.
# "Once a second" is a target, not a guarantee: an iteration is three bounded reads plus a
# one-second sleep, so under load it can take several seconds (the 18.12 forced runs saw
# 2.2 to 3.3 s gaps). Readers must allow gaps and use the utc field, not the line count.
set -u
OUT="$1"; shift

sampler() {
  local i=0 now dev seq ov bl
  echo "T0 host_epoch=$(date +%s.%N) device_epoch=$(timeout 5 adb shell date +%s.%N 2>/dev/null | tr -d '\r' || true)" > "$OUT" 2>/dev/null || return 0
  while :; do
    now=$(date -u +%H:%M:%S.%N | cut -c1-12)
    seq=$( { timeout 5 adb shell dumpsys package changes 2>/dev/null | tr -d '\r' | grep -m1 'Sequence number'; } || true )
    ov=$( { timeout 5 adb shell cmd overlay list 2>/dev/null | tr -d '\r' | md5sum | cut -c1-8; } || true )
    bl=$( { timeout 5 adb shell dumpsys activity broadcasts history 2>/dev/null | tr -d '\r' | grep 'io.blazornative.shell' | sed -E 's/^ *#[0-9]+: *//'; } || true )
    {
      printf 'S %s utc=%s seq=[%s] overlay=%s bcast=%s lines=%s\n' "$i" "$now" "$seq" "$ov" \
        "$(printf '%s\n' "$bl" | md5sum | cut -c1-8)" "$(printf '%s\n' "$bl" | grep -c . || true)"
      printf '%s\n' "$bl" | sed "s/^/B $i /"
    } >> "$OUT" 2>/dev/null || true
    i=$((i + 1))
    sleep 1
  done
}

sampler </dev/null >/dev/null 2>&1 &
SPID=$!
./gradlew --no-daemon connectedAndroidTest "$@"
rc=$?
kill "$SPID" 2>/dev/null || true
wait "$SPID" 2>/dev/null || true
exit "$rc"
