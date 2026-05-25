# SensiLab AR Sandbox
![SensiLab AR Sandbox](https://sensilab.monash.edu/new-sensilab/wp-content/uploads/2018/06/43I5615.jpg)
This project is a from-scratch Unity rebuild and extension of the AR Sandbox project developed by the [KeckCAVES group at UC Davis](https://web.cs.ucdavis.edu/~okreylos/ResDev/SARndbox/)

The new version was developed in [SensiLab](https://sensilab.monash.edu) at Monash University, Melbourne, Australia

Updates include:
* Rebuilt in Unity for faster iteration of new features
* Updated Water simulations
* Fire simulations
* Wind simulations
* Complex Geology simulations
* Ability to save, recall and export topologies
* Various configuration options
* Support for a secondary touch screen

## Linux fork

This repository (`rdarneal/sensilab-ar-sandbox-linux`) is an independent fork of the original [SensiLab/sensilab-ar-sandbox](https://github.com/SensiLab/sensilab-ar-sandbox), porting it from Windows 7 + Microsoft Kinect SDK 2.0 to **Ubuntu 24 + libfreenect2** under **Unity 6 (6000.4.7f1)**. The upstream Microsoft SDK is Windows-only and unmaintained; libfreenect2 (OpenKinect) is the mature Linux driver for the Kinect v2 sensor.

The port is in progress — see [`CLAUDE.md`](CLAUDE.md) for the architecture map and the per-phase status.

## Requirements

* Ubuntu 24.04 (tested) — kernel 6.14, x86_64
* Unity **6000.4.7f1**
* Kinect v2 sensor (Microsoft model 1656) + Kinect PC adapter (model 1850, with the 12V wall power brick)
* USB 3.0 port (see [USB controller note](#usb-controller-note) below for AMD systems)
* GPU with OpenCL 1.2+ (NVIDIA proprietary driver recommended; Intel/AMD also supported)

## Setup (Linux)

> Setup is currently a manual sequence. A `scripts/setup-ubuntu.sh` will land once the steps stabilise.

### 1. System dependencies

```bash
sudo apt install -y build-essential cmake pkg-config \
    libusb-1.0-0-dev libturbojpeg0-dev libglfw3-dev \
    ocl-icd-opencl-dev opencl-headers intel-opencl-icd \
    libva-dev libjpeg-dev
```

### 2. Build libfreenect2

```bash
git clone https://github.com/OpenKinect/libfreenect2.git ~/libfreenect2
cd ~/libfreenect2
```

**Patch required on Ubuntu 24:** the OpenCL 3.0 headers shipped by `opencl-headers` define `CL_ICDL_VERSION` as a macro, which collides with a local variable of the same name in two libfreenect2 sources. Rename the local in both files (`opencl_depth_packet_processor.cpp` and `opencl_kde_depth_packet_processor.cpp`) — at the variable declaration (`const int CL_ICDL_VERSION = 2;`) and the single use site (`clGetICDLoaderInfoOCLICD(CL_ICDL_VERSION, ...)`), change `CL_ICDL_VERSION` to `OCL_ICDL_VERSION`. Then:

```bash
mkdir build && cd build
cmake .. -DCMAKE_INSTALL_PREFIX=$HOME/freenect2 -DENABLE_CXX11=ON -DENABLE_OPENCL=ON
make -j"$(nproc)" && make install
```

### 3. Install udev rules

```bash
sudo cp ~/libfreenect2/platform/linux/udev/90-kinect2.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
```

Then unplug/replug the Kinect's USB cable.

### 4. USB controller note

Kinect v2 saturates USB 3 bandwidth. On **AMD Ryzen platforms**, the CPU-integrated xHCI controller may produce `WARN: HC couldn't access mem fast enough` errors and stream timeouts. Workaround: add `iommu=pt` to the kernel cmdline.

```bash
sudo sed -i 's/^GRUB_CMDLINE_LINUX_DEFAULT="\(.*\)"/GRUB_CMDLINE_LINUX_DEFAULT="\1 iommu=pt"/' /etc/default/grub
sudo update-grub
# reboot
```

If your system has a separate chipset USB 3 controller (visible in `lspci -nn | grep USB`), plugging the Kinect into one of its ports may also resolve the issue without a kernel change.

### 5. Verify

```bash
cd ~/libfreenect2/build
./bin/Protonect clkde -noviewer -frames 300
```

A clean exit with no `subpacket too large` / `timeout` confirms the system layer is good. The `clkde` backend uses OpenCL Kernel Density Estimation — higher-quality depth than the default `gl` processor.

### 6. Build the Kinect bridge

The native shim that lets Unity talk to libfreenect2 lives at `native/kinect-bridge/`. The built `.so` is checked into `Assets/Plugins/Linux/x86_64/`, so most contributors never have to rebuild it. Only run this step if you've modified `native/kinect-bridge/src/kinect_bridge.cpp` or the C ABI in `include/kinect_bridge.h`.

```bash
./scripts/build-bridge.sh
```

The script CMake-configures `native/kinect-bridge/build/`, compiles `libkinectbridge.so` and a `smoke_test` binary, then stages the bridge **and** `libfreenect2.so.0.2` (copied out of `$HOME/freenect2/lib`) into `Assets/Plugins/Linux/x86_64/`. The bridge's RUNPATH leads with `$ORIGIN`, so the bundled libfreenect2 is what gets loaded both in the Editor and in any standalone build — the system install is only a build-time dep.

Smoke test the bridge against the actual Kinect before opening Unity:

```bash
./native/kinect-bridge/build/smoke_test
```

Exit code 0 with ~30 unique-sequence frames over the run means the bridge is healthy. Anything else points at libfreenect2/USB/permissions before it touches Unity.

### 7. Open the Unity project

1. Install Unity **6000.4.7f1** via Unity Hub.
2. Open the repo directory as a project. First import takes 5–15 minutes while Unity builds the `Library/` cache.
3. The only playable scene is `Assets/Sandbox/Scenes/SandboxScene.unity` — it's already in Build Settings.
4. Open `Assets/Plugins/Linux/x86_64/libkinectbridge.so` in the Inspector and verify Plugin Importer shows: Native Plugin / x86_64 / Linux / Standalone enabled, everything else disabled. Do the same for `libfreenect2.so.0.2`. The hand-written `.meta`s set this up, but Unity may rewrite them on first import.
5. Press Play. The bridge initialises in `KinectManager.Awake()`, libfreenect2 takes ~1 s to spin up its stream, then sand changes drive the heightmap. Calibration runs from the `=` key.

### 8. Build a Linux standalone

1. **File → Build Profiles → Linux x86_64**. Scripting backend: **Mono** (IL2CPP requires a `link.xml` to keep the P/Invoke surface from being stripped — defer).
2. Hit Build, point it at an output directory outside the project tree.
3. Unity packages `libkinectbridge.so` and `libfreenect2.so.0.2` into `<Build>_Data/Plugins/x86_64/` next to each other. The bridge's `$ORIGIN` rpath finds the bundled libfreenect2 from there — no `LD_LIBRARY_PATH` needed.
4. Launch the produced `ARSandbox-2.0.x86_64`. Same hardware requirements as Editor play.

### Deploy on a different machine

The Unity build bundles libfreenect2, but its transitive system deps still have to be present, and the Kinect needs udev rules:

```bash
sudo apt install -y libusb-1.0-0 libturbojpeg libglfw3 \
    libgl1 libva2 libva-drm2 libudev1 libdrm2 libx11-6 \
    ocl-icd-libopencl1
# Plus an OpenCL ICD: intel-opencl-icd, mesa-opencl-icd,
# or the NVIDIA proprietary driver (registers its own ICD).
```

```bash
sudo cp 90-kinect2.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
```

(Grab `90-kinect2.rules` from the libfreenect2 source tree on the dev box — `~/libfreenect2/platform/linux/udev/90-kinect2.rules` — and ship it alongside the build.)

If the target is an AMD Ryzen box, you'll likely need `iommu=pt` on the kernel cmdline too (see [USB controller note](#4-usb-controller-note)).

## License
GNU General Public License v3.0 or later

See [COPYING](COPYING) to see the full text
