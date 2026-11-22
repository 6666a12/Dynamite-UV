#include "stream.h"

#include <cstring>
#include <new>

namespace uv {

bool FrameStream::start(const wchar_t *name, QaDelay delay) {
    if (!check(pipe_.create(name), "Could not create the local video pipe")) return false;
    buffer_.reset(new (std::nothrow) Slot[slots]);
    if (!buffer_) {
        fail("Could not allocate the ten video frame slots", ERROR_NOT_ENOUGH_MEMORY);
        pipe_.close_writer();
        return false;
    }
    delay_ = delay;
    {
        const std::lock_guard lock(mutex_);
        started_ = true;
    }
    try {
        consumer_ = std::thread(&FrameStream::consume, this);
    } catch (...) {
        fail("Could not start the local video pipe thread");
        pipe_.close_writer();
        return false;
    }
    return true;
}

bool FrameStream::healthy() const {
    const std::lock_guard lock(mutex_);
    return started_ && !failed_ && !ending_;
}

bool FrameStream::arm() {
    const std::lock_guard lock(mutex_);
    if (!started_ || failed_ || ending_ || armed_) return false;
    armed_ = true;
    first_source_frame_ = callbacks_;
    return true;
}

FrameStream::Claim FrameStream::claim(Slot *&slot) {
    std::unique_lock lock(mutex_);
    if (!started_ || failed_ || ending_) return Claim::failed;
    ++callbacks_;
    if (!armed_) return Claim::discard;
    writable_.wait(lock, [this] { return occupied_ < slots || failed_ || ending_; });
    if (failed_ || ending_) return Claim::failed;
    if (producer_in_slot_) {
        lock.unlock();
        fail("Concurrent movie frame producers are not supported");
        return Claim::failed;
    }
    slot = &buffer_[published_ % slots];
    producer_in_slot_ = true;
    ++occupied_; // The writer cannot reclaim this slot until the whole cluster has been accepted.
    if (occupied_ > max_slots_used_) max_slots_used_ = occupied_;
    return Claim::reserved;
}

bool FrameStream::publish() {
    {
        const std::lock_guard lock(mutex_);
        if (!producer_in_slot_ || failed_ || ending_) return false;
        producer_in_slot_ = false;
        ++published_;
    }
    readable_.notify_one();
    return true;
}

bool FrameStream::finish(Finished &finished) {
    bool has_frames = false;
    {
        const std::lock_guard lock(mutex_);
        has_frames = armed_ && published_ != 0 && !producer_in_slot_;
        ending_ = true;
    }
    if (!has_frames) fail("No armed video frames were captured");
    readable_.notify_all();
    writable_.notify_all();
    if (consumer_.joinable()) consumer_.join();
    const std::lock_guard lock(mutex_);
    if (failed_ || !eof_sent_ || consumed_ != published_ || occupied_ != 0) return false;
    finished = {consumed_, consumed_ * samples_per_frame, first_source_frame_, max_slots_used_};
    return true;
}

void FrameStream::fail(const char *message, DWORD code) {
    {
        const std::lock_guard lock(mutex_);
        if (failed_) return;
        failed_ = true;
    }
    pipe_.cancel();
    readable_.notify_all();
    writable_.notify_all();
    report_error_(message, code);
}

bool FrameStream::check(DWORD error, const char *message) {
    if (error == ERROR_SUCCESS) return true;
    fail(message, error);
    return false;
}

void FrameStream::consume() noexcept {
    try {
        send_frames();
    } catch (...) {
        fail("Unexpected failure writing the Matroska stream");
    }
    pipe_.close_writer();
}

void FrameStream::send_frames() {
    if (!check(pipe_.connect(), "Could not connect the video pipe reader")) return;
    const auto header = stream_header();
    if (!check(pipe_.write(header.data(), header.size()), "Could not send the Matroska header")) return;
    for (;;) {
        std::uint64_t frame = 0;
        {
            std::unique_lock lock(mutex_);
            readable_.wait(lock, [this] { return consumed_ != published_ || ending_ || failed_; });
            if (failed_) return;
            if (consumed_ == published_ && ending_) break;
            frame = consumed_;
        }
        if ((delay_.per_frame_ms != 0 && !pipe_.wait_for_delay(delay_.per_frame_ms)) ||
            (delay_.pause_ms != 0 && frame == delay_.pause_at && !pipe_.wait_for_delay(delay_.pause_ms))) return;
        auto &slot = buffer_[frame % slots];
        const auto cluster = cluster_header(frame);
        auto *start = slot.rgba() - cluster.prefix_size;
        std::memcpy(start, cluster.prefix.data(), cluster.prefix_size);
        std::memcpy(slot.rgba() + rgba_bytes, cluster.audio_header.data(), cluster.audio_header.size());
        const auto size = cluster.prefix_size + rgba_bytes + cluster.audio_header.size() + pcm_bytes;
        if (!check(pipe_.write(start, size), "Could not write a video frame to the encoder")) return;
        {
            const std::lock_guard lock(mutex_);
            ++consumed_;
            --occupied_;
        }
        writable_.notify_one();
    }
    if (!check(pipe_.finish(), "Could not drain the video pipe to EOF")) return;
    const std::lock_guard lock(mutex_);
    eof_sent_ = true;
}

FrameStream::~FrameStream() {
    // Godot normally calls finish before SCENE deinit. An abandoned writer must not leave
    // an overlapped connect/write pending while its buffers and handles are destroyed.
    if (consumer_.joinable()) {
        {
            const std::lock_guard lock(mutex_);
            failed_ = true;
        }
        pipe_.cancel();
        readable_.notify_all();
        writable_.notify_all();
        consumer_.join();
    }
}

} // namespace uv
