// Ben 10: Protector of Earth (USA, SLUS-21661 v1.01): the game's Bink movies decoded on the PC by FFmpeg.
// Copied into PS2Recomp's runner folder by tools/sync_generated.py.
//
// CBinkPlayer (0x117960 per frame, 0x118508 Play, 0x118760 Stop) drives the RAD Bink library linked into the ELF; that
// library streams the file through RAD's IOP module, which our IOP emulation can't carry (the movie stays black and
// never ends). So the library's own entry points are replaced and everything above them stays the game's:
//   BinkOpen(lsn | 0x80000000, 0x800000)    -> a small Bink struct of ours in guest RAM (only the fields the game reads)
//   BinkWait(bink)                          -> sleeps until the current frame is due, returns 0 ("show it")
//   BinkDoFrame(bink)                       -> the current frame decoded
//   BinkCopyToBuffer(bink, dest, pitch, h, x, y, flags) -> that frame as PSMCT32 (RGBA, alpha 0x80) at dest
//   BinkNextFrame / BinkGoto / BinkPause / BinkClose
// The game then uploads the frame to the GS and draws it itself (0x117b80), ends it at the last frame (0x1178f0), lets
// the player skip it (its scripts call avpStop) and moves on. The file is read straight from the disc image
// (PS2X_CD_IMAGE) at the sector the game looked up; the sound (Bink audio) is resampled to 48 kHz and mixed into the
// game's own output (PS2AudioBackend::setPcmMixer). One line per movie at its end (frames shown, sound mixed).
// PS2X_VIDEOS=0 = off: the RAD library as it is (its movies stay black, for comparing only); PS2X_VIDEOLOG=1 = gotos.
#include "ps2_runtime.h"
#include "ps2_runtime_macros.h"
#include "runtime/ee_scheduler.h"
#include "runtime/ps2_audio.h"

extern "C"
{
#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/channel_layout.h>
#include <libswresample/swresample.h>
#include <libswscale/swscale.h>
}

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <filesystem>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

namespace
{
    constexpr uint32_t kBinkOpen = 0x1A7EA0u, kBinkWait = 0x1AA828u, kBinkDoFrame = 0x1A9800u, kBinkNextFrame = 0x1A9E10u,
                       kBinkCopyToBuffer = 0x1AADF0u, kBinkClose = 0x1AA668u, kBinkPause = 0x1AAA70u, kBinkGoto = 0x1AA260u;
    // Our Bink struct: kernel RAM below the runtime's RPC pools (Helpers/State.h), which the game never touches.
    // Fields the game reads: +0 width, +4 height, +8 frames, +0xc current frame (1-based), +0x1c read error.
    constexpr uint32_t kBinkStruct = 0x0000E000u;
    constexpr uint32_t kRate = 48000u;
    constexpr int kAhead = 8; // frames decoded ahead

    using Clock = std::chrono::steady_clock;
    bool g_log = false;

    struct FrameFree
    {
        void operator()(AVFrame *f) const { av_frame_free(&f); }
    };
    using FramePtr = std::unique_ptr<AVFrame, FrameFree>;

    // One movie: the .bik read from the disc image, decoded on its own thread.
    struct Player
    {
        std::FILE *disc = nullptr;
        uint64_t base = 0, size = 0, pos = 0; // the file's bytes inside the image
        uint32_t frameCount = 1;              // the Bink header's frame count
        AVFormatContext *format = nullptr;
        AVIOContext *io = nullptr;
        AVCodecContext *video = nullptr, *audio = nullptr;
        int videoStream = -1, audioStream = -1;
        SwsContext *scale = nullptr;
        SwrContext *resample = nullptr;

        std::thread thread;
        std::atomic<bool> stop{false};
        std::mutex lock;
        std::condition_variable wake;
        std::deque<FramePtr> frames;
        std::vector<int16_t> sound;
        size_t soundRead = 0;
        bool ended = false;
        std::atomic<uint64_t> mixed{0}, offered{0}; // stereo frames mixed into / offered by the game's output

        ~Player()
        {
            stop = true;
            wake.notify_all();
            if (thread.joinable())
                thread.join();
            sws_freeContext(scale);
            swr_free(&resample);
            avcodec_free_context(&video);
            avcodec_free_context(&audio);
            avformat_close_input(&format);
            if (io)
                av_freep(&io->buffer);
            avio_context_free(&io);
            if (disc)
                std::fclose(disc);
        }
    };

