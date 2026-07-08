using Raylib_cs;

namespace Mako;

/// Audio — sounds and music for MAKO games.
///
///   using Mako2D;
///   using Audio;
///
///   main() {
///       Mako2D.init(800, 600, "Game");
///       beep = Audio.load("beep.wav");
///       song = Audio.music_load("track.ogg");
///       Audio.music_play(song);
///       while Mako2D.running() {
///           Audio.music_update(song);          # call every frame
///           if Inputs.key_pressed("SPACE") { Audio.play(beep); }
///           Mako2D.begin(); ... Mako2D.end();
///       }
///   }
///
static class MakoAudio
{
    /// A sound with a pool of alias voices so overlapping plays mix instead of
    /// restarting each other (raylib's PlaySound restarts the same handle).
    private sealed class Voice
    {
        public Sound Base;
        public Sound[] Pool = new Sound[VoicesPerSound];
        public int Next;
        public float Vol = 1f, Pitch = 1f, Pan = 0.5f;
    }

    private const int VoicesPerSound = 8;
    private static readonly List<Voice> _sounds = [];
    private static readonly List<Music> _music  = [];
    private static bool _deviceReady;

    internal static void EnsureDevice()
    {
        if (_deviceReady) return;
        Raylib.InitAudioDevice();
        _deviceReady = true;
    }

    private static object? Register(Sound s)
    {
        var v = new Voice { Base = s };
        for (int i = 0; i < VoicesPerSound; i++)
            v.Pool[i] = Raylib.LoadSoundAlias(s);
        _sounds.Add(v);
        return (object?)(double)(_sounds.Count - 1);
    }

    /// Pick a free voice (or the oldest) and apply current settings.
    private static Sound NextVoice(Voice v, float? pan = null, float? vol = null)
    {
        int pick = -1;
        for (int i = 0; i < VoicesPerSound; i++)
        {
            int idx = (v.Next + i) % VoicesPerSound;
            if (!Raylib.IsSoundPlaying(v.Pool[idx])) { pick = idx; break; }
        }
        if (pick < 0) pick = v.Next;                 // all busy → steal round-robin
        v.Next = (pick + 1) % VoicesPerSound;

        var snd = v.Pool[pick];
        Raylib.SetSoundVolume(snd, vol ?? v.Vol);
        Raylib.SetSoundPitch(snd, v.Pitch);
        Raylib.SetSoundPan(snd, pan ?? v.Pan);
        return snd;
    }

    // ── Sounds (short effects, fully loaded in memory) ────────────────────────

    /// load(path) → handle
    public static object? Load(List<object?> a)
    {
        EnsureDevice();
        string path = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        path = MakoAssets.Resolve(path);
        if (!File.Exists(path))
            throw new MakoError($"Audio.load(): file not found: '{path}'");
        return Register(Raylib.LoadSound(path));
    }

    public static object? Play(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        Raylib.PlaySound(NextVoice(v));
        return null;
    }

    public static object? Stop(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        foreach (var s in v.Pool)
            if (Raylib.IsSoundPlaying(s)) Raylib.StopSound(s);
        return null;
    }

    public static object? Pause(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        foreach (var s in v.Pool)
            if (Raylib.IsSoundPlaying(s)) Raylib.PauseSound(s);
        return null;
    }

    public static object? Resume(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        foreach (var s in v.Pool) Raylib.ResumeSound(s);
        return null;
    }

    public static object? Playing(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return (object?)false;
        foreach (var s in v.Pool)
            if (Raylib.IsSoundPlaying(s)) return (object?)true;
        return (object?)false;
    }

    /// volume(handle, 0.0–1.0)
    public static object? Volume(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        v.Vol = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 1f;
        return null;
    }

    /// pitch(handle, 1.0 = normal)
    public static object? Pitch(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        v.Pitch = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 1f;
        return null;
    }

    /// pan(handle, 0.0 = left, 0.5 = center, 1.0 = right)
    public static object? Pan(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        v.Pan = 1f - (a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 0.5f);
        return null;
    }

