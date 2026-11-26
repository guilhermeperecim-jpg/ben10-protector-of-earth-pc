// Ben 10: Protector of Earth (USA, SLUS-21661 v1.01): per-game runtime code, applied when the runtime loads this ELF.
// Copied into PS2Recomp's runner folder by tools/sync_generated.py.
#include "game_overrides.h"
#include "ps2_runtime.h"
#include "ps2_runtime_macros.h"

#include "runtime/ps2_host_profile.h"

#include <chrono>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <string>
#include <algorithm>
#include <map>
#include <thread>
#include <vector>

namespace
{
    // g++ 2.9x __pure_virtual (209 vtable slots point here): it calls __terminate, which ends the game.
    constexpr uint32_t kPureVirtual = 0x001A4C58u;
    PS2Runtime::RecompiledFunction g_pureVirtualOriginal = nullptr;

    uint32_t guestWord(const uint8_t *rdram, uint32_t address)
    {
        uint32_t value = 0u;
        std::memcpy(&value, rdram + (address & 0x01FFFFFCu), sizeof(value));
        return value;
    }

    // Says which virtual call landed on a pure virtual before the game terminates: the caller ($ra), the object
    // ($a0, after the g++ 2.9x this-adjustment) and the first slots of its vtable.
    void pureVirtualTrap(uint8_t *rdram, R5900Context *ctx, PS2Runtime *runtime)
    {
        const uint32_t ra = GPR_U32(ctx, 31);
        const uint32_t object = GPR_U32(ctx, 4);
        const uint32_t vtable = guestWord(rdram, object);
        std::fprintf(stderr, "[ben10] pure virtual call from ra=0x%x this=0x%x vtable=0x%x slots:", ra, object, vtable);
        for (uint32_t slot = 0u; slot < 0x60u; slot += 8u)
            std::fprintf(stderr, " +%x=%x", slot + 4u, guestWord(rdram, vtable + slot + 4u));
        std::fprintf(stderr, "\n");
        // The RIFF reader (FUN_001d7240) that hit it: the file the engine opened last (name built at 0x255798),
        // the chunk handler map of the parser (*(sp+0x80))->0x24 and the entry it picked (*(sp+0x20)).
        char name[96] = {};
        std::memcpy(name, rdram + 0x255798u, sizeof(name) - 1u);
        const uint32_t sp = GPR_U32(ctx, 29);
        const uint32_t parser = guestWord(rdram, sp + 0x80u);
        const uint32_t map = guestWord(rdram, parser + 0x24u);
        const uint32_t picked = guestWord(rdram, sp + 0x20u);
        const uint32_t base = guestWord(rdram, map + 0x18u);
        const uint32_t count = guestWord(rdram, map + 0x1cu);
        std::fprintf(stderr, "[ben10]   file=\"%s\" sp=0x%x parser=0x%x map=0x%x base=0x%x entries=%u picked=0x%x key=%08x\n",
                     name, sp, parser, map, base, count, picked, guestWord(rdram, picked));
        // newlib's heap: the game's sbrk break (0x229248) against EndOfHeap (the runtime's guest heap limit)
        std::fprintf(stderr, "[ben10]   sbrk break=0x%x EndOfHeap=0x%x\n", guestWord(rdram, 0x229248u),
                     runtime->guestHeapLimit());
        // the map object's first words (allocator, begin/end/capacity pointers)
        std::fprintf(stderr, "[ben10]   map words:");
        for (uint32_t off = 0u; off < 0x28u; off += 4u)
            std::fprintf(stderr, " +%x=%x", off, guestWord(rdram, map + off));
        std::fprintf(stderr, "\n");
        for (uint32_t i = 0u; i < count && i < 24u; ++i)
        {
            const uint32_t key = guestWord(rdram, base + i * 8u);
            const uint32_t handler = guestWord(rdram, base + i * 8u + 4u);
            std::fprintf(stderr, "[ben10]   [%u] key=%08x '%.4s' handler=0x%x vtable=0x%x\n", i, key,
                         reinterpret_cast<const char *>(rdram + ((base + i * 8u) & 0x01FFFFFFu)), handler,
                         guestWord(rdram, handler));
        }
        std::fflush(stderr);
        g_pureVirtualOriginal(rdram, ctx, runtime);
    }

    // PS2X_FMTLOG=1: the script's number-to-text path (ass_DisplayTextF: fptodp, floor, dptofp, fmodf, sprintf "%03g"),
    // each call's input and result, to see which step makes the options screen print "96.501005"
    constexpr uint32_t kFloor = 0x001F2D78u, kSprintf = 0x001F9DE8u;
    PS2Runtime::RecompiledFunction g_floorOriginal = nullptr, g_sprintfOriginal = nullptr;

    double asDouble(uint64_t bits)
    {
        double value = 0.0;
        std::memcpy(&value, &bits, sizeof(value));
        return value;
    }

