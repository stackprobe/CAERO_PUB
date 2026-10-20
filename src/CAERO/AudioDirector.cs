using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;

namespace Caero;

internal readonly record struct AudioMix(float Underwater, float Day, float Night)
{
    public static AudioMix At(float eyeY, float night = 0)
    {
        float submerged = DayNightCycle.Smooth((World.WaterLevel - eyeY) / 1.2f);
        return new(.3f * submerged,
            .7f * MathF.Sqrt(1 - night) * (1 - .8f * submerged),
            .7f * MathF.Sqrt(night) * (1 - .8f * submerged));
    }
    public float For(string name) => name switch
    { "underwater" => Underwater, "day" => Day, "night" => Night, _ => 0 };
}

// Only the underwater WAV needs pinned memory; day/night MP3s stream from bundled files.
// All raylib audio APIs are called on the main thread; no managed audio callback.
internal sealed class AudioDirector : IDisposable
{
    internal const float StepVolume = .103125f, JumpVolume = .24375f, LandingScale = .375f;
    private sealed class Loop(Music music, GCHandle memory)
    {
        public Music Music = music;
        public GCHandle Memory = memory;
        public float Volume;
    }
    private readonly Dictionary<string, Loop> loops = [];
    private readonly Dictionary<string, Sound> sounds = [];
    private readonly Dictionary<(long, long, int), float> lampHistory = [];
    private bool ownsDevice;
    private bool wasAudible;
    public bool Ready { get; private set; }
    public bool Muted { get; private set; }
    internal bool LoopsPlaying => Ready && loops.Count == 3 && loops.Values.All(l => Raylib.IsMusicStreamPlaying(l.Music));
    private Vector3? previous;
    private float stepDistance, strokeTime, lampCooldown, gain;
    private int stepIndex;
    public AudioDirector(bool enabled = true)
    {
        if (!enabled) return;
        try
        {
            Raylib.InitAudioDevice(); ownsDevice = true;
            if (!Raylib.IsAudioDeviceReady()) throw new InvalidOperationException("No audio output device.");
            foreach (var clip in AudioSynthesis.Create())
            {
                byte[] bytes = clip.Wav();
                if (clip.Loop)
                {
                    var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    Music music;
                    unsafe
                    {
                        fixed (byte* extension = ".wav\0"u8)
                            music = Raylib.LoadMusicStreamFromMemory((sbyte*)extension, (byte*)pin.AddrOfPinnedObject(), bytes.Length);
                    }
                    StartLoop(clip.Name, music, pin);
                }
                else
                {
                    var wave = Raylib.LoadWaveFromMemory(".wav", bytes);
                    Sound sound;
                    try { sound = Raylib.LoadSoundFromWave(wave); }
                    finally { Raylib.UnloadWave(wave); }
                    if (!Raylib.IsSoundValid(sound)) throw new InvalidOperationException("Cannot load " + clip.Name);
                    sounds.Add(clip.Name, sound);
                }
            }
            foreach (string name in BgmAssets.Names)
                StartLoop(name, Raylib.LoadMusicStream(BgmAssets.PathFor(name)));
            Ready = true;
            Console.WriteLine("AUDIO: day/night music, quiet underwater loop and nine effects ready; no wave or wind audio.");
        }
        catch (Exception error)
        {
            Console.WriteLine("AUDIO: disabled: " + error.Message);
            Dispose();
        }
    }
    private void StartLoop(string name, Music music, GCHandle memory = default)
    {
        if (!Raylib.IsMusicValid(music))
        { if (memory.IsAllocated) memory.Free(); throw new InvalidOperationException("Cannot load " + name); }
        music.Looping = true;
        loops.Add(name, new(music, memory));
        Raylib.SetMusicVolume(music, 0); Raylib.PlayMusicStream(music);
    }
    public void ToggleMute()
    {
        Muted = !Muted;
        if (Muted) foreach (var sound in sounds.Values) Raylib.StopSound(sound);
    }
    public void ResetListener(Vector3 position)
    { previous = position; stepDistance = strokeTime = 0; lampHistory.Clear(); }
    internal void ShiftOrigin(Vector3 delta) { if (previous.HasValue) previous += delta; }

