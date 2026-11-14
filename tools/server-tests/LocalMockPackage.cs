using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>Clean-room local fixture: schema example notes, generated quiet pulse audio, no artwork.</summary>
internal static class LocalMockPackage
{
    internal const string PackId = "local.mock.download-smoke";
    internal static byte[] Create(string repo)
    {
        var source = Path.Combine(repo, "schemas/chart-format-v2/examples/golden-pack");
        var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "meta.json")))!.AsObject();
        metadata["id"] = PackId;
        metadata["title"] = "[MOCK] Download Test — Synthetic Pulse";
        metadata["artist"] = "Local generated test audio (not a music release)";
        foreach (var chart in metadata["charts"]!.AsArray())
            chart!["charters"] = new JsonArray("Repository schema fixture");
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using (var output = zip.CreateEntry("meta.json").Open())
                JsonSerializer.Serialize(output, metadata);
            foreach (var file in new[] { "chart_hard.json", "chart_custom.json" })
            {
                using var output = zip.CreateEntry(file).Open();
                output.Write(File.ReadAllBytes(Path.Combine(source, file)));
            }
            using var audio = zip.CreateEntry("audio.wav").Open();
            using var writer = new BinaryWriter(audio);
            const int rate = 22050, seconds = 16, samples = rate * seconds;
            writer.Write("RIFF"u8); writer.Write(36 + samples * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
            writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
            writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(samples * 2);
            for (var i = 0; i < samples; i++)
            {
                var t = i / (double)rate;
                var phase = t % 0.4;
                var envelope = phase < 0.09 ? Math.Sin(Math.PI * phase / 0.09) : 0;
                var fade = Math.Min(1, Math.Min(t, seconds - t));
                writer.Write((short)(1600 * envelope * fade * Math.Sin(2 * Math.PI * 440 * t)));
            }
        }
        return bytes.ToArray();
    }
}
