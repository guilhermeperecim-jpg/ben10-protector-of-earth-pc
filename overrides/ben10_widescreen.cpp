// Ben 10: Protector of Earth (USA, SLUS-21661 v1.01): widescreen.
// Copied into PS2Recomp's runner folder by tools/sync_generated.py.
//
// The camera's projection is built by FUN_00119408(camera): perspective FUN_00118cf0 gets the shape as
// $f13 = height / (width * 1.0714), orthographic FUN_00118e40 gets the half widths $f14/$f15 * 1.0714. Widening the
// view by target / (4/3) on both (the same change as PCSX2's widescreen patch, which turns 1.0714 into 0.8036 for 16:9)
// draws the 3-D squeezed into the 4:3 frame buffer and the window stretches it back. From the level's first camera on,
// every frame is wide (the pause menu too, which builds no camera but shows the level behind it) until another map
// opens; while a movie plays, 4:3 (the loading tips before the first camera are 4:3 as well).
// The menus are maps too (99_*, with a 3-D background behind 2-D pictures): there the cameras are left alone and the
// frames stay 4:3, as do the movies. Which map is open: the game's debug printf (0x205600), "Opening %s (%s) ..."
// (0x248408) with the map's name. The shape: settings.toml [display] shape (VENDETTA_WIDESCREEN).
#include "ps2_runtime.h"
#include "ps2_runtime_macros.h"
#include "runtime/ps2_widescreen.h"

#include <cstdio>
#include <cstdlib>
#include <atomic>
#include <chrono>
#include <set>
#include <thread>

namespace
{
    constexpr uint32_t kCameraProjection = 0x119408u, kPerspective = 0x118CF0u, kOrthographic = 0x118E40u;
    constexpr uint32_t kDebugPrintf = 0x205600u, kOpeningMap = 0x248408u;
    PS2Runtime::RecompiledFunction g_camera = nullptr, g_perspective = nullptr, g_orthographic = nullptr, g_printf = nullptr;
    bool g_menuMap = true;                 // the menus come first
    std::atomic<bool> g_levelShown{false}; // a level's camera built since its map opened
    float g_widen = 1.0f; // target / (4/3)
    int g_inCamera = 0;

    void cameraProjection(uint8_t *rdram, R5900Context *ctx, PS2Runtime *runtime)
    {
        static const bool log = std::getenv("PS2X_WSLOG") != nullptr;
        static std::set<uint32_t> seen;
        if (log && seen.insert(GPR_U32(ctx, 31)).second)
            std::fprintf(stderr, "[widescreen] camera from ra=0x%x camera=0x%x%c", GPR_U32(ctx, 31), GPR_U32(ctx, 4), 10);
        if (g_menuMap)
        {
            g_camera(rdram, ctx, runtime);
            return;
        }
        ++g_inCamera;
        g_camera(rdram, ctx, runtime);
        --g_inCamera;
        g_levelShown.store(true, std::memory_order_relaxed);
    }

    void debugPrintf(uint8_t *rdram, R5900Context *ctx, PS2Runtime *runtime)
    {
        if (GPR_U32(ctx, 4) == kOpeningMap)
        {
            const uint8_t *name = rdram + (GPR_U32(ctx, 5) & 0x01FFFFFFu);
            g_menuMap = name[0] == '9' && name[1] == '9' && name[2] == '_';
            g_levelShown.store(false, std::memory_order_relaxed);
        }
        g_printf(rdram, ctx, runtime);
    }

    void perspective(uint8_t *rdram, R5900Context *ctx, PS2Runtime *runtime)
    {
        if (g_inCamera)
            ctx->f[13] *= g_widen;
        g_perspective(rdram, ctx, runtime);
    }

    void orthographic(uint8_t *rdram, R5900Context *ctx, PS2Runtime *runtime)
    {
        if (g_inCamera)
        {
            ctx->f[14] /= g_widen;
            ctx->f[15] /= g_widen;
        }
        g_orthographic(rdram, ctx, runtime);
    }
}

bool ben10VideoPlaying(); // ben10_videos.cpp

void installBen10Widescreen(PS2Runtime &runtime)
{
    const float target = ps2_widescreen::targetAspect();
    if (target <= 0.0f)
        return;
    g_camera = runtime.lookupFunction(kCameraProjection);
    g_perspective = runtime.lookupFunction(kPerspective);
    g_orthographic = runtime.lookupFunction(kOrthographic);
    g_printf = runtime.lookupFunction(kDebugPrintf);
    if (!g_camera || !g_perspective || !g_orthographic || !g_printf)
    {
        std::fprintf(stderr, "[widescreen] camera functions not found: 4:3\n");
        return;
    }
    g_widen = target * 3.0f / 4.0f;
    runtime.replaceFunction(kCameraProjection, cameraProjection);
    runtime.replaceFunction(kPerspective, perspective);
    runtime.replaceFunction(kOrthographic, orthographic);
    runtime.replaceFunction(kDebugPrintf, debugPrintf);
    std::thread([] {
        static const bool log = std::getenv("PS2X_WSLOG") != nullptr;
        int was = -1;
        for (;;) // the window shows the target shape while marked in the last 250 ms
        {
            const bool shown = g_levelShown.load(std::memory_order_relaxed), movie = ben10VideoPlaying();
            if (log && was != int(shown) * 2 + int(movie))
            {
                was = int(shown) * 2 + int(movie);
                std::fprintf(stderr, "[widescreen] level shown %d, movie %d, menu map %d%c", int(shown), int(movie), int(g_menuMap), 10);
            }
            if (shown && !movie)
                ps2_widescreen::markWideFrame();
            std::this_thread::sleep_for(std::chrono::milliseconds(50));
        }
    }).detach();
    std::fprintf(stderr, "[widescreen] the 3-D at %.3f:1 (x%.3f wider)\n", target, g_widen);
}
