using Godot;

namespace DynamiteUniverse.Ui;

/// <summary>A short reference tone through each actual audio bus, not a soundtrack or a level meter.
/// The settings model is the only owner of gain; do not apply its percentage a second time here.</summary>
internal sealed partial class SettingsChannelMonitor : AudioStreamPlayer
{
    private readonly Dictionary<string, AudioStreamWav> _tones = new();
    internal void Listen(string bus)
    {
        if (AudioServer.GetBusIndex(bus) < 0) return;
        if (!_tones.TryGetValue(bus,out var wav)) _tones[bus] = wav = Sample(bus);
        Stop(); Bus = bus; Stream = wav; Play();
    }
    private static AudioStreamWav Sample(string bus)
    {
        const int rate=24000; var seconds=bus=="Music" ? .53 : .19; var count=(int)(seconds*rate); var pcm=new byte[2*count];
        for (var i=0;i<count;i++)
        {
            var t=i/(double)rate;
            var tone=bus switch { "Music" => .17*(Math.Sin(Math.Tau*220*t)+.5*Math.Sin(Math.Tau*330*t)),
                "Hit" => .31*Math.Sin(Math.Tau*118*t+5*(1-Math.Exp(-80*t))), _ => .20*Math.Sin(Math.Tau*830*t) };
            var amp=Math.Min(t/.006,1)*Math.Min((seconds-t)/.025,1)*Math.Exp(-t*(bus=="Music"?4:18));
            var sample=(short)Math.Round(32767*tone*amp);pcm[2*i]=(byte)sample;pcm[2*i+1]=(byte)(sample>>8);
        }
        return new AudioStreamWav { Format=AudioStreamWav.FormatEnum.Format16Bits, MixRate=rate, Data=pcm };
    }
}