    public void Update(float dt, World world, Explorer explorer, Renderer renderer)
    {
        if (!Ready) return;
        dt = Math.Clamp(dt, 0, .1f);
        bool audible = Raylib.IsWindowFocused() && !Muted;
        if (!audible && wasAudible) foreach (var sound in sounds.Values) Raylib.StopSound(sound);
        wasAudible = audible;
        UpdateMix(dt, AudioMix.At(explorer.Position.Y, renderer.Sky.Night), audible);
        Vector3 delta = previous.HasValue ? explorer.Position - previous.Value : Vector3.Zero;
        previous = explorer.Position;
        if (delta.LengthSquared() > 16) { ResetListener(explorer.Position); return; }
        if (audible)
        {
            if (explorer.Jumped) Play("jump", JumpVolume);
            if (explorer.SplashImpact > 0) Play("splash", Math.Clamp(explorer.SplashImpact / 24, .22f, 1));
            if (explorer.Surfaced) Play("surface", .65f);
            if (explorer.LandingImpact > 0) Play("land", Math.Clamp(explorer.LandingImpact / 20, .18f, .8f) * LandingScale);
        }
        if (explorer.Grounded && !explorer.Flying)
        {
            stepDistance += new Vector2(delta.X, delta.Z).Length();
            if (stepDistance >= 2.1f)
            {
                stepDistance %= 2.1f;
                if (audible) Play($"step{stepIndex++ % 3}", StepVolume, .94f + Random.Shared.NextSingle() * .12f);
            }
        }
        else stepDistance = 0;
        if (explorer.Swimming && !explorer.Flying && delta.Length() > dt * .7f)
        {
            strokeTime += dt;
            if (strokeTime > .85f) { strokeTime = 0; if (audible) Play("stroke", .4f); }
        }
        else strokeTime = 0;
        lampCooldown = Math.Max(0, lampCooldown - dt);
        var nearby = world.Lights.Where(l => Vector3.DistanceSquared(l.Position, explorer.Position) < 24 * 24).ToArray();
        var retained = nearby.Select(l => l.Key).ToHashSet();
        foreach (var key in lampHistory.Keys.Where(k => !retained.Contains(k)).ToArray()) lampHistory.Remove(key);
        foreach (var lamp in nearby)
        {
            float brightness = renderer.LanternBrightness(lamp);
            if (lampHistory.TryGetValue(lamp.Key, out float old) && old < .15f && brightness >= .15f
                && lampCooldown == 0 && audible && explorer.Position.Y > World.WaterLevel)
            {
                Vector3 offset = lamp.Position - explorer.Position;
                Vector3 right = new(MathF.Cos(explorer.Yaw), 0, MathF.Sin(explorer.Yaw));
                float pan = .5f - .45f * Vector3.Dot(Vector3.Normalize(offset + new Vector3(.0001f)), right);
                Play("lamp", .32f * (1 - offset.Length() / 24), 1, pan);
                lampCooldown = .35f;
            }
            lampHistory[lamp.Key] = brightness;
        }
    }
    internal void UpdateMix(float dt, AudioMix mix, bool audible = true)
    {
        if (!Ready) return;
        float blend = 1 - MathF.Exp(-Math.Max(0, dt) * 2);
        gain += ((audible && !Muted ? .75f : 0) - gain) * blend;
        foreach (var (name, loop) in loops)
        {
            Raylib.UpdateMusicStream(loop.Music);
            loop.Volume += (mix.For(name) - loop.Volume) * blend;
            Raylib.SetMusicVolume(loop.Music, loop.Volume * gain);
        }
    }
    internal void Play(string name, float volume = .6f, float pitch = 1, float pan = .5f)
    {
        if (!Ready || Muted || !sounds.TryGetValue(name, out var sound)) return;
        Raylib.SetSoundVolume(sound, volume * gain); Raylib.SetSoundPitch(sound, pitch);
        Raylib.SetSoundPan(sound, Math.Clamp(pan, 0, 1)); Raylib.PlaySound(sound);
    }
    internal void SeekBgmForCheck(float seconds)
    {
        foreach (string name in BgmAssets.Names) Raylib.SeekMusicStream(loops[name].Music, seconds);
    }
    internal bool BgmWrappedForCheck(float maximumSeconds) => BgmAssets.Names.All(name =>
        Raylib.GetMusicTimePlayed(loops[name].Music) >= 0 && Raylib.GetMusicTimePlayed(loops[name].Music) < maximumSeconds);
    public void Dispose()
    {
        Ready = false;
        foreach (var loop in loops.Values) { Raylib.UnloadMusicStream(loop.Music); if (loop.Memory.IsAllocated) loop.Memory.Free(); }
        loops.Clear();
        foreach (var sound in sounds.Values) Raylib.UnloadSound(sound);
        sounds.Clear();
        if (ownsDevice) { Raylib.CloseAudioDevice(); ownsDevice = false; }
    }
}
