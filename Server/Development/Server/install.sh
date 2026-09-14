#!/usr/bin/env bash

set -Eeuo pipefail
umask 0002

# Required: URL of the server ZIP archive, set via VINTAGESTORY_ARCHIVE_URL in .env.
ARCHIVE_URL="${VINTAGESTORY_ARCHIVE_URL:?Set VINTAGESTORY_ARCHIVE_URL in .env}"

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_MARKER="${SCRIPT_DIR}/INSTALLED"

FORCE_REINSTALL=false
case "${1:-}" in
  "")
    ;;
  --force-reinstall)
    FORCE_REINSTALL=true
    ;;
  *)
    echo "Usage: $0 [--force-reinstall]" >&2
    exit 1
    ;;
esac

if [[ -f "${INSTALL_MARKER}" && "${FORCE_REINSTALL}" == false ]]; then
  echo "Server is already installed. Use --force-reinstall to install it again."
  exit 0
fi

ARCHIVE_FILE="$(mktemp --tmpdir "server-archive.XXXXXX.zip")"

cleanup() {
  rm -f -- "${ARCHIVE_FILE}"
}

trap cleanup EXIT

command -v curl >/dev/null 2>&1 || {
  echo "Error: curl is required to download the server archive." >&2
  exit 1
}

command -v unzip >/dev/null 2>&1 || {
  echo "Error: unzip is required to extract the server archive." >&2
  exit 1
}

echo "Downloading server archive..."

curl --fail --location --retry 3 --silent --show-error \
  --output "${ARCHIVE_FILE}" \
  "${ARCHIVE_URL}" &
download_pid=$!

while kill -0 "${download_pid}" 2>/dev/null; do
  downloaded_bytes="$(stat --format='%s' "${ARCHIVE_FILE}" 2>/dev/null || printf '0')"
  echo "Download progress: ${downloaded_bytes} bytes"
  sleep 5
done

wait "${download_pid}"
downloaded_bytes="$(stat --format='%s' "${ARCHIVE_FILE}")"
echo "Download complete: ${downloaded_bytes} bytes"

echo "Extracting server archive to ${SCRIPT_DIR}..."
unzip -q -o "${ARCHIVE_FILE}" -d "${SCRIPT_DIR}"

# Keep the bind-mounted installation writable by the configured container user
# and its group while retaining read access for other host users.
find "${SCRIPT_DIR}" -type d -exec chmod u+rwx,g+rwx,o+rx {} +
find "${SCRIPT_DIR}" -type f -perm /111 -exec chmod u+rwx,g+rwx,o+rx {} +
find "${SCRIPT_DIR}" -type f ! -perm /111 -exec chmod u+rw,g+rw,o+r {} +

touch "${INSTALL_MARKER}"

echo "Server archive installed."