    int readPacket(void *opaque, uint8_t *buf, int want)
    {
        Player *p = static_cast<Player *>(opaque);
        const uint64_t left = p->size - std::min(p->pos, p->size);
        const size_t n = size_t(std::min<uint64_t>(left, uint64_t(want)));
        if (n == 0)
            return AVERROR_EOF;
        _fseeki64(p->disc, int64_t(p->base + p->pos), SEEK_SET);
        const size_t got = std::fread(buf, 1, n, p->disc);
        p->pos += got;
        return got ? int(got) : AVERROR_EOF;
    }

    int64_t seekPacket(void *opaque, int64_t offset, int whence)
    {
        Player *p = static_cast<Player *>(opaque);
        if (whence == AVSEEK_SIZE)
            return int64_t(p->size);
        whence &= ~AVSEEK_FORCE;
        const int64_t to = whence == SEEK_SET ? offset : whence == SEEK_CUR ? int64_t(p->pos) + offset : int64_t(p->size) + offset;
        if (to < 0)
            return -1;
        p->pos = uint64_t(to);
        return to;
    }

    bool openCodec(Player &p, int stream, AVCodecContext *&out)
    {
        const AVCodecParameters *par = p.format->streams[stream]->codecpar;
        const AVCodec *codec = avcodec_find_decoder(par->codec_id);
        if (!codec || !(out = avcodec_alloc_context3(codec)) || avcodec_parameters_to_context(out, par) < 0)
            return false;
        out->thread_count = 0;
        return avcodec_open2(out, codec, nullptr) >= 0;
    }

    void decode(Player *p)
    {
        AVPacket *packet = av_packet_alloc();
        AVFrame *frame = av_frame_alloc();
        auto takeFrames = [&]
        {
            while (avcodec_receive_frame(p->video, frame) >= 0)
            {
                FramePtr f(av_frame_alloc());
                av_frame_move_ref(f.get(), frame);
                std::unique_lock<std::mutex> lk(p->lock);
                p->wake.wait(lk, [&] { return p->stop.load() || p->frames.size() < size_t(kAhead) * 4; });
                if (p->stop)
                    return;
                p->frames.push_back(std::move(f));
                p->wake.notify_all();
            }
        };
        auto takeSound = [&]
        {
            while (avcodec_receive_frame(p->audio, frame) >= 0)
            {
                const int room = swr_get_out_samples(p->resample, frame->nb_samples);
                std::vector<int16_t> out(size_t(std::max(0, room)) * 2u);
                uint8_t *dst[1] = {reinterpret_cast<uint8_t *>(out.data())};
                const int got = swr_convert(p->resample, dst, room, const_cast<const uint8_t **>(frame->extended_data), frame->nb_samples);
                av_frame_unref(frame);
                if (got <= 0)
                    continue;
                std::lock_guard<std::mutex> lk(p->lock);
                if (p->soundRead > 0 && p->soundRead * 2 > p->sound.size())
                {
                    p->sound.erase(p->sound.begin(), p->sound.begin() + std::ptrdiff_t(p->soundRead));
                    p->soundRead = 0;
                }
                p->sound.insert(p->sound.end(), out.begin(), out.begin() + std::ptrdiff_t(got) * 2);
            }
        };
        while (!p->stop)
        {
            {
                std::unique_lock<std::mutex> lk(p->lock);
                p->wake.wait(lk, [&] {
                    return p->stop.load() || int(p->frames.size()) < kAhead ||
                           (p->audio && (p->sound.size() - p->soundRead) / 2u < kRate);
                });
            }
            if (p->stop)
                break;
            if (av_read_frame(p->format, packet) < 0)
            {
                avcodec_send_packet(p->video, nullptr);
                takeFrames();
                if (p->audio)
                {
                    avcodec_send_packet(p->audio, nullptr);
                    takeSound();
                }
                break;
            }
            if (packet->stream_index == p->videoStream && avcodec_send_packet(p->video, packet) >= 0)
                takeFrames();
            else if (p->audio && packet->stream_index == p->audioStream && avcodec_send_packet(p->audio, packet) >= 0)
                takeSound();
            av_packet_unref(packet);
        }
        av_packet_free(&packet);
        av_frame_free(&frame);
        std::lock_guard<std::mutex> lk(p->lock);
        p->ended = true;
        p->wake.notify_all();
    }

