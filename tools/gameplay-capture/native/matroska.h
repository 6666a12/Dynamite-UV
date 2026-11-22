#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace uv {

inline constexpr int width = 1920;
inline constexpr int height = 1080;
inline constexpr int fps = 60;
inline constexpr int sample_rate = 48000;
inline constexpr int samples_per_frame = sample_rate / fps; // Stereo sample pairs.
inline constexpr std::size_t rgba_bytes = std::size_t(width) * height * 4;
inline constexpr std::size_t pcm_bytes = samples_per_frame * 2 * sizeof(std::int32_t);
static_assert(sample_rate % fps == 0);

using Bytes = std::vector<std::uint8_t>;

// One known-size cluster per frame, placed inside an unknown-size Segment. No seeking is needed.
struct Cluster {
    std::array<std::uint8_t, 40> prefix{};
    std::size_t prefix_size = 0;
    std::array<std::uint8_t, 7> audio_header{};
};

Bytes stream_header();
Cluster cluster_header(std::uint64_t frame);

} // namespace uv
