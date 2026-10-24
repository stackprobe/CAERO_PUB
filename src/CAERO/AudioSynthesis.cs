namespace Caero;

// Original procedural effects and underwater ambience. Land music is bundled as MP3.
internal static class AudioSynthesis
{
    public const int Rate = 22050;
    internal sealed record Clip(string Name, bool Loop, float[] Samples)
    {
        public byte[] Wav()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            void Tag(string tag) => writer.Write(System.Text.Encoding.ASCII.GetBytes(tag));
            Tag("RIFF"); writer.Write(36 + Samples.Length * 2); Tag("WAVE"); Tag("fmt ");
            writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(Rate);
            writer.Write(Rate * 4); writer.Write((short)4); writer.Write((short)16);
            Tag("data"); writer.Write(Samples.Length * 2);
            foreach (float sample in Samples) writer.Write((short)Math.Round(Math.Clamp(sample, -1, 1) * 32767));
            return stream.ToArray();
        }
    }
    public static Clip[] Create()
    {
        var result = new List<Clip>();
        for (int i = 0; i < 3; i++) result.Add(Effect($"step{i}", .20, 10 + i, (t, n) =>
            .35 * n * Math.Exp(-t * 35) + .22 * Math.Sin(Math.Tau * (140 + i * 17) * t) * Math.Exp(-t * 50)));
        result.Add(Effect("jump", .28, 31, (t, n) => .14 * n * Math.Exp(-t * 18)
            + .18 * Math.Sin(Math.Tau * (240 * t + 190 * t * t)) * Math.Exp(-t * 15)));
        result.Add(Effect("land", .36, 32, (t, n) => (.36 * n + .3 * Math.Sin(Math.Tau * 83 * t)) * Math.Exp(-t * 22)));
        result.Add(Effect("bump", .32, 36, (t, n) =>
            .5 * Math.Sin(Math.Tau * (440 * t - 450 * t * t)) * Math.Exp(-t * 14)
            + .08 * n * Math.Exp(-t * 45)));
        result.Add(Effect("flight-start", .45, 38, (t, n) =>
            .4 * Math.Sin(Math.Tau * (260 * t + 650 * t * t)) * Math.Exp(-t * 8)
            + .06 * n * Math.Sin(Math.PI * t / .45) * Math.Exp(-t * 10)));
        result.Add(Effect("warp", 2.4, 39, (t, n) =>
        {
            double envelope = Math.Pow(Math.Sin(Math.PI * t / 2.4), 2) * Math.Exp(-t * .5);
            double pulse = .7 + .3 * Math.Sin(Math.Tau * (5 * t + 2 * t * t));
            return envelope * (pulse * (.32 * Math.Sin(Math.Tau * (130 * t + 180 * t * t))
                + .14 * Math.Sin(Math.Tau * (195 * t + 270 * t * t))) + .22 * n);
        }));
        result.Add(Effect("drink", 1.25, 37, (t, n) =>
        {
            double value = 0;
            for (int gulp = 0; gulp < 4; gulp++)
            {
                double onset = gulp switch { 0 => .03, 1 => .27, 2 => .49, _ => .69 };
                double u = t - onset;
                if (u is < 0 or > .22) continue;
                double envelope = Math.Sin(Math.PI * u / .22) * Math.Exp(-u * 9);
                value += envelope * (.48 * Math.Sin(Math.Tau * (290 * u - 420 * u * u))
                    + .12 * Math.Sin(Math.Tau * (630 * u - 700 * u * u)) + .16 * n);
            }
            double swallow = t - .95;
            if (swallow is >= 0 and < .25)
                value += Math.Sin(Math.PI * swallow / .25) * Math.Exp(-swallow * 10)
                    * (.28 * Math.Sin(Math.Tau * 115 * swallow) + .2 * n);
            return value;
        }));
        result.Add(Effect("stroke", .65, 34, (t, n) => .3 * n * Math.Sin(Math.PI * t / .65) * Math.Exp(-t * 4)));
        result.Add(Effect("lamp", 1.8, 35, (t, n) => .13 * (Math.Sin(Math.Tau * 880 * t)
            + .45 * Math.Sin(Math.Tau * 1320 * t)) * Math.Exp(-t * 4)));
        result.Add(Effect("splash", 1.6, 33, (t, n) => .9 * n * Math.Exp(-t * 3.2)
            + .12 * Math.Sin(Math.Tau * (75 * t + 8 * Math.Sin(t * 13))) * Math.Exp(-t * 5)));
        result.Add(Effect("surface", .75, 34, (t, n) => .45 * n * Math.Exp(-t * 6)
            + .065 * Math.Sin(Math.Tau * (190 * t + 120 * t * t)) * Math.Exp(-t * 9)));
        result.Add(UnderwaterLoop());
        return result.ToArray();
    }
    private static Clip Effect(string name, double duration, int seed, Func<double, double, double> sample)
    {
        int frames = (int)(duration * Rate);
        float[] pcm = new float[frames * 2];
        var random = new Random(seed); double low = 0;
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / Rate;
            low += ((random.NextDouble() * 2 - 1) - low) * .28;
            double edge = Math.Min(1, t / .005) * Math.Min(1, (duration - t) / .025);
            float value = (float)(sample(t, low) * edge);
            pcm[i * 2] = pcm[i * 2 + 1] = value;
        }
        return new(name, false, pcm);
    }
    private static Clip UnderwaterLoop()
    {
        const int seconds = 12, frames = seconds * Rate;
        float[] pcm = new float[frames * 2];
        var random = new Random(73);
        // Circular low-pass noise has matching values at the loop boundary.
        float[][] noise = new float[2][];
        for (int channel = 0; channel < 2; channel++)
        {
            float[] source = new float[frames];
            for (int i = 0; i < frames; i++) source[i] = (float)(random.NextDouble() * 2 - 1);
            float low = 0; const float alpha = .012f;
            for (int i = frames - 4096; i < frames; i++) low += (source[i] - low) * alpha;
            for (int i = 0; i < frames; i++) { low += (source[i] - low) * alpha; source[i] = low; }
            noise[channel] = source;
        }
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / Rate, phase = Math.Tau * t / seconds;
            for (int channel = 0; channel < 2; channel++)
            {
                double local = (t + channel * .3) % 3;
                double bubbleEnvelope = Math.Pow(Math.Sin(Math.PI * local / 3), 2) * Math.Exp(-local * 4);
                double value = noise[channel][i] * .65 * (.8 + .2 * Math.Sin(phase))
                    + .022 * Math.Sin(Math.Tau * 47 * t) + .012 * Math.Sin(Math.Tau * 71 * t)
                    + .1 * bubbleEnvelope * Math.Sin(Math.Tau * (140 * local + 90 * local * local));
                pcm[i * 2 + channel] = (float)value;
            }
        }
        return new("underwater", true, pcm);
    }
}
