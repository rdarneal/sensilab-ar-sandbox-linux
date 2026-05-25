// kinect_bridge.cpp — libfreenect2-backed implementation of the C ABI
// declared in include/kinect_bridge.h. See header for the contract.

#include "kinect_bridge.h"

#include <libfreenect2/libfreenect2.hpp>
#include <libfreenect2/frame_listener_impl.h>
#include <libfreenect2/packet_pipeline.h>
#include <libfreenect2/registration.h>
#include <libfreenect2/config.h>

#include <atomic>
#include <chrono>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace {

constexpr int    KB_DEPTH_W = 512;
constexpr int    KB_DEPTH_H = 424;
constexpr size_t KB_DEPTH_BYTES = static_cast<size_t>(KB_DEPTH_W) * KB_DEPTH_H * sizeof(uint16_t);
constexpr float  KB_MIN_DEPTH_MM = 500.0f;
constexpr float  KB_MAX_DEPTH_MM = 4500.0f;

void kb_log(const char* fmt, ...) {
    std::va_list ap;
    va_start(ap, fmt);
    std::fprintf(stderr, "[kinect_bridge] ");
    std::vfprintf(stderr, fmt, ap);
    std::fprintf(stderr, "\n");
    va_end(ap);
}

struct BridgeState {
    bool initialised = false;
    bool device_open = false;
    bool streaming   = false;

    std::unique_ptr<libfreenect2::Freenect2> ctx;
    libfreenect2::Freenect2Device*           dev = nullptr;
    std::unique_ptr<libfreenect2::SyncMultiFrameListener> listener;

    const char* pipeline_name = "none";
    kb_depth_descriptor_t descriptor{};

    // Latest-frame slot. The listener thread writes; pollers read.
    std::mutex                latest_mu;
    std::vector<uint16_t>     latest;        // KB_DEPTH_W*KB_DEPTH_H elements
    std::atomic<uint64_t>     latest_seq{0}; // 0 == no frame yet

    // Listener thread control.
    std::thread               worker;
    std::atomic<bool>         stop_flag{false};
};

BridgeState g_state;

// Construct a pipeline of the requested kind. Returns nullptr on failure
// (or for combos libfreenect2 wasn't built with).
libfreenect2::PacketPipeline* make_pipeline(kb_pipeline_t which, const char** out_name) {
    try {
        switch (which) {
            case KB_PIPELINE_CLKDE:
#ifdef LIBFREENECT2_WITH_OPENCL_SUPPORT
                *out_name = "clkde";
                return new libfreenect2::OpenCLKdePacketPipeline(-1);
#else
                return nullptr;
#endif
            case KB_PIPELINE_CL:
#ifdef LIBFREENECT2_WITH_OPENCL_SUPPORT
                *out_name = "cl";
                return new libfreenect2::OpenCLPacketPipeline(-1);
#else
                return nullptr;
#endif
            case KB_PIPELINE_GL:
#ifdef LIBFREENECT2_WITH_OPENGL_SUPPORT
                *out_name = "gl";
                return new libfreenect2::OpenGLPacketPipeline();
#else
                return nullptr;
#endif
            case KB_PIPELINE_CPU:
                *out_name = "cpu";
                return new libfreenect2::CpuPacketPipeline();
            default:
                return nullptr;
        }
    } catch (const std::exception& e) {
        kb_log("pipeline ctor threw: %s", e.what());
        return nullptr;
    }
}

void worker_loop() {
    while (!g_state.stop_flag.load(std::memory_order_acquire)) {
        libfreenect2::FrameMap frames;
        bool got = false;
        try {
            // 1000ms keeps us responsive to stop_flag without spinning.
            got = g_state.listener->waitForNewFrame(frames, 1000);
        } catch (const std::exception& e) {
            kb_log("waitForNewFrame threw: %s", e.what());
            got = false;
        }
        if (!got) {
            continue;
        }

        auto it = frames.find(libfreenect2::Frame::Depth);
        if (it != frames.end() && it->second != nullptr) {
            libfreenect2::Frame* depth = it->second;
            if (depth->width == static_cast<size_t>(KB_DEPTH_W) &&
                depth->height == static_cast<size_t>(KB_DEPTH_H) &&
                depth->data != nullptr) {
                const float* src = reinterpret_cast<const float*>(depth->data);
                std::lock_guard<std::mutex> lk(g_state.latest_mu);
                uint16_t* dst = g_state.latest.data();
                const size_t n = static_cast<size_t>(KB_DEPTH_W) * KB_DEPTH_H;
                for (size_t i = 0; i < n; ++i) {
                    float v = src[i];
                    // libfreenect2 yields non-positive / NaN / inf for missing data;
                    // collapse those to 0 to match the old SDK's "no return" sentinel.
                    if (!(v > 0.0f) || !std::isfinite(v)) {
                        dst[i] = 0;
                    } else if (v >= 65535.0f) {
                        dst[i] = 65535;
                    } else {
                        dst[i] = static_cast<uint16_t>(v);
                    }
                }
                g_state.latest_seq.fetch_add(1, std::memory_order_release);
            }
        }
        g_state.listener->release(frames);
    }
}

} // namespace