    std::unique_ptr<Player> openPlayer(uint32_t lsn, std::string &why)
    {
        const std::filesystem::path image = PS2Runtime::getIoPaths().cdImage;
        auto p = std::make_unique<Player>();
        p->disc = _wfopen(image.c_str(), L"rb");
        if (!p->disc)
        {
            why = "no disc image (PS2X_CD_IMAGE)";
            return nullptr;
        }
        p->base = uint64_t(lsn) * 2048u;
        uint8_t header[12] = {};
        _fseeki64(p->disc, int64_t(p->base), SEEK_SET);
        if (std::fread(header, 1, 12, p->disc) != 12 || std::memcmp(header, "BIK", 3) != 0)
        {
            why = "no Bink file at that sector";
            return nullptr;
        }
        uint32_t rest = 0;
        std::memcpy(&rest, header + 4, 4);
        p->size = uint64_t(rest) + 8u;
        std::memcpy(&p->frameCount, header + 8, 4);
        p->frameCount = std::max(1u, p->frameCount);
        constexpr int kBuffer = 64 * 1024;
        p->io = avio_alloc_context(static_cast<uint8_t *>(av_malloc(kBuffer)), kBuffer, 0, p.get(), readPacket, nullptr, seekPacket);
        p->format = avformat_alloc_context();
        p->format->pb = p->io;
        if (avformat_open_input(&p->format, nullptr, av_find_input_format("bink"), nullptr) < 0 ||
            avformat_find_stream_info(p->format, nullptr) < 0)
        {
            why = "FFmpeg can't read it";
            return nullptr;
        }
        p->videoStream = av_find_best_stream(p->format, AVMEDIA_TYPE_VIDEO, -1, -1, nullptr, 0);
        if (p->videoStream < 0 || !openCodec(*p, p->videoStream, p->video))
        {
            why = "no picture FFmpeg decodes";
            return nullptr;
        }
        p->audioStream = av_find_best_stream(p->format, AVMEDIA_TYPE_AUDIO, -1, p->videoStream, nullptr, 0);
        if (p->audioStream >= 0 && openCodec(*p, p->audioStream, p->audio))
        {
            AVChannelLayout stereo{};
            av_channel_layout_default(&stereo, 2);
            if (swr_alloc_set_opts2(&p->resample, &stereo, AV_SAMPLE_FMT_S16, int(kRate), &p->audio->ch_layout,
                                    p->audio->sample_fmt, p->audio->sample_rate, 0, nullptr) < 0 ||
                swr_init(p->resample) < 0)
                avcodec_free_context(&p->audio);
            av_channel_layout_uninit(&stereo);
        }
        else
            avcodec_free_context(&p->audio);
        p->thread = std::thread(decode, p.get());
        return p;
    }

    // ---------------------------------------------------------------- the movie playing (the game's thread)

    struct Movie
    {
        std::unique_ptr<Player> player;
        uint32_t lsn = 0, frames = 0, current = 0; // current = the frame the game is on (the struct's +0xc)
        uint32_t decoded = 0;                      // the frame in `picture`
        FramePtr picture;
        double fps = 30.0;
        Clock::time_point start;                   // frame n is due at start + (n - 1) / fps
        Clock::time_point pausedAt;
        bool paused = false, sound = true;
    } g_movie;
    std::mutex g_movieLock;                       // the mixer reads g_movie.player
    std::atomic<bool> g_mixing{false};

    void mix(int16_t *out, size_t count, size_t)
    {
        if (!g_mixing.load(std::memory_order_acquire))
            return;
        std::lock_guard<std::mutex> movieLock(g_movieLock);
        Player *p = g_movie.player.get();
        if (!p || !p->audio || g_movie.paused || !g_movie.sound)
            return;
        p->offered += count;
        {
            std::lock_guard<std::mutex> lk(p->lock);
            const size_t took = std::min(count, (p->sound.size() - p->soundRead) / 2u);
            const int16_t *in = p->sound.data() + p->soundRead;
            for (size_t i = 0; i < took * 2u; ++i)
                out[i] = int16_t(std::clamp(int(out[i]) + int(in[i]), -32768, 32767));
            p->soundRead += took * 2u;
            p->mixed += took;
        }
        p->wake.notify_all();
    }

    uint32_t u32(const uint8_t *rdram, uint32_t address)
    {
        uint32_t v;
        std::memcpy(&v, rdram + (address & 0x01FFFFFCu), 4);
        return v;
    }
    void put32(uint8_t *rdram, uint32_t address, uint32_t v) { std::memcpy(rdram + (address & 0x01FFFFFCu), &v, 4); }

    void done(R5900Context *ctx, uint32_t v)
    {
        setReturnU32(ctx, v);
        ctx->pc = GPR_U32(ctx, 31);
    }

    bool ours(R5900Context *ctx) { return GPR_U32(ctx, 4) == kBinkStruct && g_movie.player; }

    void setCurrent(uint8_t *rdram, uint32_t n)
    {
        g_movie.current = n;
        put32(rdram, kBinkStruct + 0xC, n);
    }

