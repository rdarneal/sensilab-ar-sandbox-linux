#!/usr/bin/env bash
# Build the kinect_bridge .so (and smoke test) and stage the .so into
# Assets/Plugins/Linux/x86_64/ where Unity's plugin importer will pick it up.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BRIDGE_DIR="${REPO_ROOT}/native/kinect-bridge"
BUILD_DIR="${BRIDGE_DIR}/build"
PLUGIN_DIR="${REPO_ROOT}/Assets/Plugins/Linux/x86_64"
SO_NAME="libkinectbridge.so"

mkdir -p "${BUILD_DIR}"
mkdir -p "${PLUGIN_DIR}"

cd "${BUILD_DIR}"
cmake .. -DCMAKE_BUILD_TYPE=Release
make -j"$(nproc)"

SRC_SO="${BUILD_DIR}/${SO_NAME}"
if [[ ! -f "${SRC_SO}" ]]; then
    echo "build-bridge: expected ${SRC_SO} but it doesn't exist" >&2
    exit 1
fi

cp "${SRC_SO}" "${PLUGIN_DIR}/${SO_NAME}"
SIZE_BYTES=$(stat -c%s "${PLUGIN_DIR}/${SO_NAME}")
echo "build-bridge: staged ${PLUGIN_DIR}/${SO_NAME} (${SIZE_BYTES} bytes)"

# Stage libfreenect2 next to the bridge so the $ORIGIN entry in libkinectbridge's
# RUNPATH finds it in a deployed Linux build. We copy the underlying file under
# the SONAME ("libfreenect2.so.0.2") — that matches the bridge's DT_NEEDED, so
# no symlink is needed (and Unity prefers a flat file for its plugin importer).
FREENECT2_SONAME="libfreenect2.so.0.2"
FREENECT2_SRC="${HOME}/freenect2/lib/libfreenect2.so.0.2.0"
if [[ ! -f "${FREENECT2_SRC}" ]]; then
    echo "build-bridge: expected ${FREENECT2_SRC} but it doesn't exist" >&2
    echo "build-bridge: install libfreenect2 to \$HOME/freenect2 first" >&2
    exit 1
fi
cp "${FREENECT2_SRC}" "${PLUGIN_DIR}/${FREENECT2_SONAME}"
FN2_SIZE_BYTES=$(stat -c%s "${PLUGIN_DIR}/${FREENECT2_SONAME}")
echo "build-bridge: staged ${PLUGIN_DIR}/${FREENECT2_SONAME} (${FN2_SIZE_BYTES} bytes)"
