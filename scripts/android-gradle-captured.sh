#!/usr/bin/env bash
# Phase 18.12 (#718): run Gradle's connectedAndroidTest with a sampler of package and
# overlay changes beside it, so a recurrence of #407's relaunch leaves the package, overlay
# and broadcast changes around it on record. See docs/plans/2026-11-15-phase-18.12-record.md,
# section 4.
# Usage: android-gradle-captured.sh <sampler-output-file> <gradle args...>
# Run from src/BlazorNative.Jni. The exit status is Gradle's and nothing else's: every
# read in the sampler is guarded. When Gradle returns, `kill` on the sampler's PID stops the
# sampler subshell only. A read in flight, such as `timeout 5 adb shell`, can outlive it by up
# to 5 s. It is detached from stdout and stderr, so it holds nothing open and does nothing.
# A sample comes every 1 to 3 s, not every second: an iteration is three bounded reads plus a
# one-second sleep (gaps of 1.1 to 2.8 s in the two green lane runs, and 2.2 to 3.3 s in the
# 18.12 forced runs). Readers must allow gaps and use the utc field, not the line count.
# utc is the start of a sample. Its three signals are read one after the other, so each was
# read between utc and its own end stamp: seq_end, ovl_end and bc_end. An end stamp is an
# observation time, not a change time. A change seen in a sample happened between that
# signal's end stamp in the previous sample and its end stamp in this one. The end stamp
# alone cannot order a change against a relaunch, so line the interval up with logcat.
set -u
OUT="$1"; shift

sampler() {
  local i=0 now seq ov bl seq_end ovl_end bc_end
  echo "T0 host_epoch=$(date +%s.%N) device_epoch=$(timeout 5 adb shell date +%s.%N 2>/dev/null | tr -d '\r' || true)" > "$OUT" 2>/dev/null || return 0
  while :; do
    now=$(date -u +%H:%M:%S.%N | cut -c1-12)
    seq=$( { timeout 5 adb shell dumpsys package changes 2>/dev/null | tr -d '\r' | grep -m1 'Sequence number'; } || true )
    seq_end=$(date -u +%H:%M:%S.%N | cut -c1-12)
    ov=$( { timeout 5 adb shell cmd overlay list 2>/dev/null | tr -d '\r' | md5sum | cut -c1-8; } || true )
    ovl_end=$(date -u +%H:%M:%S.%N | cut -c1-12)
    bl=$( { timeout 5 adb shell dumpsys activity broadcasts history 2>/dev/null | tr -d '\r' | grep 'io.blazornative.shell' | sed -E 's/^ *#[0-9]+: *//'; } || true )
    bc_end=$(date -u +%H:%M:%S.%N | cut -c1-12)
    {
      printf 'S %s utc=%s seq=[%s] overlay=%s bcast=%s lines=%s seq_end=%s ovl_end=%s bc_end=%s\n' "$i" "$now" "$seq" "$ov" \
        "$(printf '%s\n' "$bl" | md5sum | cut -c1-8)" "$(printf '%s\n' "$bl" | grep -c . || true)" \
        "$seq_end" "$ovl_end" "$bc_end"
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
