using System.Text.Json;

namespace Mako;

/// Discovers optional native physics adapters without making the MAKO runtime
/// depend on them.  An adapter owns its native bridge and describes itself with
/// backend.json inside ~/.local/share/mko/physics/<name>/.
static class PhysicsBackends
{
    public const int AbiVersion = 1;

    public sealed record Backend(string Id, string Name, string Dimension,
        bool BuiltIn, bool Installed, string Status, string? Root);

    private static readonly (string Id, string Name, string Dimension)[] Known =
    [
        ("mako", "MAKO Physics", "2d+3d"),
        ("jolt", "Jolt Physics", "3d"),
        ("physx", "NVIDIA PhysX", "3d"),
        ("bullet", "Bullet Physics", "3d"),
        ("box2d", "Box2D", "2d"),
    ];

    public static string InstallRoot => Environment.GetEnvironmentVariable("MAKO_PHYSICS_PATH")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mko", "physics");

    public static Backend Find(string requested)
    {
        string id = Normalize(requested);
        var known = Known.FirstOrDefault(x => x.Id == id);
        if (known == default)
            return new(id, requested, "unknown", false, false,
                "Unknown backend. Use mako, jolt, physx, or bullet for Physics3D.", null);
        if (id == "mako") return new(id, known.Name, known.Dimension, true, true, "Ready", null);
        if (id == "box2d") return new(id, known.Name, known.Dimension, false, false,
            "Box2D is a 2D engine; select it through Physics2D.", null);

        string root = Path.Combine(InstallRoot, id);
        string manifestPath = Path.Combine(root, "backend.json");
        if (!File.Exists(manifestPath))
            return new(id, known.Name, known.Dimension, false, false,
                $"Not installed. Expected {manifestPath}", root);
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(manifestPath));
            int abi = json.RootElement.TryGetProperty("abi", out var value) ? value.GetInt32() : 0;
            if (abi != AbiVersion)
                return new(id, known.Name, known.Dimension, false, false,
                    $"Adapter ABI {abi} does not match MAKO ABI {AbiVersion}.", root);
            string library = json.RootElement.TryGetProperty("library", out value) ? value.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(library) || !File.Exists(Path.Combine(root, library)))
                return new(id, known.Name, known.Dimension, false, false,
                    "backend.json does not point to an existing native bridge library.", root);
            return new(id, known.Name, known.Dimension, false, true, "Installed", root);
        }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
        {
            return new(id, known.Name, known.Dimension, false, false,
                $"Invalid backend.json: {ex.Message}", root);
        }
    }

    public static IReadOnlyList<Backend> All() => Known.Select(x => Find(x.Id)).ToList();

    public static string Normalize(string name) => name.Trim().ToLowerInvariant() switch
    {
        "native" or "builtin" or "mako3d" => "mako",
        "nvidia physx" or "physicsx" => "physx",
        "bullet3" => "bullet",
        _ => name.Trim().ToLowerInvariant(),
    };
}
