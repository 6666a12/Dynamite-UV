#pragma once

#include "matroska.h"
#include "pipe.h"

#include <condition_variable>
#include <memory>
#include <mutex>
#include <thread>

namespace uv {

inline constexpr std::size_t slots = 10; // Includes reserve/copy and the active pipe write, not just queued frames.

struct Slot {
    static constexpr std::size_t headroom = 40;
    static constexpr std::size_t audio_header_size = 7;
    // Uninitialized until the first use to avoid touching 79 MiB during Godot startup.
    std::array<std::uint8_t, headroom + rgba_bytes + audio_header_size + pcm_bytes> bytes;
    std::uint8_t *rgba() { return bytes.data() + headroom; }
    std::uint8_t *pcm() { return rgba() + rgba_bytes + audio_header_size; }
};

struct Finished {
    std::uint64_t frames;
    std::uint64_t audio_samples_per_channel;
    std::uint64_t first_source_frame;
    std::size_t max_slots_used;
};

struct QaDelay {
    unsigned per_frame_ms = 0;
    std::uint64_t pause_at = 0;
    unsigned pause_ms = 0;
};

// Exactly one caller thread supplies frames; the internal consumer thread owns pipe I/O.
class FrameStream {
public:
    using ReportError = void (*)(const char *, DWORD) noexcept;
    enum class Claim { discard, reserved, failed };

    explicit FrameStream(ReportError report_error) : report_error_(report_error) {}
    ~FrameStream();
    FrameStream(const FrameStream &) = delete;
    FrameStream &operator=(const FrameStream &) = delete;

    bool start(const wchar_t *name, QaDelay delay = {});
    bool arm();
    bool healthy() const;
    Claim claim(Slot *&slot);
    bool publish();
    bool finish(Finished &finished);
    void fail(const char *message, DWORD code = ERROR_SUCCESS);

private:
    void consume() noexcept;
    void send_frames();
    bool check(DWORD error, const char *message);

    LocalPipe pipe_;
    ReportError report_error_;
    std::unique_ptr<Slot[]> buffer_;
    std::thread consumer_;
    QaDelay delay_;
    mutable std::mutex mutex_;
    std::condition_variable writable_;
    std::condition_variable readable_;
    bool started_ = false;
    bool armed_ = false;
    bool producer_in_slot_ = false;
    bool ending_ = false;
    bool failed_ = false;
    bool eof_sent_ = false;
    std::size_t occupied_ = 0;
    std::size_t max_slots_used_ = 0;
    std::uint64_t callbacks_ = 0;
    std::uint64_t first_source_frame_ = 0;
    std::uint64_t published_ = 0;
    std::uint64_t consumed_ = 0;
};

} // namespace uv
