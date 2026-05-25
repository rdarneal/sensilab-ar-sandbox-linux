/*
 * kinect_bridge.h — C ABI between Unity (C# P/Invoke) and libfreenect2.
 *
 * Keep this header trivially C-callable: no C++ types in signatures, no
 * structs with non-trivial layout, no exceptions across the boundary.
 * P/Invoke marshalling is easier when nothing surprises it.
 *
 * Threading: the bridge owns one internal listener thread that pulls frames
 * from libfreenect2. The Unity side never blocks waiting for a frame — it
 * polls kb_try_get_depth_frame() each Update, and the bridge hands back the
 * most recent frame plus a monotonically increasing sequence number so the
 * caller can detect "no new frame since last poll."
 *
 * Lifetime: callers do init -> open -> start -> (poll loop) -> stop -> close
 * -> shutdown. Multiple devices not supported; this matches the existing
 * single-Kinect assumption in KinectManager.cs.
 */

#ifndef KINECT_BRIDGE_H
#define KINECT_BRIDGE_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* Visibility: GCC/Clang use __attribute__, MSVC uses __declspec.
 * We only target Linux for now but keep the macro for portability. */
#if defined(_WIN32) || defined(__CYGWIN__)
  #define KB_API __declspec(dllexport)
#else
  #define KB_API __attribute__((visibility("default")))
#endif

/* Status codes returned by every fallible call. 0 = success.
 * Negative values indicate distinct error classes so the C# side can log
 * something useful without parsing strings. */
typedef enum {
    KB_OK                  = 0,
    KB_ERR_NOT_INITIALISED = -1,  /* called before kb_init() */
    KB_ERR_ALREADY_OPEN    = -2,  /* kb_open() while a device is already open */
    KB_ERR_NO_DEVICE       = -3,  /* libfreenect2 enumerated zero Kinects */
    KB_ERR_OPEN_FAILED     = -4,  /* device found but openDevice() failed */
    KB_ERR_NOT_OPEN        = -5,  /* operation requires an open device */
    KB_ERR_NOT_STARTED     = -6,  /* poll called before kb_start() */
    KB_ERR_PIPELINE_FAILED = -7,  /* requested pipeline (e.g. clkde) failed to init */
    KB_ERR_BUFFER_TOO_SMALL= -8,  /* caller buffer < width*height*sizeof(uint16_t) */
    KB_ERR_NO_NEW_FRAME    = -9,  /* poll succeeded but no frame since last call */
    KB_ERR_INTERNAL        = -99  /* anything else; check log */
} kb_status_t;

/* Pipeline selector — passed to kb_open(). Bridge falls through to the
 * next-cheapest option on failure unless KB_PIPELINE_STRICT is set.
 *
 * KB_PIPELINE_DEFAULT picks the best available: clkde -> cl -> gl -> cpu.
 * For the user's 1080 Ti machine this resolves to clkde at ~535 Hz. */
typedef enum {
    KB_PIPELINE_DEFAULT = 0,
    KB_PIPELINE_CLKDE   = 1,
    KB_PIPELINE_CL      = 2,
    KB_PIPELINE_GL      = 3,
    KB_PIPELINE_CPU     = 4
} kb_pipeline_t;

/* Layout MUST match the C# [StructLayout(LayoutKind.Sequential)] struct.
 * Don't reorder, don't insert fields in the middle — only append. */
typedef struct {
    int32_t width;            /* always 512 for Kinect v2 depth */
    int32_t height;           /* always 424 for Kinect v2 depth */
    float   min_depth_mm;     /* libfreenect2 reports ~500 */
    float   max_depth_mm;     /* libfreenect2 reports ~4500 */
    float   fx, fy;           /* IR camera intrinsics */
    float   cx, cy;
} kb_depth_descriptor_t;

/* --- Lifecycle ----------------------------------------------------------- */

/* Initialise the bridge. Idempotent; safe to call multiple times. */
KB_API kb_status_t kb_init(void);

/* Tear down the bridge. After this, kb_init() must be called again before
 * any other function. Safe to call from a Unity OnApplicationQuit. */
KB_API void kb_shutdown(void);

/* Open the first available Kinect v2 with the requested pipeline.
 * Pass KB_PIPELINE_DEFAULT unless you have a reason to pin one.
 * Pass `strict=0` to allow fallback on pipeline init failure, `1` to fail hard. */
KB_API kb_status_t kb_open(kb_pipeline_t pipeline, int32_t strict);

/* Close the device; call kb_stop() first if streaming. */
KB_API kb_status_t kb_close(void);

/* Start the depth stream. After this returns KB_OK, kb_try_get_depth_frame()
 * will begin yielding frames within ~1 second (warm-up). */
KB_API kb_status_t kb_start(void);

/* Stop the depth stream but keep the device open. */
KB_API kb_status_t kb_stop(void);

/* --- Query --------------------------------------------------------------- */

/* Fill `out` with the device's depth frame descriptor. Valid after kb_open(),
 * BUT note: libfreenect2's getIrCameraParams() returns zeros until the stream
 * has started at least once. Width/height/min/max are always correct; the
 * fx/fy/cx/cy intrinsics are only populated after kb_start() has run. Calibration
 * code that needs intrinsics should therefore call this after kb_start(). */
KB_API kb_status_t kb_get_depth_descriptor(kb_depth_descriptor_t* out);

/* --- Frame polling ------------------------------------------------------- */

/*
 * Copy the most recently received depth frame into `buffer`.
 *
 * `buffer_len_bytes` must be >= width*height*sizeof(uint16_t) (= 512*424*2
 *   = 434176 for Kinect v2).
 * `out_sequence` (optional, may be NULL) receives a monotonically increasing
 *   frame counter — caller can compare against the previous value to detect
 *   "new frame since last poll." (Sensor outputs ~30 Hz.)
 *
 * Returns:
 *   KB_OK                if a frame was copied (whether new or not — check seq).
 *   KB_ERR_NO_NEW_FRAME  if no frame has arrived yet since kb_start()
 *                        (typical only during the ~1s warm-up).
 *   KB_ERR_NOT_STARTED   if kb_start() hasn't been called.
 *   KB_ERR_BUFFER_TOO_SMALL if buffer is too small.
 *
 * The depth values in `buffer` are millimetres as uint16, exactly matching
 * what the old Windows Kinect SDK produced — KinectManager's downstream
 * filtering and the SandboxComputeShader expect that exact format.
 */
KB_API kb_status_t kb_try_get_depth_frame(uint16_t* buffer,
                                          size_t buffer_len_bytes,
                                          uint64_t* out_sequence);

/* --- Diagnostics --------------------------------------------------------- */

/* Returns a static string identifying the active pipeline ("clkde", "gl",
 * "cpu", etc.) or "none" if no device is open. Caller does NOT free. */
KB_API const char* kb_active_pipeline_name(void);

/* libfreenect2 version, baked at compile time. Useful in logs. */
KB_API const char* kb_freenect2_version(void);

#ifdef __cplusplus
}
#endif

#endif /* KINECT_BRIDGE_H */
