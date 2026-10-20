using System.Numerics;
using Raylib_cs;

namespace Caero;

internal static class AudioChecks
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Audio test failed: " + message); }
    public static void Run(bool device)
    {
        var clips = AudioSynthesis.Create();
        string[] expected = ["jump", "lamp", "land", "splash", "step0", "step1", "step2", "stroke", "surface", "underwater"];
        Check(clips.Select(c => c.Name).Order().SequenceEqual(expected), "unchanged effects and underwater ambience, without synthesized land music");
        CheckBgmFiles();
        string directory = Path.Combine(SaveStore.DirectoryPath, "Audio");
        Directory.CreateDirectory(directory);
        foreach (var clip in clips)
        {
            Check(clip.Samples.All(float.IsFinite), clip.Name + " finite samples");
            Check(clip.Samples.Max(x => Math.Abs(x)) < .95f, clip.Name + " headroom");
            double rms = Math.Sqrt(clip.Samples.Average(x => (double)x * x));
            Check(rms > .002, clip.Name + " is audible");
            if (clip.Name == "underwater") Check(rms < .06, "underwater ambience stays quiet");
            if (clip.Loop)
                for (int c = 0; c < 2; c++)
                {
                    float largestStep = 0;
                    for (int i = c + 2; i < clip.Samples.Length; i += 2)
                        largestStep = Math.Max(largestStep, Math.Abs(clip.Samples[i] - clip.Samples[i - 2]));
                    Check(Math.Abs(clip.Samples[c] - clip.Samples[^(2 - c)]) <= largestStep + .001f, clip.Name + " loop seam matches ordinary waveform slope");
                }
            else
                Check(Math.Abs(clip.Samples[0]) < .001f && Math.Abs(clip.Samples[^1]) < .001f, clip.Name + " edge fades");
            byte[] wav = clip.Wav();
            Check(wav.Length == clip.Samples.Length * 2 + 44 && BitConverter.ToInt32(wav, 24) == AudioSynthesis.Rate, "PCM WAV header");
            File.WriteAllBytes(Path.Combine(directory, clip.Name + ".wav"), wav);
            Console.WriteLine($"AUDIO CLIP: {clip.Name}, {clip.Samples.Length / (2.0 * AudioSynthesis.Rate):F2}s, peak {clip.Samples.Max(x => Math.Abs(x)):F3}, RMS {rms:F3}");
        }
        Check(AudioMix.At(2).Underwater == 0 && AudioMix.At(240).Underwater == 0, "water loop is silent above water at all heights");
        Check(AudioMix.At(World.WaterLevel).Underwater == 0 && AudioMix.At(World.WaterLevel - 3).Underwater == .3f, "underwater mix with reduced maximum volume");
        var day = AudioMix.At(2, 0); var night = AudioMix.At(2, 1);
        Check(day.Day == .7f && day.Night == 0 && night.Day == 0 && night.Night == .7f, "original day and night music levels");
        Check(AudioMix.At(240, 0) == day, "height adds no wave or wind ambience");
        Check(AudioMix.At(World.WaterLevel - 3, 0).Day < day.Day, "music softens underwater");
        for (float y = World.WaterLevel - 2; y < World.WaterLevel + 2; y += .01f)
            Check(Math.Abs(AudioMix.At(y).Underwater - AudioMix.At(y + .01f).Underwater) < .01f, "smooth surface transition");
        using (var world = new World(upload: false))
        {
            var walker = new Explorer(); walker.GoTo(world.Views[0]);
            walker.Advance(.02f, world, Vector3.Zero, false, false, false, false);
            Check(walker.Grounded, "spawn standing on stone");
            walker.Advance(.02f, world, Vector3.Zero, true, true, false, false);
            Check(walker.Jumped && !walker.Grounded, "one jump event");
            int lands = 0;
            for (int i = 0; i < 100; i++)
            {
                walker.Advance(.02f, world, Vector3.Zero, false, false, false, false);
                Check(!walker.Jumped, "jump event resets"); if (walker.LandingImpact > 0) lands++;
            }
            Check(lands == 1, "one landing event");
            foreach (float dt in new[] { .01f, .05f, .1f })
            {
                var falling = new Explorer { Position = new(90, 50, 90) };
                int splashes = 0, surfaces = 0;
                for (int i = 0; i < (int)(30 / dt); i++)
                {
                    falling.Advance(dt, world, Vector3.Zero, false, false, false, false);
                    if (falling.SplashImpact > 0) { splashes++; Check(falling.SplashImpact > 20, "splash retains impact speed"); }
                    if (falling.Surfaced) surfaces++;
                }
                Check(splashes == 1 && surfaces == 1, "one splash and one surface sound per fall and passive float");
                for (int i = 0; i < (int)(3 / dt); i++) falling.Advance(dt, world, Vector3.Zero, false, false, true, false);
                surfaces = 0;
                for (int i = 0; i < (int)(10 / dt); i++)
                {
                    falling.Advance(dt, world, Vector3.Zero, true, false, false, false);
                    if (falling.Surfaced) surfaces++;
                }
                Check(surfaces == 1, "diving again rearms the surface sound");
                falling.GoTo(new("TELEPORT", new(90, World.WaterLevel - 3, 90), new(91, World.WaterLevel - 3, 90)));
                falling.Advance(dt, world, Vector3.Zero, false, false, false, false);
                falling.GoTo(world.Views[0]); falling.Advance(dt, world, Vector3.Zero, false, false, false, false);
                Check(!falling.Surfaced && falling.SplashImpact == 0, "teleport does not emit a water transition");
                falling.Flying = true; falling.Position = new(90, World.WaterLevel - 3, 90);
                falling.Advance(dt, world, Vector3.UnitY, false, false, false, false);
                Check(!falling.Surfaced && falling.SplashImpact == 0, "flying does not emit water effects");
            }
        }
        ExportPreview(clips, directory);
        if (device) Smoke();
        Console.WriteLine("AUDIO PASS: five-minute MP3 scores with silent ends, synthesized effects/underwater ambience, headroom, envelopes, loop seams, mixing and physics events; preview in " + directory);
    }
    private static unsafe void CheckBgmFiles()
    {
        foreach (string name in BgmAssets.Names)
        {
            string path = BgmAssets.PathFor(name);
            Check(File.Exists(path) && Path.GetExtension(path) == ".mp3", name + " bundled MP3");
            var wave = Raylib.LoadWave(path);
            Check(Raylib.IsWaveValid(wave), name + " MP3 decoder accepts file");
            float* samples = null;
            try
            {
                float duration = (float)wave.SampleCount / wave.SampleRate;
                Check(wave.Channels == 2 && wave.SampleRate == 44100 && Math.Abs(duration - BgmAssets.Duration) < .15f,
                    name + " five-minute stereo track");
                samples = Raylib.LoadWaveSamples(wave);
                Check(samples != null, name + " decoded PCM");
                int count = checked((int)(wave.SampleCount * wave.Channels));
                float peak = 0, edgePeak = 0; double sum = 0;
                int second = checked((int)(wave.SampleRate * wave.Channels));
                for (int i = 0; i < count; i++)
                {
                    float value = samples[i];
                    if (!float.IsFinite(value)) Check(false, name + " finite decoded sample");
                    peak = Math.Max(peak, Math.Abs(value)); sum += (double)value * value;
                    if (i < second || i >= count - second) edgePeak = Math.Max(edgePeak, Math.Abs(value));
                }
                Check(edgePeak < .00001f, name + " first/last second stays silent after MP3 decoding");
                Check(peak < .75f && Math.Sqrt(sum / count) > .02, name + " audible with headroom");
                Console.WriteLine($"MP3: {name}, {duration:F3}s, peak {peak:F3}, RMS {Math.Sqrt(sum / count):F3}, silent edge peak {edgePeak:G3}");
            }
            finally
            {
                if (samples != null) Raylib.UnloadWaveSamples(samples);
                Raylib.UnloadWave(wave);
            }
        }
    }
    private static void ExportPreview(AudioSynthesis.Clip[] clips, string directory)
    {
        const int seconds = 18;
        float[] output = new float[seconds * AudioSynthesis.Rate * 2];
        for (int frame = 0; frame < seconds * AudioSynthesis.Rate; frame++)
        {
            float t = (float)frame / AudioSynthesis.Rate;
            float depth = DayNightCycle.Smooth((t - 2) / 1.5f) * (1 - DayNightCycle.Smooth((t - 11) / 1.5f));
            var mix = AudioMix.At(World.WaterLevel - depth * 1.2f);
            foreach (var clip in clips.Where(c => c.Name == "underwater"))
                for (int c = 0; c < 2; c++) output[frame * 2 + c] += clip.Samples[(frame * 2 + c) % clip.Samples.Length] * mix.For(clip.Name) * .75f;
        }
        (string Name, float Time)[] cues = [("splash", 2), ("surface", 12.5f)];
        foreach (var cue in cues)
        {
            var clip = clips.First(c => c.Name == cue.Name); int start = (int)(cue.Time * AudioSynthesis.Rate) * 2;
            for (int i = 0; i < clip.Samples.Length && start + i < output.Length; i++) output[start + i] += clip.Samples[i] * .65f;
        }
        for (int i = 0; i < output.Length; i++) output[i] *= Math.Min(1, Math.Min(i, output.Length - 1 - i) / (AudioSynthesis.Rate * 2f));
        Check(output.Max(x => Math.Abs(x)) < .95f, "preview mix headroom");
        File.WriteAllBytes(Path.Combine(directory, "caero4-water-preview.wav"), new AudioSynthesis.Clip("preview", false, output).Wav());
    }
    private static void Smoke()
    {
        using var audio = new AudioDirector();
        Check(audio.Ready, "native audio device and all sound/music handles");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        string[] cues = ["step0", "step1", "step2", "jump", "land", "lamp", "stroke", "splash", "surface"];
        int cue = 0;
        bool collected = false, muted = false, unmuted = false, sought = false;
        while (timer.Elapsed.TotalSeconds < 34)
        {
            double t = timer.Elapsed.TotalSeconds;
            audio.UpdateMix(.01f, AudioMix.At(t > 8 && t < 24 ? World.WaterLevel - 4 : 2, DayNightCycle.Smooth((float)(t - 3) / 3)));
            if (cue < cues.Length && t > cue * 3 + 1)
            {
                string name = cues[cue++];
                float volume = name.StartsWith("step") ? AudioDirector.StepVolume : name == "jump" ? AudioDirector.JumpVolume
                    : name == "land" ? .8f * AudioDirector.LandingScale : .6f;
                audio.Play(name, volume);
            }
            if (t > 4 && !collected) { GC.Collect(); GC.WaitForPendingFinalizers(); collected = true; }
            if (t > 6 && !muted) { audio.ToggleMute(); muted = true; }
            if (t > 8 && !unmuted) { audio.ToggleMute(); unmuted = true; }
            if (t > 25 && !sought) { audio.SeekBgmForCheck(BgmAssets.Duration - 2); sought = true; }
            Thread.Sleep(10);
        }
        Check(audio.LoopsPlaying && !audio.Muted, "all streams survive loop wrap, GC and mute toggle");
        Check(sought && audio.BgmWrappedForCheck(15), "both five-minute MP3s restart after their ends");
        Console.WriteLine("AUDIO DEVICE PASS: day/night MP3s and unchanged underwater loop, all nine effects, actual MP3 end/restart after seeking, GC and mute.");
    }
}
