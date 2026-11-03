using DynamiteUniverse.Shared.Audio;

namespace CoreTests;

/// <summary>
/// FLAC 解码器回归：逐采样地面真值来自 <see cref="FlacTestEncoder"/>，
/// 边界/拒绝行为逐条断言。失败时必须报出，不能静默通过。
/// </summary>
internal static class FlacDecoderTests
{
    private const int Rate = 44_100;

    public static void Run(Action<bool, string> check)
    {
        RunRoundTrips(check);
        RunFormatBoundaries(check);
        RunMalformedInput(check);
        RunBudgets(check);
    }

    // ---------- 正常解码：所有子帧类型 × 声道分配 × 位深 ----------

    private static void RunRoundTrips(Action<bool, string> check)
    {
        foreach (var bits in new[] { 8, 16, 24 })
        {
            foreach (var channels in new[] { 1, 2 })
            {
                foreach (var kind in Enum.GetValues<FlacTestEncoder.SubframeKind>())
                {
                    var modes = channels == 2
                        ? Enum.GetValues<FlacTestEncoder.ChannelMode>()
                        : new[] { FlacTestEncoder.ChannelMode.Independent };
                    foreach (var mode in modes)
                    {
                        // constant 子帧的语义就是“整块一个值”，用真正的常量信号验证。
                        var samples = kind == FlacTestEncoder.SubframeKind.Constant
                            ? BuildConstantSignal(channels, bits, 5_000)
                            : BuildSignal(channels, bits, 5_000);
                        var encoder = new FlacTestEncoder
                        {
                            Channels = samples,
                            SampleRate = Rate,
                            BitsPerSample = bits,
                            BlockSize = 1024,
                            Kind = kind,
                            Mode = mode,
                            ResidualPartitionOrder = kind is FlacTestEncoder.SubframeKind.Fixed0
                                or FlacTestEncoder.SubframeKind.Verbatim
                                or FlacTestEncoder.SubframeKind.Constant
                                ? 0
                                : 2,
                        };
                        var label = $"{bits}-bit {channels}ch {kind}/{mode}";
                        var audio = ExpectDecode(check, encoder, label);
                        if (audio == null)
                            continue;
                        CheckShape(check, audio, channels, bits, encoder.FrameCount, label);
                        check(audio.Pcm16LittleEndian.AsSpan().SequenceEqual(
                                encoder.ExpectedPcm16()),
                            $"{label} samples round-trip exactly");
                    }
                }
            }
        }

        // mid/side 与 left-side 的 side 声道需要多 1 位，这里给出接近满幅的信号，
        // 只有正确处理符号扩展才不会溢出。
        foreach (var mode in new[]
                 {
                     FlacTestEncoder.ChannelMode.MidSide,
                     FlacTestEncoder.ChannelMode.LeftSide,
                     FlacTestEncoder.ChannelMode.RightSide,
                 })
        {
            var samples = AntiPhaseSignal(16, 3_000);
            var encoder = new FlacTestEncoder
            {
                Channels = samples,
                SampleRate = Rate,
                BitsPerSample = 16,
                BlockSize = 576,
                Kind = FlacTestEncoder.SubframeKind.Lpc,
                Mode = mode,
                LpcOrder = 4,
            };
            var label = $"24-bit-side full scale {mode}";
            var wide = new FlacTestEncoder
            {
                Channels = AntiPhaseSignal(24, 3_000),
                SampleRate = 48_000,
                BitsPerSample = 24,
                BlockSize = 576,
                Kind = FlacTestEncoder.SubframeKind.Lpc,
                Mode = mode,
                LpcOrder = 4,
            };
            var audio = ExpectDecode(check, encoder, label);
            if (audio != null)
                check(audio.Pcm16LittleEndian.AsSpan().SequenceEqual(encoder.ExpectedPcm16()),
                    $"{label} samples round-trip exactly");
            var wideLabel = $"24-bit full scale {mode}";
            var wideAudio = ExpectDecode(check, wide, wideLabel);
            if (wideAudio != null)
                check(wideAudio.Pcm16LittleEndian.AsSpan().SequenceEqual(wide.ExpectedPcm16()),
                    $"{wideLabel} samples round-trip exactly");
        }

        // 浪费位（wasted bits）路径。
        var wasted = BuildSignal(1, 16, 2_000);
        for (var i = 0; i < wasted[0].Length; i++)
            wasted[0][i] = (wasted[0][i] >> 6) << 6;
        var wastedEncoder = new FlacTestEncoder
        {
            Channels = wasted,
            SampleRate = Rate,
            BitsPerSample = 16,
            BlockSize = 512,
            Kind = FlacTestEncoder.SubframeKind.Fixed2,
            WastedBits = 6,
        };
        var wastedAudio = ExpectDecode(check, wastedEncoder, "wasted bits");
        if (wastedAudio != null)
            check(wastedAudio.Pcm16LittleEndian.AsSpan().SequenceEqual(wastedEncoder.ExpectedPcm16()),
                "wasted bits samples round-trip exactly");

        // Rice 转义码（每个分区直接写定宽原始残差）。
        var escapeEncoder = new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 2_000),
            SampleRate = Rate,
            BitsPerSample = 16,
            BlockSize = 1024,
            Kind = FlacTestEncoder.SubframeKind.Fixed2,
            Mode = FlacTestEncoder.ChannelMode.MidSide,
            Residual = FlacTestEncoder.ResidualKind.Escape,
        };
        var escapeAudio = ExpectDecode(check, escapeEncoder, "rice escape");
        if (escapeAudio != null)
            check(escapeAudio.Pcm16LittleEndian.AsSpan().SequenceEqual(escapeEncoder.ExpectedPcm16()),
                "rice escape samples round-trip exactly");

        // 变量块 + 非 2 的幂块大小 + 多种采样率编码。
        // 注意 STREAMINFO 的块大小下限是 16，更小的帧只能作为流末尾的短帧出现。
        foreach (var blockSize in new[] { 16, 17, 191, 192, 193, 255, 256, 257, 1152 })
        {
            var encoder = new FlacTestEncoder
            {
                Channels = BuildSignal(2, 16, 2_500),
                SampleRate = Rate,
                BitsPerSample = 16,
                BlockSize = blockSize,
                Kind = FlacTestEncoder.SubframeKind.Fixed2,
                UseVariableBlocking = true,
            };
            var label = $"block size {blockSize}";
            var audio = ExpectDecode(check, encoder, label);
            if (audio == null)
                continue;
            check(audio.FrameCount == 2_500, $"{label} decodes every frame");
            check(audio.Pcm16LittleEndian.AsSpan().SequenceEqual(encoder.ExpectedPcm16()),
                $"{label} samples round-trip exactly");
        }

        // 流末尾的短帧可以小于 STREAMINFO 下限，块大小编码 6 会带一个 8 位长度。
        foreach (var frames in new[] { 17, 18, 33, 1_025 })
        {
            var encoder = new FlacTestEncoder
            {
                Channels = BuildSignal(2, 16, frames),
                SampleRate = Rate,
                BitsPerSample = 16,
                BlockSize = 1_024,
                Kind = FlacTestEncoder.SubframeKind.Fixed2,
                Mode = FlacTestEncoder.ChannelMode.MidSide,
            };
            var label = $"{frames} samples with short trailing frame";
            var audio = ExpectDecode(check, encoder, label);
            if (audio != null)
                check(audio.Pcm16LittleEndian.AsSpan().SequenceEqual(encoder.ExpectedPcm16()),
                    $"{label} samples round-trip exactly");
        }

        foreach (var rate in new[] { 8_000, 16_000, 22_050, 24_000, 32_000, 44_100, 48_000, 96_000 })
        {
            var encoder = new FlacTestEncoder
            {
                Channels = BuildSignal(1, 16, 1_000),
                SampleRate = rate,
                BitsPerSample = 16,
                BlockSize = 512,
                Kind = FlacTestEncoder.SubframeKind.Fixed1,
            };
            var label = $"sample rate {rate}";
            var audio = ExpectDecode(check, encoder, label);
            if (audio != null)
            {
                check(audio.SampleRate == rate, $"{label} reported sample rate matches");
                check(Math.Abs(audio.DurationSeconds - 1000.0 / rate) < 1e-9,
                    $"{label} duration derived from frame count");
            }
        }
    }

    // ---------- 格式边界：明确拒绝而不是返回空音频 ----------

    private static void RunFormatBoundaries(Action<bool, string> check)
    {
        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            StreamInfoChannels = 3,
        }, "3 channels rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            StreamInfoBitsPerSample = 12,
        }, "12-bit depth rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            StreamInfoBitsPerSample = 32,
        }, "32-bit depth rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            SampleSizeCodeOverride = 3,
        }, "reserved sample size code rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            SampleRateCodeOverride = 15,
        }, "reserved sample rate code rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            BlockSizeCodeOverride = 0,
        }, "reserved block size code rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            ReservedSubframeType = 0x20 + 8, // fixed 区之外的保留值
        }, "reserved subframe type rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            ReservedLpcPrecision = 15,
        }, "reserved LPC coefficient precision rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            ReservedChannelAssignment = 11,
        }, "reserved channel assignment rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            OmitStreamInfo = true,
        }, "missing STREAMINFO rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(1, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            StreamInfoChannels = 2,
        }, "channel count mismatch with STREAMINFO rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(1, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            StreamInfoSampleRate = 32_000,
        }, "sample rate mismatch with STREAMINFO rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(1, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            StreamInfoTotalSamples = 128,
        }, "sample count overflow against STREAMINFO rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(1, 16, 256),
            SampleRate = Rate,
            BitsPerSample = 16,
            StreamInfoTotalSamples = 4_096,
        }, "truncated stream against STREAMINFO rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(1, 16, 256),
            SampleRate = 4_000,
            BitsPerSample = 16,
        }, "sample rate below 8 kHz rejected");

        ExpectReject(check, new FlacTestEncoder
        {
            Channels = BuildSignal(1, 16, 256),
            SampleRate = 384_000,
            BitsPerSample = 16,
            SampleRateCodeOverride = 14,
        }, "sample rate above 192 kHz rejected");

        // STREAMINFO 的 TotalSamples 允许为 0（未知）：此时必须按位流解到底，
        // 既不能因此报错，也不能返回空音频。
        var unknownTotal = new FlacTestEncoder
        {
            Channels = BuildSignal(2, 16, 3_000),
            SampleRate = Rate,
            BitsPerSample = 16,
            BlockSize = 512,
            Kind = FlacTestEncoder.SubframeKind.Fixed2,
            StreamInfoTotalSamples = 0,
        };
        var unknownAudio = ExpectDecode(check, unknownTotal, "unknown total sample count");
        if (unknownAudio != null)
        {
            check(unknownAudio.FrameCount == 3_000,
                "unknown total sample count decodes to the real frame count");
            check(unknownAudio.Pcm16LittleEndian.AsSpan().SequenceEqual(unknownTotal.ExpectedPcm16()),
                "unknown total sample count samples round-trip exactly");
        }

        // 只有元数据、没有任何音频帧：必须失败，不能返回空音频。
        ExpectReject(check, new FlacTestEncoder
        {
            Channels = new[] { Array.Empty<int>() },
            SampleRate = Rate,
            BitsPerSample = 16,
            StreamInfoTotalSamples = 0,
        }, "metadata-only file rejected instead of empty audio");
    }

    // ---------- 损坏数据 ----------

    private static void RunMalformedInput(Action<bool, string> check)
    {
        check(Throws(() => FlacDecoder.Decode(ReadOnlySpan<byte>.Empty)),
            "empty input rejected");
        check(Throws(() => FlacDecoder.Decode("nope"u8.ToArray())),
            "wrong magic rejected");

        var valid = BuildBaseline();

        // 帧头 CRC-8 错误
        var badHeaderCrc = BuildBaseline(corruptHeaderCrc: true);
        check(Throws(() => FlacDecoder.Decode(badHeaderCrc)), "bad frame header CRC-8 rejected");

        // 帧尾 CRC-16 错误
        var badFrameCrc = BuildBaseline(corruptFrameCrc: true);
        check(Throws(() => FlacDecoder.Decode(badFrameCrc)), "bad frame CRC-16 rejected");
        var lenient = FlacDecoderOptions.Default with { VerifyFrameCrc = false };
        check(!Throws(() => FlacDecoder.Decode(badFrameCrc, lenient)),
            "CRC verification can be disabled for damaged samples");

        // 每一处截断都必须抛异常，不能返回部分音频
        var allTruncationsRejected = true;
        for (var cut = 1; cut < valid.Length; cut++)
        {
            if (Throws(() => FlacDecoder.Decode(valid.AsSpan(0, cut))))
                continue;
            allTruncationsRejected = false;
            break;
        }
        check(allTruncationsRejected, "every truncation point is rejected");

        // 随机字节翻转不得抛非 FlacDecodeException 的异常
        var random = new Random(20261102);
        var unexpected = 0;
        for (var trial = 0; trial < 400; trial++)
        {
            var mutated = (byte[])valid.Clone();
            mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
            try
            {
                FlacDecoder.Decode(mutated);
            }
            catch (FlacDecodeException)
            {
            }
            catch (Exception)
            {
                unexpected++;
            }
        }
        check(unexpected == 0, "single-bit corruption only ever raises FlacDecodeException");

        // 前置 ID3v2 标签被跳过；标签长度越界被拒绝
        var withId3 = PrependId3(valid, payloadBytes: 12);
        var id3Audio = ExpectDecodeBytes(check, withId3, "ID3v2 prefixed file");
        check(id3Audio != null &&
              id3Audio.Pcm16LittleEndian.AsSpan().SequenceEqual(BuildBaselineReference()),
            "ID3v2 prefixed file decodes the same audio");
        var bogusId3 = PrependId3(valid, payloadBytes: 12);
        bogusId3[6] = 0x7F; // 声明一个巨大的标签
        bogusId3[7] = 0x7F;
        bogusId3[8] = 0x7F;
        bogusId3[9] = 0x7F;
        check(Throws(() => FlacDecoder.Decode(bogusId3)), "oversized ID3v2 length rejected");

        // 尾随垃圾字节不应被当作音频帧解释
        var trailing = BuildBaseline(extraTrailingBytes: 64);
        var trailingAudio = ExpectDecodeBytes(check, trailing, "trailing bytes tolerated");
        check(trailingAudio != null && trailingAudio.FrameCount == 2_048,
            "trailing bytes after the declared sample count are ignored");
    }

    // ---------- 预算 ----------

    private static void RunBudgets(Action<bool, string> check)
    {
        var payload = BuildBaseline();
        var strict = FlacDecoderOptions.Default with { MaxInputBytes = payload.Length - 1 };
        check(Throws(() => FlacDecoder.Decode(payload, strict)), "input byte budget enforced");

        var tinyPcm = FlacDecoderOptions.Default with { MaxPcmBytes = 3_000 };
        check(Throws(() => FlacDecoder.Decode(payload, tinyPcm)), "PCM byte budget enforced");

        var noFrames = FlacDecoderOptions.Default with { MaxFrameCount = 1 };
        check(Throws(() => FlacDecoder.Decode(payload, noFrames)), "frame count budget enforced");

        var monoOnly = FlacDecoderOptions.Default with { MaxChannels = 1 };
        check(Throws(() => FlacDecoder.Decode(payload, monoOnly)), "channel budget enforced");

        // 声明长度巨大的 STREAMINFO 不能逼出对应分配：预算先命中，异常类型可控。
        var huge = BuildBaseline();
        var before = GC.GetAllocatedBytesForCurrentThread();
        check(Throws(() => FlacDecoder.Decode(huge, FlacDecoderOptions.Default with
        {
            MaxPcmBytes = 1_024,
        })), "oversized PCM request fails before allocation");
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        check(allocated < 8 * 1024 * 1024,
            $"budget rejection does not allocate wildly (allocated {allocated} bytes)");
    }

    // ---------- helpers ----------

    private static FlacDecodedAudio? ExpectDecode(Action<bool, string> check,
        FlacTestEncoder encoder, string label)
    {
        try
        {
            return FlacDecoder.Decode(encoder.Encode());
        }
        catch (Exception ex)
        {
            check(false, $"{label} decodes without throwing ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static FlacDecodedAudio? ExpectDecodeBytes(Action<bool, string> check, byte[] bytes,
        string label)
    {
        try
        {
            return FlacDecoder.Decode(bytes);
        }
        catch (Exception ex)
        {
            check(false, $"{label} decodes without throwing ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    private static void ExpectReject(Action<bool, string> check, FlacTestEncoder encoder,
        string label)
    {
        byte[] bytes;
        try
        {
            bytes = encoder.Encode();
        }
        catch (Exception ex)
        {
            check(false, $"{label}: encoder failed ({ex.Message})");
            return;
        }
        check(Throws(() => FlacDecoder.Decode(bytes)), label);
    }

    private static void CheckShape(Action<bool, string> check, FlacDecodedAudio audio,
        int channels, int bits, long frames, string label)
    {
        check(audio.Channels == channels, $"{label} channel count");
        check(audio.SourceBitsPerSample == bits, $"{label} source bit depth reported");
        check(audio.FrameCount == frames, $"{label} frame count {audio.FrameCount} == {frames}");
        check(audio.Pcm16LittleEndian.Length == frames * channels * 2,
            $"{label} PCM length matches frames × channels × 2");
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (FlacDecodeException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static int[][] BuildSignal(int channels, int bits, int frames)
    {
        var max = (1L << (bits - 1)) - 1;
        var result = new int[channels][];
        for (var c = 0; c < channels; c++)
        {
            result[c] = new int[frames];
            for (var i = 0; i < frames; i++)
            {
                var a = Math.Sin(2.0 * Math.PI * (220.0 + 55 * c) * i / Rate);
                var b = Math.Sin(2.0 * Math.PI * (1_100.0 + 30 * c) * i / Rate) * 0.4;
                result[c][i] = (int)Math.Round((a + b) / 1.4 * max * 0.9);
            }
        }
        return result;
    }

    private static int[][] BuildConstantSignal(int channels, int bits, int frames)
    {
        var max = (1L << (bits - 1)) - 1;
        var result = new int[channels][];
        for (var c = 0; c < channels; c++)
        {
            result[c] = new int[frames];
            Array.Fill(result[c], (int)(max / 3 * (c == 0 ? 1 : -1)));
        }
        return result;
    }

    /// <summary>左右反相信号：side 声道接近满幅，能压出多 1 位宽度的符号问题。</summary>
    private static int[][] AntiPhaseSignal(int bits, int frames)
    {
        var max = (1L << (bits - 1)) - 1;
        var left = new int[frames];
        var right = new int[frames];
        for (var i = 0; i < frames; i++)
        {
            var v = (long)Math.Round(max * Math.Sin(2.0 * Math.PI * 330.0 * i / Rate));
            left[i] = (int)v;
            right[i] = (int)-v;
        }
        return new[] { left, right };
    }

    private static byte[] BuildBaseline(bool corruptHeaderCrc = false, bool corruptFrameCrc = false,
        int extraTrailingBytes = 0) => new FlacTestEncoder
    {
        Channels = BuildSignal(2, 16, 2_048),
        SampleRate = Rate,
        BitsPerSample = 16,
        BlockSize = 1024,
        Kind = FlacTestEncoder.SubframeKind.Fixed2,
        Mode = FlacTestEncoder.ChannelMode.MidSide,
        CorruptHeaderCrc = corruptHeaderCrc,
        CorruptFrameCrc = corruptFrameCrc,
        ExtraTrailingBytes = extraTrailingBytes,
    }.Encode();

    private static byte[] BuildBaselineReference() => new FlacTestEncoder
    {
        Channels = BuildSignal(2, 16, 2_048),
        SampleRate = Rate,
        BitsPerSample = 16,
        BlockSize = 1024,
        Kind = FlacTestEncoder.SubframeKind.Fixed2,
        Mode = FlacTestEncoder.ChannelMode.MidSide,
    }.ExpectedPcm16();

    private static byte[] PrependId3(byte[] flac, int payloadBytes)
    {
        var header = new byte[10 + payloadBytes];
        header[0] = (byte)'I';
        header[1] = (byte)'D';
        header[2] = (byte)'3';
        header[3] = 4;
        header[4] = 0;
        header[5] = 0;
        // 同步安全整数：每字节 7 位
        header[6] = (byte)((payloadBytes >> 21) & 0x7F);
        header[7] = (byte)((payloadBytes >> 14) & 0x7F);
        header[8] = (byte)((payloadBytes >> 7) & 0x7F);
        header[9] = (byte)(payloadBytes & 0x7F);
        var result = new byte[header.Length + flac.Length];
        header.CopyTo(result, 0);
        flac.CopyTo(result, header.Length);
        return result;
    }
}