    void floorLogged(uint8_t *rdram, R5900Context *ctx, PS2Runtime *runtime)
    {
        const uint64_t in = GPR_U64(ctx, 4);
        g_floorOriginal(rdram, ctx, runtime);
        std::fprintf(stderr, "[fmt] floor(%.9g) = %.9g (bits %016llx -> %016llx)\n", asDouble(in), asDouble(GPR_U64(ctx, 2)),
                     static_cast<unsigned long long>(in), static_cast<unsigned long long>(GPR_U64(ctx, 2)));
    }

    void sprintfLogged(uint8_t *rdram, R5900Context *ctx, PS2Runtime *runtime)
    {
        const uint32_t buffer = GPR_U32(ctx, 4), format = GPR_U32(ctx, 5);
        const uint64_t arg = GPR_U64(ctx, 6);
        g_sprintfOriginal(rdram, ctx, runtime);
        char fmt[32] = {}, text[64] = {};
        std::memcpy(fmt, rdram + (format & 0x01FFFFFFu), sizeof(fmt) - 1u);
        std::memcpy(text, rdram + (buffer & 0x01FFFFFFu), sizeof(text) - 1u);
        fmt[std::strcspn(fmt, "\n")] = 0;
        std::fprintf(stderr, "[fmt] sprintf(\"%s\", a2=%016llx = %.9g) -> \"%s\"\n", fmt, static_cast<unsigned long long>(arg),
                     asDouble(arg), text);
    }

    // VENDETTA_HOSTPROF=1 (adapted from def-jam-vendetta-recomp's override): every 5 s the game's frames/s (GS flips:
    // presentations whose display registers changed), what the game and GS threads were busy with (sampled 1000x/s,
    // runtime/ps2_host_profile.h) and the work counters; =2 also lists the busiest host functions per thread.
    void startHostProfile(PS2Runtime &runtime)
    {
        const char *mode = std::getenv("VENDETTA_HOSTPROF");
        if (!mode)
            return;
        const bool samplePcs = mode[0] == '2';
        PS2Runtime *rt = &runtime;
        std::thread([samplePcs, rt]()
                    {
                        using namespace ps2_host_profile;
                        uint32_t counts[TrackCount][Count] = {};
                        uint32_t samples = 0;
                        auto next = std::chrono::steady_clock::now();
                        auto report = next + std::chrono::seconds(5);
                        // "[stall] N ms without a vblank" (the emulation behind real time: a hitch on screen and in
                        // the sound): what the threads did meanwhile and the game thread's rough call stacks
                        constexpr size_t kMaxStacks = 64, kStackDepth = 24;
                        static uint64_t stacks[kMaxStacks][kStackDepth];
                        size_t stackLens[kMaxStacks] = {}, stackSamples = 0;
                        uint32_t stallPhases[TrackCount][Count] = {};
                        uint64_t stallTick = 0;
                        uint32_t stallMs = 0;
                        for (;;)
                        {
                            next += std::chrono::milliseconds(1);
                            std::this_thread::sleep_until(next);
                            uint8_t phases[TrackCount];
                            for (uint32_t t = 0; t < TrackCount; ++t)
                                if ((phases[t] = g_phases[t].load(std::memory_order_relaxed)) < Count)
                                    ++counts[t][phases[t]];
                            ++samples;
                            const uint64_t vblankNow = rt->memory().gs().vsyncTick.load(std::memory_order_acquire);
                            if (vblankNow != stallTick)
                            {
                                if (stallMs >= 60u)
                                {
                                    std::string text = "[stall] " + std::to_string(stallMs) + " ms without a vblank (after tick " +
                                                       std::to_string(stallTick) + ")";
                                    static const char *const trackNames[TrackCount] = {"game thread", "GS thread", "chain worker"};
                                    for (uint32_t t = 0; t < TrackCount; ++t)
                                    {
                                        uint32_t total = 0;
                                        for (uint8_t i = 0; i < Count; ++i)
                                            total += stallPhases[t][i];
                                        text += std::string(t == 0 ? ": " : "; ") + trackNames[t];
                                        for (uint8_t i = 0; i < Count && total; ++i)
                                            if (stallPhases[t][i] != 0u)
                                                text += std::string(" ") + name(i) + "=" + std::to_string((stallPhases[t][i] * 100u + total / 2u) / total) + "%";
                                    }
                                    std::map<std::string, uint32_t> seen;
                                    for (size_t i = 0; i < stackSamples; ++i)
                                        ++seen[describeStack(stacks[i], stackLens[i])];
                                    std::vector<std::pair<uint32_t, std::string>> ranked;
                                    for (auto &[stack, count] : seen)
                                        ranked.emplace_back(count, stack);
                                    std::sort(ranked.rbegin(), ranked.rend());
                                    for (size_t i = 0; i < ranked.size() && i < 3u; ++i)
                                        text += std::string(1, char(10)) + "[stall]   " + std::to_string(ranked[i].first) + "/" +
                                                std::to_string(stackSamples) + " game thread: " + ranked[i].second;
                                    std::fprintf(stderr, "%s%c", text.c_str(), 10);
                                }
                                stallTick = vblankNow;
                                stallMs = 0;
                                stackSamples = 0;
                                std::memset(stallPhases, 0, sizeof(stallPhases));
                            }
                            else if (++stallMs >= 40u)
                            {
                                for (uint32_t t = 0; t < TrackCount; ++t)
                                    if (phases[t] < Count)
                                        ++stallPhases[t][phases[t]];
                                if (stallMs % 5u == 0u && stackSamples < kMaxStacks)
                                {
                                    stackLens[stackSamples] = stackScan(GameTrack, stacks[stackSamples], kStackDepth);
                                    ++stackSamples;
                                }
                            }
                            if (samplePcs)
                            {
                                const Track track = static_cast<Track>(samples % TrackCount);
                                samplePc(track, phases[track]);
                            }
                            if (std::chrono::steady_clock::now() < report)
                                continue;
                            report += std::chrono::seconds(5);
                            static const char *const labels[TrackCount] = {"[hostprof]", "[hostprof-gs]", "[hostprof-chain]"};
                            for (uint32_t t = 0; t < TrackCount; ++t)
                            {
                                if (t != GameTrack && counts[t][Idle] == samples)
                                    continue;
                                std::string line = labels[t];
                                for (uint8_t i = 0; i < Count; ++i)
                                    if (counts[t][i] != 0u)
                                        line += std::string(" ") + name(i) + "=" + std::to_string((counts[t][i] * 100u + samples / 2u) / samples) + "%";
                                if (t == GameTrack)
                                    for (uint8_t i = 0; i < CounterCount; ++i)
                                    {
                                        const uint64_t value = g_counters[i].exchange(0u, std::memory_order_relaxed);
                                        if (value != 0u)
                                            line += std::string(" ") + counterName(i) + "/s=" + std::to_string((value + 2u) / 5u);
                                    }
                                std::fprintf(stderr, "%s\n", line.c_str());
                            }
                            if (samplePcs)
                                std::fprintf(stderr, "%s", pcReport(30u, 12u).c_str());
                            samples = 0;
                            std::memset(counts, 0, sizeof(counts));
                        }
                    })
            .detach();
    }

