using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mako;

/// A single entry in the package registry — what's discoverable, distinct
/// from PackageManager (what's installed/cached). See registry.json.
sealed record RegistryEntry
{
    [JsonPropertyName("name")]        public required string Name { get; init; }
    [JsonPropertyName("kind")]        public required string Kind { get; init; }        // "native" | "github"
    [JsonPropertyName("status")]      public required string Status { get; init; }      // "available" | "planned"
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("usage")]       public string? Usage { get; init; }               // e.g. "using Mako3D;"
    [JsonPropertyName("source")]      public string? Source { get; init; }              // e.g. "github:User/Repo"
    [JsonPropertyName("docs")]        public string? Docs { get; init; }                // e.g. "docs/mako3d.md"
    [JsonPropertyName("note")]        public string? Note { get; init; }                // extra context, mainly for "planned" entries
}

/// Package discovery — "what packages exist and what do they do," backed by
/// a small embedded registry.json. Separate from PackageManager, which
/// handles installing/caching a package you've already decided to use.
static class PackageRegistry
{
    private static IReadOnlyList<RegistryEntry>? _cache;

    public static IReadOnlyList<RegistryEntry> All()
    {
        if (_cache != null) return _cache;

        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("registry.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new MakoError("internal error: registry.json embedded resource not found");

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new MakoError("internal error: could not open registry.json resource");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        _cache = JsonSerializer.Deserialize<List<RegistryEntry>>(json)
            ?? throw new MakoError("internal error: registry.json parsed to null");
        return _cache;
    }

    /// Case-insensitive substring match against name or description.
    /// Empty/null query returns everything.
    public static IEnumerable<RegistryEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All();
        var q = query.Trim();
        return All().Where(e =>
            e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            e.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    /// Exact (case-insensitive) name lookup.
    public static RegistryEntry? Find(string name) =>
        All().FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
}