    void closeMovie()
    {
        g_mixing.store(false, std::memory_order_release);
        std::unique_ptr<Player> gone;
        {
            std::lock_guard<std::mutex> lk(g_movieLock);
            gone = std::move(g_movie.player);
        }
        g_movie.picture.reset();
    }

    void binkOpen(uint8_t *rdram, R5900Context *ctx, PS2Runtime *)
    {
        const uint32_t lsn = GPR_U32(ctx, 4) & 0x7FFFFFFFu;
        closeMovie();
        std::string why;
        std::unique_ptr<Player> p = openPlayer(lsn, why);
        if (!p)
        {
            std::fprintf(stderr, "[videos] sector 0x%x: %s; the movie is skipped\n", lsn, why.c_str());
            done(ctx, 0u);
            return;
        }
        const AVStream *vs = p->format->streams[p->videoStream];
        const AVRational rate = av_guess_frame_rate(p->format, const_cast<AVStream *>(vs), nullptr);
        Movie &m = g_movie;
        m.lsn = lsn;
        m.fps = rate.num > 0 && rate.den > 0 ? av_q2d(rate) : 30.0;
        m.frames = p->frameCount;
        m.decoded = 0;
        m.paused = false;
        m.sound = true;
        m.start = Clock::now();
        std::memset(rdram + kBinkStruct, 0, 0x100);
        put32(rdram, kBinkStruct + 0x0, uint32_t(p->video->width));
        put32(rdram, kBinkStruct + 0x4, uint32_t(p->video->height));
        put32(rdram, kBinkStruct + 0x8, m.frames);
        std::fprintf(stderr, "[videos] sector 0x%x: %d x %d, %u frames at %.3f a second, %s\n", lsn, p->video->width,
                     p->video->height, m.frames, m.fps, p->audio ? "with sound" : "no sound");
        {
            std::lock_guard<std::mutex> lk(g_movieLock);
            m.player = std::move(p);
        }
        setCurrent(rdram, 1u);
        g_mixing.store(true, std::memory_order_release);
        done(ctx, kBinkStruct);
    }

    // sleeps until the current frame is due (the game's loop spins on this on the PS2), then "show it"
    // Not due yet: the game's thread waits for the next VBlank and calls this again (a host sleep here would stall the
    // scheduler, and with it the IOP and the game's own sound, which then ran at half speed).
    void binkWait(uint8_t *, R5900Context *ctx, PS2Runtime *runtime)
    {
        if (!ours(ctx))
        {
            done(ctx, 0u);
            return;
        }
        Movie &m = g_movie;
        if (!m.paused)
        {
            const auto due = m.start + std::chrono::duration_cast<Clock::duration>(std::chrono::duration<double>((m.current - 1) / m.fps));
            const auto now = Clock::now();
            if (due - now > std::chrono::milliseconds(4))
            {
                ctx->pc = kBinkWait;
                EeScheduler &ee = runtime->eeScheduler();
                ee.waitVSync(ee.currentVSyncTick());
            }
            if (now - due > std::chrono::milliseconds(250))
                m.start += now - due; // the game fell behind (a load): carry on from here rather than race
        }
        done(ctx, 0u);
    }

    void binkDoFrame(uint8_t *rdram, R5900Context *ctx, PS2Runtime *)
    {
        if (!ours(ctx))
        {
            done(ctx, 0u);
            return;
        }
        Movie &m = g_movie;
        if (m.current < m.decoded)
        {
            // back in the movie (BinkGoto): from the start again, silent
            std::string why;
            std::unique_ptr<Player> again = openPlayer(m.lsn, why);
            if (again)
            {
                std::lock_guard<std::mutex> lk(g_movieLock);
                m.player = std::move(again);
                m.sound = false;
            }
            m.decoded = 0;
        }
        Player &p = *m.player;
        while (m.decoded < m.current)
        {
            std::unique_lock<std::mutex> lk(p.lock);
            p.wake.wait(lk, [&] { return !p.frames.empty() || p.ended; });
            if (p.frames.empty())
                break; // the file ends early: the last picture stays
            m.picture = std::move(p.frames.front());
            p.frames.pop_front();
            ++m.decoded;
            p.wake.notify_all();
        }
        (void)rdram;
        done(ctx, 0u);
    }

