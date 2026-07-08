namespace Mako;

/// Resolves asset paths for native packages (Audio, Mako2D textures, Mako3D models).
/// Paths are tried as-given first, then relative to the running script's directory —
/// so `Audio.load("assets/coin.wav")` works no matter where mko was launched from.
static class MakoAssets
{
    public static string BaseDir = "";

    public static string Resolve(string path)
    {
        if (File.Exists(path) || string.IsNullOrEmpty(BaseDir)) return path;
        var relative = Path.Combine(BaseDir, path);
        return File.Exists(relative) ? relative : path;
    }
}
