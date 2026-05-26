#!/usr/bin/env bash
# setup-ubuntu.sh
#
# One-shot dev setup for the AR Sandbox on Ubuntu 24. Mirrors README
# sections 1–5: apt deps, libfreenect2 source + Ubuntu 24 patch + build,
# udev rule install, AMD iommu=pt check. Idempotent — safe to re-run.
#
# After this completes, run ./scripts/build-bridge.sh and open the
# project in Unity 6.

set -euo pipefail

FREENECT2_REPO="https://github.com/OpenKinect/libfreenect2.git"
FREENECT2_SRC="${HOME}/libfreenect2"
FREENECT2_PREFIX="${HOME}/freenect2"
UDEV_RULE_DST="/etc/udev/rules.d/90-kinect2.rules"

log()  { printf '\n[setup]  %s\n' "$*"; }
warn() { printf '\n[setup]  WARN: %s\n' "$*" >&2; }
die()  { printf '\n[setup]  ERROR: %s\n' "$*" >&2; exit 1; }

[[ "$(uname -s)" == "Linux" ]] || die "this script targets Linux only"

# Refresh sudo credentials up front so the script doesn't pause mid-flight.
sudo -v

# ---------------------------------------------------------------------------
# 1. apt dependencies
# ---------------------------------------------------------------------------
log "Installing build dependencies via apt"
sudo apt update
sudo apt install -y \
    build-essential cmake pkg-config git \
    libusb-1.0-0-dev libturbojpeg0-dev libglfw3-dev \
    ocl-icd-opencl-dev opencl-headers intel-opencl-icd \
    libva-dev libjpeg-dev

# ---------------------------------------------------------------------------
# 2. libfreenect2 source + Ubuntu 24 patch + build
# ---------------------------------------------------------------------------
if [[ -d "${FREENECT2_SRC}/.git" ]]; then
    log "libfreenect2 source already at ${FREENECT2_SRC} (skipping clone)"
else
    log "Cloning libfreenect2 to ${FREENECT2_SRC}"
    git clone "${FREENECT2_REPO}" "${FREENECT2_SRC}"
fi

# Ubuntu 24's opencl-headers defines CL_ICDL_VERSION as a macro, which collides
# with a local const of the same name in two libfreenect2 sources. Rename the
# locals to OCL_ICDL_VERSION. The sed is idempotent — a second run matches
# nothing.
log "Applying Ubuntu 24 OpenCL 3.0 macro-collision patch (idempotent)"
for f in \
    "${FREENECT2_SRC}/src/opencl_depth_packet_processor.cpp" \
    "${FREENECT2_SRC}/src/opencl_kde_depth_packet_processor.cpp"; do
    [[ -f "$f" ]] || die "expected ${f} to exist — has libfreenect2's layout changed?"
    sed -i 's/\bCL_ICDL_VERSION\b/OCL_ICDL_VERSION/g' "$f"
done

log "Building libfreenect2 → ${FREENECT2_PREFIX}"
mkdir -p "${FREENECT2_SRC}/build"
(
    cd "${FREENECT2_SRC}/build"
    cmake .. \
        -DCMAKE_INSTALL_PREFIX="${FREENECT2_PREFIX}" \
        -DCMAKE_BUILD_TYPE=Release \
        -DENABLE_CXX11=ON \
        -DENABLE_OPENCL=ON
    make -j"$(nproc)"
    make install
)

# ---------------------------------------------------------------------------
# 3. udev rule
# ---------------------------------------------------------------------------
UDEV_RULE_SRC="${FREENECT2_SRC}/platform/linux/udev/90-kinect2.rules"
if [[ -f "${UDEV_RULE_DST}" ]] && cmp -s "${UDEV_RULE_SRC}" "${UDEV_RULE_DST}"; then
    log "udev rule already in place at ${UDEV_RULE_DST} (skipping)"
else
    log "Installing udev rule to ${UDEV_RULE_DST}"
    sudo cp "${UDEV_RULE_SRC}" "${UDEV_RULE_DST}"
    sudo udevadm control --reload-rules
    sudo udevadm trigger
    warn "Unplug and replug the Kinect's USB cable for the new udev rule to take effect."
fi

# ---------------------------------------------------------------------------
# 4. AMD xHCI / iommu=pt check
# ---------------------------------------------------------------------------
if grep -q 'iommu=pt' /proc/cmdline; then
    log "iommu=pt already on kernel cmdline"
elif lspci -nn 2>/dev/null | grep -qiE '1022:.*USB|AMD.*xHCI'; then
    warn "AMD USB controller detected and iommu=pt is NOT on the kernel cmdline."
    warn "Kinect v2 streaming may hit 'xhci_hcd: HC couldn't access mem fast enough' /"
    warn "'subpacket too large' errors without it. To fix:"
    warn "  sudo sed -i 's/^GRUB_CMDLINE_LINUX_DEFAULT=\"\\(.*\\)\"/GRUB_CMDLINE_LINUX_DEFAULT=\"\\1 iommu=pt\"/' /etc/default/grub"
    warn "  sudo update-grub"
    warn "  # then reboot"
    warn "See README §4 (USB controller note) for context."
fi

# ---------------------------------------------------------------------------
# 5. Done
# ---------------------------------------------------------------------------
log "System setup complete."
log ""
log "Next steps:"
log "  1. Plug in the Kinect (USB 3 SuperSpeed port + 12V wall power)."
log "  2. Verify the system layer:"
log "       cd ${FREENECT2_SRC}/build && ./bin/Protonect clkde -noviewer -frames 300"
log "     A clean exit (no 'subpacket too large' / 'timeout') means it's good."
log "  3. Build the Unity bridge:"
log "       ./scripts/build-bridge.sh"
log "  4. Open the project in Unity 6000.4.7f1."