    void binkCopyToBuffer(uint8_t *rdram, R5900Context *ctx, PS2Runtime *)
    {
        if (!ours(ctx) || !g_movie.picture)
        {
            done(ctx, 0u);
            return;
        }
        const uint32_t dest = GPR_U32(ctx, 5) & 0x01FFFFFFu, pitch = GPR_U32(ctx, 6), height = GPR_U32(ctx, 7);
        const uint32_t x = GPR_U32(ctx, 8), y = GPR_U32(ctx, 9);
        Player &p = *g_movie.player;
        const AVFrame *f = g_movie.picture.get();
        const int w = f->width, h = std::min<int>(f->height, int(height));
        if (uint64_t(dest) + uint64_t(y + h) * pitch > 0x02000000u)
        {
            done(ctx, 0u);
            return;
        }
        p.scale = sws_getCachedContext(p.scale, f->width, f->height, AVPixelFormat(f->format), w, f->height, AV_PIX_FMT_RGBA,
                                       SWS_POINT, nullptr, nullptr, nullptr);
        uint8_t *dst[1] = {rdram + dest + size_t(y) * pitch + size_t(x) * 4u};
        const int stride[1] = {int(pitch)};
        sws_scale(p.scale, f->data, f->linesize, 0, h, dst, stride);
        for (int row = 0; row < h; ++row) // PS2 alpha: 0x80 = opaque
        {
            uint8_t *line = dst[0] + size_t(row) * pitch;
            for (int col = 0; col < w; ++col)
                line[col * 4 + 3] = 0x80u;
        }
        done(ctx, 0u);
    }

    void binkNextFrame(uint8_t *rdram, R5900Context *ctx, PS2Runtime *)
    {
        if (ours(ctx))
            setCurrent(rdram, g_movie.current >= g_movie.frames ? 1u : g_movie.current + 1u); // past the last: loops
        done(ctx, 0u);
    }

    void binkGoto(uint8_t *rdram, R5900Context *ctx, PS2Runtime *)
    {
        if (ours(ctx))
        {
            Movie &m = g_movie;
            const uint32_t n = std::clamp<uint32_t>(GPR_U32(ctx, 5), 1u, m.frames);
            if (g_log)
                std::fprintf(stderr, "[videos] goto frame %u\n", n);
            setCurrent(rdram, n);
            m.start = Clock::now() - std::chrono::duration_cast<Clock::duration>(std::chrono::duration<double>((n - 1) / m.fps));
        }
        done(ctx, 0u);
    }

    void binkPause(uint8_t *, R5900Context *ctx, PS2Runtime *)
    {
        if (ours(ctx))
        {
            Movie &m = g_movie;
            const bool on = GPR_U32(ctx, 5) != 0;
            if (on && !m.paused)
                m.pausedAt = Clock::now();
            else if (!on && m.paused)
                m.start += Clock::now() - m.pausedAt;
            m.paused = on;
        }
        done(ctx, 0u);
    }

    void binkClose(uint8_t *, R5900Context *ctx, PS2Runtime *)
    {
        if (ours(ctx))
        {
            std::fprintf(stderr, "[videos] sector 0x%x closed at frame %u of %u (%.1f s of sound mixed of %.1f s of output)\n",
                         g_movie.lsn, g_movie.current, g_movie.frames, double(g_movie.player->mixed.load()) / kRate,
                         double(g_movie.player->offered.load()) / kRate);
            closeMovie();
        }
        done(ctx, 0u);
    }
}

void installBen10Videos(PS2Runtime &runtime)
{
    if (const char *off = std::getenv("PS2X_VIDEOS"); off && off[0] == '0')
        return;
    g_log = std::getenv("PS2X_VIDEOLOG") != nullptr;
    const struct
    {
        uint32_t address;
        PS2Runtime::RecompiledFunction function;
    } hooks[] = {{kBinkOpen, binkOpen},   {kBinkWait, binkWait},   {kBinkDoFrame, binkDoFrame},
                 {kBinkNextFrame, binkNextFrame}, {kBinkCopyToBuffer, binkCopyToBuffer},
                 {kBinkClose, binkClose}, {kBinkPause, binkPause}, {kBinkGoto, binkGoto}};
    for (const auto &h : hooks)
        if (!runtime.lookupFunction(h.address))
        {
            std::fprintf(stderr, "[videos] Bink function 0x%x not found: the game's own player stays\n", h.address);
            return;
        }
    for (const auto &h : hooks)
        runtime.replaceFunction(h.address, h.function);
    PS2AudioBackend::setPcmMixer(mix);
    std::fprintf(stderr, "[videos] Bink movies through FFmpeg\n");
}

// A movie of ours is open (ben10_widescreen.cpp shows it at 4:3).
bool ben10VideoPlaying() { return g_mixing.load(std::memory_order_acquire); }
