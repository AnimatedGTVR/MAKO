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
    private static readonly List<Sound> _sounds = [];
    private static readonly List<Music> _music  = [];
    private static bool _deviceReady;

    internal static void EnsureDevice()
    {
        if (_deviceReady) return;
        Raylib.InitAudioDevice();
        _deviceReady = true;
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
        _sounds.Add(Raylib.LoadSound(path));
        return (object?)(double)(_sounds.Count - 1);
    }

    public static object? Play(List<object?> a)
    {
        var s = GetSound(a); if (s is null) return null;
        Raylib.PlaySound(s.Value);
        return null;
    }

    public static object? Stop(List<object?> a)
    {
        var s = GetSound(a); if (s is null) return null;
        Raylib.StopSound(s.Value);
        return null;
    }

    public static object? Pause(List<object?> a)
    {
        var s = GetSound(a); if (s is null) return null;
        Raylib.PauseSound(s.Value);
        return null;
    }

    public static object? Resume(List<object?> a)
    {
        var s = GetSound(a); if (s is null) return null;
        Raylib.ResumeSound(s.Value);
        return null;
    }

    public static object? Playing(List<object?> a)
    {
        var s = GetSound(a); if (s is null) return (object?)false;
        return (object?)(bool)Raylib.IsSoundPlaying(s.Value);
    }

    /// volume(handle, 0.0–1.0)
    public static object? Volume(List<object?> a)
    {
        var s = GetSound(a); if (s is null) return null;
        Raylib.SetSoundVolume(s.Value, a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 1f);
        return null;
    }

    /// pitch(handle, 1.0 = normal)
    public static object? Pitch(List<object?> a)
    {
        var s = GetSound(a); if (s is null) return null;
        Raylib.SetSoundPitch(s.Value, a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 1f);
        return null;
    }

    /// pan(handle, 0.0 = left, 0.5 = center, 1.0 = right)
    public static object? Pan(List<object?> a)
    {
        var s = GetSound(a); if (s is null) return null;
        Raylib.SetSoundPan(s.Value, a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 0.5f);
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
        foreach (var s in _sounds) Raylib.UnloadSound(s);
        foreach (var m in _music)  Raylib.UnloadMusicStream(m);
        _sounds.Clear();
        _music.Clear();
        if (_deviceReady) { Raylib.CloseAudioDevice(); _deviceReady = false; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Sound? GetSound(List<object?> a)
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
    };
}
