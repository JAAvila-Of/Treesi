#!/usr/bin/env bash
set -euo pipefail
if [[ "${GITHUB_ACTIONS:-}" != true || "${RUNNER_ENVIRONMENT:-}" != github-hosted || "${RUNNER_OS:-}" != Linux || "${TREESI_ISOLATED_SUITE:-}" != linux-native-ui ]]; then
  echo 'Native Linux launch requires the opted-in disposable hosted VM.' >&2
  exit 1
fi
lab_dir=$(mktemp -d "$RUNNER_TEMP/treesi-linux-native.XXXXXX")
chmod 700 "$lab_dir"
export DISPLAY=:177
export XAUTHORITY="$lab_dir/authority"
export TREESI_LINUX_DISPLAY_TOKEN="$lab_dir/capability.json"
export XDG_DATA_HOME="$lab_dir/data"
export XDG_CONFIG_HOME="$lab_dir/config"
export XDG_CACHE_HOME="$lab_dir/cache"
export LIBGL_ALWAYS_SOFTWARE=1
if [[ -e /tmp/.X11-unix/X177 ]]; then
  echo 'The requested virtual display is occupied; refusing to attach.' >&2
  exit 1
fi
touch "$XAUTHORITY"
chmod 600 "$XAUTHORITY"
xauth -f "$XAUTHORITY" add "$DISPLAY" . "$(mcookie)"
Xvfb "$DISPLAY" -screen 0 1440x900x24 -nolisten tcp -auth "$XAUTHORITY" > "$TREESI_NATIVE_RESULTS/xvfb.log" 2>&1 &
server_pid=$!
wm_pid=''
cleanup() {
  for child in "$wm_pid" "$server_pid"; do
    if [[ -n "$child" && -r "/proc/$child/status" ]] && [[ "$(awk '/^PPid:/ {print $2}' "/proc/$child/status")" == "$$" ]]; then
      kill "$child" 2>/dev/null || true
      wait "$child" 2>/dev/null || true
    fi
  done
}
trap cleanup EXIT
ready=false
for attempt in {1..60}; do
  if ! kill -0 "$server_pid" 2>/dev/null; then echo 'Owned Xvfb exited.' >&2; exit 1; fi
  if xdpyinfo -display "$DISPLAY" >/dev/null 2>&1; then ready=true; break; fi
  sleep 0.2
done
if [[ "$ready" != true ]]; then echo 'Owned Xvfb did not become ready.' >&2; exit 1; fi
printf '{"schemaVersion":"1","display":"%s","authority":"%s","serverPid":%s}\n' "$DISPLAY" "$XAUTHORITY" "$server_pid" > "$TREESI_LINUX_DISPLAY_TOKEN"
chmod 600 "$TREESI_LINUX_DISPLAY_TOKEN"
openbox > "$TREESI_NATIVE_RESULTS/openbox.log" 2>&1 &
wm_pid=$!
dotnet test tests/Treesi.Native.Tests -c Release --no-build --filter TestCategory=LinuxNative --logger trx --results-directory "$TREESI_NATIVE_RESULTS/tests"