    void applyBen10(PS2Runtime &runtime)
    {
        startHostProfile(runtime);
        if (std::getenv("PS2X_FMTLOG"))
        {
            g_floorOriginal = runtime.lookupFunction(kFloor);
            g_sprintfOriginal = runtime.lookupFunction(kSprintf);
            if (g_floorOriginal && g_sprintfOriginal)
            {
                runtime.replaceFunction(kFloor, floorLogged);
                runtime.replaceFunction(kSprintf, sprintfLogged);
            }
        }
        // The disc image (PS2X_CD_IMAGE, set by tools\run.ps1): EE sceCdSearchFile/sceCdRead and the IOP's cdvdman
        // (used by CDARB.IRX to stream GAME.WAD) then agree on the disc's real sector numbers.
        if (const char *image = std::getenv("PS2X_CD_IMAGE"); image != nullptr && image[0] != '\0')
        {
            PS2Runtime::IoPaths paths = PS2Runtime::getIoPaths();
            paths.cdImage = image;
            PS2Runtime::setIoPaths(paths);
            std::fprintf(stderr, "[ben10] disc image: %s\n", image);
        }
        else
            std::fprintf(stderr, "[ben10] PS2X_CD_IMAGE not set: CDARB cannot read GAME.WAD\n");
        // PS2X_MC_ROOT: another memory card folder (tools\run.ps1 uses a scratch card, so its scripted runs never
        // touch the player's save in game\mc0).
        if (const char *mc = std::getenv("PS2X_MC_ROOT"); mc != nullptr && mc[0] != '\0')
        {
            PS2Runtime::IoPaths paths = PS2Runtime::getIoPaths();
            paths.mcRoot = mc;
            PS2Runtime::setIoPaths(paths);
            std::fprintf(stderr, "[ben10] memory card: %s\n", mc);
        }

        g_pureVirtualOriginal = runtime.lookupFunction(kPureVirtual);
        if (g_pureVirtualOriginal != nullptr)
            runtime.replaceFunction(kPureVirtual, pureVirtualTrap);
        std::fprintf(stderr, "[ben10] overrides applied (pure virtual trap %s)\n", g_pureVirtualOriginal ? "on" : "off");
    }
}

PS2_REGISTER_GAME_OVERRIDE("Ben 10: Protector of Earth (USA)", "SLUS_216.61", 0x100008u, 0u, applyBen10)