    // ── Music (streamed from disk, for longer tracks) ─────────────────────────

    /// music_load(path) → handle
    public static object? MusicLoad(List<object?> a)
    {
        EnsureDevice();
        string path = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        path = MakoAssets.Resolve(path);
        if (!File.Exists(path))
            throw new MakoError($"Audio.music_load(): file not found: '{path}'");
        _music.Add(Raylib.LoadMusicStream(path));
        return (object?)(double)(_music.Count - 1);
    }

    public static object? MusicPlay(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return null;
        Raylib.PlayMusicStream(m.Value);
        return null;
    }

    /// music_update(handle) — MUST be called every frame while music plays.
    public static object? MusicUpdate(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return null;
        Raylib.UpdateMusicStream(m.Value);
        return null;
    }

    public static object? MusicStop(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return null;
        Raylib.StopMusicStream(m.Value);
        return null;
    }

    public static object? MusicPause(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return null;
        Raylib.PauseMusicStream(m.Value);
        return null;
    }

    public static object? MusicResume(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return null;
        Raylib.ResumeMusicStream(m.Value);
        return null;
    }

    public static object? MusicPlaying(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return (object?)false;
        return (object?)(bool)Raylib.IsMusicStreamPlaying(m.Value);
    }

    public static object? MusicVolume(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return null;
        Raylib.SetMusicVolume(m.Value, a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 1f);
        return null;
    }

    /// music_seek(handle, seconds)
    public static object? MusicSeek(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return null;
        Raylib.SeekMusicStream(m.Value, a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 0f);
        return null;
    }

    /// music_length(handle) → seconds
    public static object? MusicLength(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return 0d;
        return (object?)(double)Raylib.GetMusicTimeLength(m.Value);
    }

    /// music_pos(handle) → seconds played so far
    public static object? MusicPos(List<object?> a)
    {
        var m = GetMusic(a); if (m is null) return 0d;
        return (object?)(double)Raylib.GetMusicTimePlayed(m.Value);
    }

    // ── Synth: generated tones ────────────────────────────────────────────────
    //
    // Waveform types: "sine" (soft), "square" (chippy), "saw" (buzzy),
    //                 "triangle" (mellow), "noise" (percussion / effects)

    private const int SampleRate = 44100;

    /// tone(type, freq_hz, duration_s) → sound handle
    public static object? Tone(List<object?> a)
    {
        EnsureDevice();
        string type = a.Count > 0 ? a[0]?.ToString() ?? "sine" : "sine";
        double freq = a.Count > 1 ? Convert.ToDouble(a[1]) : 440;
        double dur  = a.Count > 2 ? Convert.ToDouble(a[2]) : 0.25;
        return SoundFromSamples(Synth(type, freq, dur));
    }

    /// note(type, "C4", duration_s) → sound handle.  Names: C4, F#3, Bb5, A4=440.
    public static object? Note(List<object?> a)
    {
        EnsureDevice();
        string type = a.Count > 0 ? a[0]?.ToString() ?? "sine" : "sine";
        string name = a.Count > 1 ? a[1]?.ToString() ?? "A4" : "A4";
        double dur  = a.Count > 2 ? Convert.ToDouble(a[2]) : 0.25;
        return SoundFromSamples(Synth(type, NoteFreq(name), dur));
    }

    /// melody(type, notes_list, bpm) → sound handle — bakes a whole tune into one clip.
    /// Notes: "C4"  |  "C4:2" (2 beats)  |  "R" / "R:0.5" (rest)
    public static object? Melody(List<object?> a)
    {
        EnsureDevice();
        string type = a.Count > 0 ? a[0]?.ToString() ?? "sine" : "sine";
        if (a.Count < 2 || a[1] is not List<object?> notes)
            throw new MakoError("Audio.melody() expects (type, notes_list, bpm)");
        double bpm  = a.Count > 2 ? Convert.ToDouble(a[2]) : 120;
        double beat = 60.0 / bpm;