extern "C" {

KB_API kb_status_t kb_init(void) {
    if (g_state.initialised) return KB_OK;
    try {
        g_state.ctx.reset(new libfreenect2::Freenect2());
        g_state.latest.assign(static_cast<size_t>(KB_DEPTH_W) * KB_DEPTH_H, 0);
        g_state.latest_seq.store(0);
        g_state.initialised = true;
        kb_log("init ok (libfreenect2 %s)", LIBFREENECT2_VERSION);
        return KB_OK;
    } catch (const std::exception& e) {
        kb_log("init failed: %s", e.what());
        g_state.ctx.reset();
        return KB_ERR_INTERNAL;
    }
}

KB_API void kb_shutdown(void) {
    if (!g_state.initialised) return;
    if (g_state.streaming) kb_stop();
    if (g_state.device_open) kb_close();
    g_state.ctx.reset();
    g_state.initialised = false;
    g_state.pipeline_name = "none";
    g_state.latest_seq.store(0);
    kb_log("shutdown ok");
}

KB_API kb_status_t kb_open(kb_pipeline_t pipeline, int32_t strict) {
    if (!g_state.initialised) return KB_ERR_NOT_INITIALISED;
    if (g_state.device_open)  return KB_ERR_ALREADY_OPEN;

    int n = 0;
    try {
        n = g_state.ctx->enumerateDevices();
    } catch (const std::exception& e) {
        kb_log("enumerateDevices threw: %s", e.what());
        return KB_ERR_INTERNAL;
    }
    if (n <= 0) {
        kb_log("no Kinect v2 enumerated");
        return KB_ERR_NO_DEVICE;
    }
    std::string serial;
    try {
        serial = g_state.ctx->getDefaultDeviceSerialNumber();
    } catch (const std::exception& e) {
        kb_log("getDefaultDeviceSerialNumber threw: %s", e.what());
        return KB_ERR_INTERNAL;
    }
    if (serial.empty()) return KB_ERR_NO_DEVICE;

    // Build the fallback ladder. If KB_PIPELINE_DEFAULT, run the full ladder;
    // if a specific kind is requested non-strict, try it first then the others;
    // if strict, only the requested one.
    std::vector<kb_pipeline_t> attempts;
    if (pipeline == KB_PIPELINE_DEFAULT) {
        attempts = {KB_PIPELINE_CLKDE, KB_PIPELINE_CL, KB_PIPELINE_GL, KB_PIPELINE_CPU};
    } else if (strict) {
        attempts = {pipeline};
    } else {
        attempts.push_back(pipeline);
        for (auto p : {KB_PIPELINE_CLKDE, KB_PIPELINE_CL, KB_PIPELINE_GL, KB_PIPELINE_CPU}) {
            if (p != pipeline) attempts.push_back(p);
        }
    }

    libfreenect2::Freenect2Device* dev = nullptr;
    const char* chosen_name = "none";
    for (kb_pipeline_t which : attempts) {
        const char* name = "none";
        libfreenect2::PacketPipeline* pl = make_pipeline(which, &name);
        if (!pl) {
            kb_log("pipeline '%d' unavailable, skipping", static_cast<int>(which));
            continue;
        }
        try {
            // openDevice takes ownership of `pl` and frees it on failure too.
            dev = g_state.ctx->openDevice(serial, pl);
        } catch (const std::exception& e) {
            kb_log("openDevice('%s') threw: %s", name, e.what());
            dev = nullptr;
        }
        if (dev) {
            chosen_name = name;
            kb_log("openDevice ok with pipeline '%s' (serial %s)", name, serial.c_str());
            break;
        }
        kb_log("openDevice with pipeline '%s' returned null", name);
    }

    if (!dev) {
        if (pipeline != KB_PIPELINE_DEFAULT && strict) {
            return KB_ERR_PIPELINE_FAILED;
        }
        return KB_ERR_OPEN_FAILED;
    }

    g_state.dev = dev;
    g_state.device_open = true;
    g_state.pipeline_name = chosen_name;

    // Descriptor: width/height/depth bounds known up-front. IR intrinsics are
    // populated lazily after kb_start(); libfreenect2 doesn't have the factory
    // calibration loaded until the device has been started at least once.
    g_state.descriptor.width        = KB_DEPTH_W;
    g_state.descriptor.height       = KB_DEPTH_H;
    g_state.descriptor.min_depth_mm = KB_MIN_DEPTH_MM;
    g_state.descriptor.max_depth_mm = KB_MAX_DEPTH_MM;
    g_state.descriptor.fx = 0.0f;
    g_state.descriptor.fy = 0.0f;
    g_state.descriptor.cx = 0.0f;
    g_state.descriptor.cy = 0.0f;

    return KB_OK;
}

KB_API kb_status_t kb_close(void) {
    if (!g_state.initialised) return KB_ERR_NOT_INITIALISED;
    if (!g_state.device_open) return KB_ERR_NOT_OPEN;
    if (g_state.streaming) kb_stop();
    try {
        if (g_state.dev) g_state.dev->close();
    } catch (const std::exception& e) {
        kb_log("device close threw: %s", e.what());
    }
    // Freenect2Device* is owned by the Freenect2 context; do not delete here.
    g_state.dev = nullptr;
    g_state.device_open = false;
    g_state.pipeline_name = "none";
    g_state.descriptor = kb_depth_descriptor_t{};
    kb_log("close ok");
    return KB_OK;
}

KB_API kb_status_t kb_start(void) {
    if (!g_state.initialised) return KB_ERR_NOT_INITIALISED;
    if (!g_state.device_open) return KB_ERR_NOT_OPEN;
    if (g_state.streaming)    return KB_OK;

    try {
        g_state.listener.reset(
            new libfreenect2::SyncMultiFrameListener(libfreenect2::Frame::Depth));
        g_state.dev->setIrAndDepthFrameListener(g_state.listener.get());
        if (!g_state.dev->startStreams(false, true)) {
            kb_log("startStreams returned false");
            g_state.listener.reset();
            return KB_ERR_INTERNAL;
        }
    } catch (const std::exception& e) {
        kb_log("start threw: %s", e.what());
        g_state.listener.reset();
        return KB_ERR_INTERNAL;
    }

    g_state.stop_flag.store(false, std::memory_order_release);
    g_state.latest_seq.store(0);
    g_state.worker = std::thread(worker_loop);
    g_state.streaming = true;

    // IR intrinsics are only populated after the device starts streaming, so
    // refresh the descriptor here.
    try {
        libfreenect2::Freenect2Device::IrCameraParams ir = g_state.dev->getIrCameraParams();
        g_state.descriptor.fx = ir.fx;
        g_state.descriptor.fy = ir.fy;
        g_state.descriptor.cx = ir.cx;
        g_state.descriptor.cy = ir.cy;
    } catch (const std::exception& e) {
        kb_log("getIrCameraParams threw post-start: %s (intrinsics left zero)", e.what());
    }

    kb_log("start ok (depth-only stream, listener thread running)");
    return KB_OK;
}

KB_API kb_status_t kb_stop(void) {
    if (!g_state.initialised) return KB_ERR_NOT_INITIALISED;
    if (!g_state.device_open) return KB_ERR_NOT_OPEN;
    if (!g_state.streaming)   return KB_OK;

    g_state.stop_flag.store(true, std::memory_order_release);
    if (g_state.worker.joinable()) g_state.worker.join();

    try {
        if (g_state.dev) g_state.dev->stop();
    } catch (const std::exception& e) {
        kb_log("device stop threw: %s", e.what());
    }
    g_state.listener.reset();
    g_state.streaming = false;
    kb_log("stop ok");
    return KB_OK;
}

KB_API kb_status_t kb_get_depth_descriptor(kb_depth_descriptor_t* out) {
    if (!g_state.initialised) return KB_ERR_NOT_INITIALISED;
    if (!g_state.device_open) return KB_ERR_NOT_OPEN;
    if (!out) return KB_ERR_INTERNAL;
    *out = g_state.descriptor;
    return KB_OK;
}

KB_API kb_status_t kb_try_get_depth_frame(uint16_t* buffer,
                                          size_t buffer_len_bytes,
                                          uint64_t* out_sequence) {
    if (!g_state.initialised) return KB_ERR_NOT_INITIALISED;
    if (!g_state.streaming)   return KB_ERR_NOT_STARTED;
    if (!buffer || buffer_len_bytes < KB_DEPTH_BYTES) return KB_ERR_BUFFER_TOO_SMALL;

    if (g_state.latest_seq.load(std::memory_order_acquire) == 0) {
        return KB_ERR_NO_NEW_FRAME;
    }

    std::lock_guard<std::mutex> lk(g_state.latest_mu);
    std::memcpy(buffer, g_state.latest.data(), KB_DEPTH_BYTES);
    if (out_sequence) {
        *out_sequence = g_state.latest_seq.load(std::memory_order_relaxed);
    }
    return KB_OK;
}

KB_API const char* kb_active_pipeline_name(void) {
    return g_state.pipeline_name ? g_state.pipeline_name : "none";
}

KB_API const char* kb_freenect2_version(void) {
    return LIBFREENECT2_VERSION;
}

} // extern "C"
