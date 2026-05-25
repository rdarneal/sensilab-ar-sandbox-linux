/* smoke_test.c — exercise the kinect_bridge C ABI against a real Kinect v2.
 *
 * Sequence: init -> open(default) -> describe -> start -> poll 10s or 30 unique
 * frames -> stop -> close -> shutdown. Exits 0 only if >=10 unique frames came
 * through cleanly. */

#include "kinect_bridge.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include <math.h>

#define KB_W 512
#define KB_H 424
#define KB_PIXELS (KB_W * KB_H)
#define POLL_DT_MS 33
#define POLL_TIMEOUT_S 10
#define TARGET_FRAMES 30
#define MIN_FRAMES_PASS 10
#define CENTER_X 256
#define CENTER_Y 212

static double now_seconds(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (double)ts.tv_sec + (double)ts.tv_nsec / 1e9;
}

static void sleep_ms(int ms) {
    struct timespec req;
    req.tv_sec = ms / 1000;
    req.tv_nsec = (long)(ms % 1000) * 1000000L;
    nanosleep(&req, NULL);
}

int main(void) {
    int exit_code = 1;
    uint64_t unique_frames = 0;
    int      hard_error = 0;
    uint16_t* buf = (uint16_t*)malloc(sizeof(uint16_t) * KB_PIXELS);
    if (!buf) {
        fprintf(stderr, "alloc failed\n");
        return 1;
    }
    memset(buf, 0, sizeof(uint16_t) * KB_PIXELS);

    printf("[smoke] libfreenect2 version = %s\n", kb_freenect2_version());

    kb_status_t st = kb_init();
    if (st != KB_OK) { fprintf(stderr, "[smoke] kb_init failed: %d\n", st); goto done; }

    st = kb_open(KB_PIPELINE_DEFAULT, 0);
    if (st != KB_OK) { fprintf(stderr, "[smoke] kb_open failed: %d\n", st); goto shutdown; }

    printf("[smoke] active pipeline = %s\n", kb_active_pipeline_name());

    kb_depth_descriptor_t desc;
    st = kb_get_depth_descriptor(&desc);
    if (st != KB_OK) { fprintf(stderr, "[smoke] kb_get_depth_descriptor failed: %d\n", st); goto close_dev; }
    printf("[smoke] descriptor: %dx%d, depth [%.1f..%.1f] mm, fx=%.3f fy=%.3f cx=%.3f cy=%.3f\n",
           desc.width, desc.height, desc.min_depth_mm, desc.max_depth_mm,
           desc.fx, desc.fy, desc.cx, desc.cy);

    st = kb_start();
    if (st != KB_OK) { fprintf(stderr, "[smoke] kb_start failed: %d\n", st); goto close_dev; }

    uint64_t last_seq = 0;
    int      stale_polls = 0;
    int      warmup_polls = 0;
    int      first_frame_seen = 0;

    uint16_t center_min = 0xFFFF;
    uint16_t center_max = 0;
    double   center_sum = 0.0;
    uint64_t center_count = 0;
    uint64_t nonzero_pixels_total = 0;
    uint16_t frame_max_ever = 0;
    double   first_frame_t = 0.0;
    double   last_frame_t = 0.0;

    double t_start = now_seconds();
    while (unique_frames < TARGET_FRAMES) {
        double t_now = now_seconds();
        if (t_now - t_start >= POLL_TIMEOUT_S) break;

        uint64_t seq = 0;
        st = kb_try_get_depth_frame(buf, sizeof(uint16_t) * KB_PIXELS, &seq);
        if (st == KB_OK) {
            if (seq != last_seq) {
                last_seq = seq;
                unique_frames++;
                if (!first_frame_seen) {
                    first_frame_seen = 1;
                    first_frame_t = t_now;
                }
                last_frame_t = t_now;
                uint16_t v = buf[CENTER_Y * KB_W + CENTER_X];
                if (v < center_min) center_min = v;
                if (v > center_max) center_max = v;
                center_sum += (double)v;
                center_count++;
                // Cheap sanity check: are ANY pixels non-zero? Tells us whether
                // an all-zero center is "scene hole" vs "decoder broken."
                for (int i = 0; i < KB_PIXELS; ++i) {
                    uint16_t pv = buf[i];
                    if (pv != 0) nonzero_pixels_total++;
                    if (pv > frame_max_ever) frame_max_ever = pv;
                }
            } else {
                stale_polls++;
            }
        } else if (st == KB_ERR_NO_NEW_FRAME) {
            if (first_frame_seen) {
                // Contract: NO_NEW_FRAME must NOT fire in steady state.
                fprintf(stderr, "[smoke] WARN: NO_NEW_FRAME after first frame (steady-state violation)\n");
                hard_error = 1;
                break;
            }
            warmup_polls++;
        } else {
            fprintf(stderr, "[smoke] kb_try_get_depth_frame failed: %d\n", st);
            hard_error = 1;
            break;
        }
        sleep_ms(POLL_DT_MS);
    }

    double t_end = now_seconds();
    double elapsed = t_end - t_start;
    double mean_interval_ms = 0.0;
    if (unique_frames >= 2) {
        mean_interval_ms = (last_frame_t - first_frame_t) * 1000.0 / (double)(unique_frames - 1);
    }
    double center_mean = (center_count > 0) ? center_sum / (double)center_count : 0.0;

    printf("[smoke] ---- summary ----\n");
    printf("[smoke] elapsed_s        = %.3f\n", elapsed);
    printf("[smoke] unique_frames    = %llu\n", (unsigned long long)unique_frames);
    printf("[smoke] stale_polls      = %d\n", stale_polls);
    printf("[smoke] warmup_polls     = %d (NO_NEW_FRAME before first frame)\n", warmup_polls);
    printf("[smoke] mean_interval_ms = %.2f (steady-state, derived from first/last frame timestamps)\n", mean_interval_ms);
    printf("[smoke] depth[%d][%d]   min=%u max=%u mean=%.1f mm  (samples=%llu)\n",
           CENTER_Y, CENTER_X,
           (unsigned)center_min, (unsigned)center_max, center_mean,
           (unsigned long long)center_count);
    {
        double mean_nonzero_per_frame =
            (unique_frames > 0)
                ? (double)nonzero_pixels_total / (double)unique_frames
                : 0.0;
        printf("[smoke] nonzero_pixels/frame ~= %.0f / %d  (max value seen = %u mm)\n",
               mean_nonzero_per_frame, KB_PIXELS, (unsigned)frame_max_ever);
    }

    /* Re-fetch descriptor here to surface the post-start intrinsics. */
    {
        kb_depth_descriptor_t d2;
        if (kb_get_depth_descriptor(&d2) == KB_OK) {
            printf("[smoke] post-stream intrinsics: fx=%.3f fy=%.3f cx=%.3f cy=%.3f\n",
                   d2.fx, d2.fy, d2.cx, d2.cy);
        }
    }

    (void)kb_stop();
close_dev:
    (void)kb_close();
shutdown:
    kb_shutdown();

    if (!hard_error && unique_frames >= MIN_FRAMES_PASS) {
        exit_code = 0;
        printf("[smoke] PASS\n");
    } else {
        printf("[smoke] FAIL (hard_error=%d unique_frames=%llu min=%d)\n",
               hard_error, (unsigned long long)unique_frames, MIN_FRAMES_PASS);
    }
done:
    free(buf);
    return exit_code;
}
