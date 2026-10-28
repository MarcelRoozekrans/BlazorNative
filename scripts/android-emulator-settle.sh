#!/usr/bin/env bash
# ─────────────────────────────────────────────────────────────────────────────
# Phase 18.1 (#407): settle the CI emulator's packages before connectedAndroidTest.
#
# About two minutes after boot, the google_apis image's Play services keep changing
# their own package state: module and flag updates fetched over the network, each
# a PACKAGE_CHANGED. Once, in run 36124026574, that churn relaunched MainActivity
# in the middle of BnCameraAndroidTest, and the test lost its page state.
#
# 1. Stop the updates at their source:
#    - disable the Play Store, if the image has one;
#    - turn off the guest's Wi-Fi and mobile data, so Play services cannot fetch
#      updates, and confirm it from connectivity's default network. The suite
#      needs no network: its one server, ImageFixtureServer, listens on
#      127.0.0.1 inside the test process, and adb does not use the guest network;
#    - turn off package verification, which otherwise calls Play services on
#      every install.
# 2. Wait until the package manager holds still: its change sequence number,
#    from `dumpsys package changes`, and the overlay list stay the same for
#    SETTLE_QUIET_S seconds. Then wait for the broadcast queues to drain.
#    A ceiling of SETTLE_CEILING_S seconds of watching fails the step, naming the last change, so an
#    emulator that never settles fails here and not as a flake in some test.
#
# Every action and every change seen is logged with its time since the start.
# ─────────────────────────────────────────────────────────────────────────────
set -uo pipefail

QUIET_S=${SETTLE_QUIET_S:-30}
CEILING_S=${SETTLE_CEILING_S:-300}
POLL_S=5
start=$(date +%s)

log() { echo "settle +$(( $(date +%s) - start ))s: $*"; }
sh_() { adb shell "$@" 2>&1 | tr -d '\r'; }

# ── 1. Stop the updates at their source ──────────────────────────────────────
if sh_ pm path com.android.vending | grep -q '^package:'; then
  log "Play Store present; disable-user: $(sh_ pm disable-user --user 0 com.android.vending)"
else
  log "no Play Store on this image: com.android.vending is not installed"
fi
# Right after boot, telephony may not take `svc data disable` yet: run 40609777833
# read mobile_data=1 just after it. So re-issue both until connectivity reports
# no default network, for up to 30 s.
net=""
for _ in $(seq 1 30); do
  sh_ svc wifi disable >/dev/null
  sh_ svc data disable >/dev/null
  net=$(sh_ dumpsys connectivity | grep -m 1 'Active default network')
  case "$net" in *none*) break ;; esac
  sleep 1
done
log "guest network: ${net:-no 'Active default network' line}; wifi_on=$(sh_ settings get global wifi_on) mobile_data=$(sh_ settings get global mobile_data)"
case "$net" in
  *none*) ;;
  *) echo "::warning::settle: the guest network is still up, so Play services can still fetch updates; the wait below still applies" ;;
esac
sh_ settings put global package_verifier_enable 0 >/dev/null
sh_ settings put global verifier_verify_adb_installs 0 >/dev/null
log "package verification off: package_verifier_enable=$(sh_ settings get global package_verifier_enable)"

# ── 2. Wait for the package manager to hold still ────────────────────────────
fingerprint() {
  local seq ovl
  seq=$(sh_ dumpsys package changes | sed -n 's/^ *Sequence number=\([0-9][0-9]*\).*/\1/p' | head -n 1)
  ovl=$(sh_ cmd overlay list | md5sum | cut -c1-8)
  echo "seq=${seq:-none} overlays=${ovl}"
}

last=$(fingerprint)
case "$last" in
  seq=none*)
    log "FAILED: 'dumpsys package changes' printed no sequence number, so there is no signal to wait on:"
    sh_ dumpsys package changes | head -n 20
    exit 1 ;;
esac
log "watching: ${last}; settled after ${QUIET_S}s without a change, failing after ${CEILING_S}s of watching"
watch_start=$(date +%s)
last_change=$watch_start
changes=0
while :; do
  now=$(date +%s)
  if (( now - last_change >= QUIET_S )); then
    break
  fi
  if (( now - watch_start >= CEILING_S )); then
    log "FAILED: the emulator's packages never held still for ${QUIET_S}s within ${CEILING_S}s of watching;" \
        "${changes} changes seen, the last at +$(( last_change - start ))s: ${last}"
    exit 1
  fi
  sleep "$POLL_S"
  cur=$(fingerprint)
  if [ "$cur" != "$last" ]; then
    changes=$((changes + 1))
    log "changed: ${cur}"
    last=$cur
    last_change=$(date +%s)
  fi
done
log "held still for ${QUIET_S}s after ${changes} changes: ${last}"

t0=$(date +%s)
if timeout 120 adb shell am wait-for-broadcast-idle >/dev/null 2>&1; then
  log "broadcast queues idle after $(( $(date +%s) - t0 ))s"
else
  log "FAILED: the broadcast queues did not drain within 120s"
  exit 1
fi
log "settled: ${last}"