        var song = new List<float>();
        foreach (var item in notes)
        {
            var spec  = item?.ToString() ?? "R";
            var parts = spec.Split(':');
            double beats = parts.Length > 1 ? double.Parse(parts[1]) : 1;
            double dur   = beats * beat;
            if (parts[0].Equals("R", StringComparison.OrdinalIgnoreCase))
                song.AddRange(new float[(int)(SampleRate * dur)]);      // rest = silence
            else
                song.AddRange(Synth(type, NoteFreq(parts[0]), dur));
        }
        return SoundFromSamples(song.ToArray());
    }

    private static float[] Synth(string type, double freq, double dur)
    {
        int n = Math.Max(1, (int)(SampleRate * dur));
        var s = new float[n];
        var rng = Random.Shared;
        int attack  = Math.Min(n, SampleRate / 200);        // 5 ms fade-in
        int release = Math.Min(n, (int)(n * 0.25));          // fade-out tail

        for (int i = 0; i < n; i++)
        {
            double phase = freq * i / SampleRate;
            double frac  = phase - Math.Floor(phase);
            float v = type.ToLower() switch
            {
                "square"   => frac < 0.5 ? 0.7f : -0.7f,
                "saw"      => (float)(2 * frac - 1) * 0.7f,
                "triangle" => (float)(4 * Math.Abs(frac - 0.5) - 1) * 0.85f,
                "noise"    => (float)(rng.NextDouble() * 2 - 1) * 0.6f,
                _          => (float)Math.Sin(2 * Math.PI * phase) * 0.85f,   // sine
            };
            float env = 1f;
            if (i < attack)       env = i / (float)attack;
            if (i > n - release)  env = Math.Min(env, (n - i) / (float)release);
            s[i] = v * env;
        }
        return s;
    }

    /// "C4" / "F#3" / "Bb5" → frequency (A4 = 440)
    private static double NoteFreq(string name)
    {
        name = name.Trim();
        if (name.Length < 2) throw new MakoError($"Audio: bad note name '{name}'");
        int semis = char.ToUpper(name[0]) switch
        {
            'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11,
            _   => throw new MakoError($"Audio: bad note name '{name}'"),
        };
        int i = 1;
        if (name[i] == '#') { semis++; i++; }
        else if (name[i] == 'b') { semis--; i++; }
        if (!int.TryParse(name[i..], out int octave))
            throw new MakoError($"Audio: bad note name '{name}'");
        int midi = 12 * (octave + 1) + semis;
        return 440.0 * Math.Pow(2, (midi - 69) / 12.0);
    }

