# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project context

Unity AR Sandbox originally built for **Windows 7 + Microsoft Kinect for Windows SDK 2.0 + Kinect v2 sensor**, now being **ported to Ubuntu 24** under Unity 6 (`6000.4.7f1`). The fork is independent — this repo is `rdarneal/sensilab-ar-sandbox-linux`, **not** linked to upstream `SensiLab/sensilab-ar-sandbox`.

**Default assumption**: Linux is the target. Flag any Windows-specific code (`.dll`-bound P/Invoke, `Windows.Kinect`, paths with backslashes, `#if UNITY_STANDALONE_WIN`) as conversion candidates rather than code to preserve.

## Open, build, run

- Open the project in **Unity 6000.4.7f1** (see `ProjectSettings/ProjectVersion.txt`). The single playable scene is `Assets/Sandbox/Scenes/SandboxScene.unity`.
- There are no `.bat`/`Makefile`/CI scripts in the repo — builds are produced from the Editor's `File → Build Profiles` (target: **Linux Dedicated Server / Linux x86_64**, scripting backend Mono or IL2CPP).
- Headless Editor build from CLI: `Unity -batchmode -quit -projectPath <repo> -buildLinux64Player <output>` (run via the installed Unity 6 binary; not yet scripted).
- The project currently will **fail to enter Play mode on Linux** until the Kinect input layer is replaced — `Assets/Plugins/{Metro,x86,x86_64}/KinectUnityAddin.dll` is Windows-only and the depth pipeline depends on it. See *Kinect boundary* below.

## Architecture: the big picture

The whole system is a one-way pipeline `depth sensor → sandbox mesh → simulations → render`. Read these in order to understand it:

1. **`Assets/Sandbox/Scripts/SandboxBase/KinectManager.cs`** — *sole* sensor I/O. Opens the Kinect, polls `DepthFrameReader`, exposes `ushort[] depthData` and a `FrameDescription`. Everything downstream reads from this one component via getters.
2. **`Assets/Sandbox/Scripts/SandboxBase/Sandbox.cs`** — orchestrator. Holds a `KinectManager` ref, runs the depth pipeline (low-pass filter → downsample → blur → mesh generation → contour extraction), and owns the `RenderTexture`s the simulations sample from. Coordinates `SandboxCamera`, `SandboxDataCamera`, `SandboxContourCamera`, and `ModeSelector` (which simulation is active).
3. **`Assets/Sandbox/Scripts/SandboxBase/SandboxCSHelper.cs` + `Assets/Sandbox/ComputeShaders/SandboxComputeShader.compute`** — the GPU side: depth low-pass, Gaussian blur, Sobel + non-maximal suppression for contours, plane mesh generation. Compute shaders are HLSL-style but compile to SPIR-V on Linux/Vulkan; no changes expected.
4. **`Assets/Sandbox/Scripts/Calibration/`** — projector ↔ Kinect alignment. `CalibrationManager.cs` runs the routine; `CalibrationFileManager.cs` persists to `Application.persistentDataPath` (cross-platform). Calibration parameters will need re-tuning under the Linux Kinect stack because intrinsics from libfreenect2 differ slightly from Microsoft's SDK.
5. **`Assets/Sandbox/Scripts/TopographyBuilder/TopographyBuilder.cs`** — captures DEMs (low-pass-filtered depth snapshots) and stores them with the `FrameDescription` metadata. One of the three files that touches `Windows.Kinect` directly.
6. **Simulation modules**, all decoupled from Kinect — each is a `*CSHelper.cs` that dispatches a compute kernel against the sandbox mesh's height texture:
   - `WaterSimulation/` — shallow-water equations
   - `FireSimulation/` — fire spread on terrain
   - `WindSimulation/` — particle/velocity advection
   - `GeologySimulation/` — layered structural geology with save/load to `.geo` files

The UI (`Assets/Sandbox/Scripts/UI/`) and `SharingManager.cs` (email export via `System.Net.Mail`) have **no** Kinect coupling.

## Kinect boundary (the port surface)

Only **three** scripts directly `using Windows.Kinect;`:

- `Assets/Sandbox/Scripts/SandboxBase/KinectManager.cs` — depth acquisition (the entire sensor surface lives here)
- `Assets/Sandbox/Scripts/SandboxBase/Sandbox.cs` — references `KinectManager` only, no direct frame access (`using` may be removable)
- `Assets/Sandbox/Scripts/TopographyBuilder/TopographyBuilder.cs` — uses `FrameDescription` for stored frame dimensions

Behind them sits **`Assets/Standard Assets/Windows/Kinect/`** — ~80 managed wrapper classes auto-generated from Microsoft's Kinect SDK 2.0. They P/Invoke `KinectUnityAddin` (`[DllImport("KinectUnityAddin", CallingConvention=Cdecl)]`), and that native binary is shipped only as Windows DLLs in `Assets/Plugins/{Metro,x86,x86_64}/`.

For Linux, this whole wrapper layer is the thing to replace — the touchpoint scripts above (especially `KinectManager.cs`) are thin enough that a `libfreenect2`-backed shim can keep them mostly intact.

## Persistence formats

User data is written to `Application.persistentDataPath` via cross-platform `System.IO` APIs — these are already Linux-clean.

- Calibration → custom serialized struct via `CalibrationFileManager.cs` / `StoredCalibration.cs`
- Topography snapshots → `.props` files via `TopographyBuilder.cs` / `LoadedTopography.cs`
- Saved geology → `.geo` files via `GeologyFileManager.cs`
- `Assets/Depth.txt` (434 KB) is a recorded depth frame used as `MockData` for offline testing without a Kinect connected — useful when iterating on simulations.

## Conventions worth knowing

- Compute-shader dispatch helpers are named `<Domain>CSHelper.cs` and live next to the `.compute` they wrap. Treat that pair as one unit.
- Simulations are independent: changes to one (e.g. `WaterSurfaceCSHelper.cs`) should not touch `SandboxBase/`. `Sandbox.cs` exposes a stable mesh/height texture interface they all read from.
- Scenes: only `SandboxScene.unity`. There is no scene-flow multiplexing; `ModeSelector.cs` swaps active simulation GameObjects within the one scene.
- `Assets/Plugins/{Metro,x86,x86_64}/KinectUnityAddin.dll` will need plugin-importer settings updated (or be removed) once a Linux replacement lands, otherwise Unity will warn on Linux builds about the missing platform binary.