    private static object? SoundFromSamples(float[] samples)
    {
        var bytes = BuildWav(samples);
        var wave  = Raylib.LoadWaveFromMemory(".wav", bytes);
        var snd   = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);
        return Register(snd);
    }

    private static byte[] BuildWav(float[] samples, int rate = SampleRate)
    {
        int n = samples.Length;
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + n * 2); bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray()); bw.Write(16);
        bw.Write((short)1); bw.Write((short)1);                 // PCM, mono
        bw.Write(rate); bw.Write(rate * 2);
        bw.Write((short)2); bw.Write((short)16);                // 16-bit
        bw.Write("data"u8.ToArray()); bw.Write(n * 2);
        foreach (var s in samples)
            bw.Write((short)(Math.Clamp(s, -1f, 1f) * 32000));
        return ms.ToArray();
    }

    // ── Positional sound ──────────────────────────────────────────────────────

    /// play_at(handle, x, y) — 2D positional: pans and attenuates relative to
    /// the centre of the window (the "listener").
    public static object? PlayAt(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        double x = a.Count > 1 ? Convert.ToDouble(a[1]) : 0;
        double y = a.Count > 2 ? Convert.ToDouble(a[2]) : 0;

        double cx = Raylib.GetScreenWidth()  / 2.0;
        double cy = Raylib.GetScreenHeight() / 2.0;
        double half = Math.Max(cx, 1);

        float pan  = (float)Math.Clamp(0.5 + (x - cx) / (half * 2), 0.05, 0.95);
        double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        double maxd = Math.Sqrt(cx * cx + cy * cy);
        float vol  = (float)Math.Clamp(1.0 - 0.7 * (dist / Math.Max(maxd, 1)), 0.1, 1.0);

        // raylib pan: 1 = left, 0 = right
        Raylib.PlaySound(NextVoice(v, pan: 1f - pan, vol: vol * v.Vol));
        return null;
    }

    /// play_3d(handle, sx, sy, sz,  lx, ly, lz) — 3D positional: volume falls
    /// off with distance, panning from the listener-relative X offset.
    public static object? Play3D(List<object?> a)
    {
        var v = GetSound(a); if (v is null) return null;
        double sx = Cv(a, 1), sy = Cv(a, 2), sz = Cv(a, 3);
        double lx = Cv(a, 4), ly = Cv(a, 5), lz = Cv(a, 6);

        double dx = sx - lx, dy = sy - ly, dz = sz - lz;
        double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        float vol = (float)Math.Clamp(1.0 / (1.0 + 0.15 * dist), 0.02, 1.0);
        float pan = dist > 0.001
            ? (float)Math.Clamp(0.5 + 0.5 * (dx / dist), 0.05, 0.95)
            : 0.5f;

        Raylib.PlaySound(NextVoice(v, pan: 1f - pan, vol: vol * v.Vol));
        return null;
    }

    private static double Cv(List<object?> a, int i) =>
        a.Count > i ? Convert.ToDouble(a[i]) : 0;

    // ── Global ────────────────────────────────────────────────────────────────

    /// master_volume(0.0–1.0)
    public static object? MasterVolume(List<object?> a)
    {
        EnsureDevice();
        Raylib.SetMasterVolume(a.Count > 0 ? (float)Convert.ToDouble(a[0]) : 1f);
        return null;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public static void UnloadAll()
    {
        foreach (var v in _sounds)
        {
            foreach (var alias in v.Pool) Raylib.UnloadSoundAlias(alias);
            Raylib.UnloadSound(v.Base);
        }
        foreach (var m in _music) Raylib.UnloadMusicStream(m);
        _sounds.Clear();
        _music.Clear();
        if (_deviceReady) { Raylib.CloseAudioDevice(); _deviceReady = false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Voice? GetSound(List<object?> a)
    {
        int id = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : -1;
        return id >= 0 && id < _sounds.Count ? _sounds[id] : null;
    }

    private static Music? GetMusic(List<object?> a)
    {
        int id = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : -1;
        return id >= 0 && id < _music.Count ? _music[id] : null;
    }

    // ── Dispatch table ────────────────────────────────────────────────────────

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["load"]          = Load,
        ["play"]          = Play,
        ["stop"]          = Stop,
        ["pause"]         = Pause,
        ["resume"]        = Resume,
        ["playing"]       = Playing,
        ["volume"]        = Volume,
        ["pitch"]         = Pitch,
        ["pan"]           = Pan,
        ["music_load"]    = MusicLoad,
        ["music_play"]    = MusicPlay,
        ["music_update"]  = MusicUpdate,
        ["music_stop"]    = MusicStop,
        ["music_pause"]   = MusicPause,
        ["music_resume"]  = MusicResume,
        ["music_playing"] = MusicPlaying,
        ["music_volume"]  = MusicVolume,
        ["music_seek"]    = MusicSeek,
        ["music_length"]  = MusicLength,
        ["music_pos"]     = MusicPos,
        ["master_volume"] = MasterVolume,
        // Synth / music maker
        ["tone"]          = Tone,
        ["note"]          = Note,
        ["melody"]        = Melody,
        // Positional
        ["play_at"]       = PlayAt,
        ["play_3d"]       = Play3D,
    };
}
