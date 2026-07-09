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
    [JsonPropertyName("versions")]    public List<RegistryVariant>? Versions { get; init; } // other forms/identities of this same package
}

/// Another version/identity of a package — e.g. MakoGUI is a variant of
/// MakoUI (same package, different name for a different use case). Not
/// separately searchable/browsable as its own top-level entry; discovered
/// via the parent entry's Versions tab/section.
sealed record RegistryVariant
{
    [JsonPropertyName("name")]        public required string Name { get; init; }
    [JsonPropertyName("description")] public required string Description { get; init; }
    [JsonPropertyName("usage")]       public string? Usage { get; init; }
    [JsonPropertyName("status")]      public string? Status { get; init; }  // defaults to the parent's status when absent
    [JsonPropertyName("note")]        public string? Note { get; init; }
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

    /// Case-insensitive substring match against name or description — the
    /// entry's own, or any of its versions/variants (e.g. searching
    /// "MakoGUI" surfaces the parent MakoUI entry; its Versions tab has
    /// the rest of the detail).
    public static IEnumerable<RegistryEntry> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return All();
        var q = query.Trim();
        return All().Where(e => Matches(e, q));
    }

    private static bool Matches(RegistryEntry e, string q) =>
        e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        e.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
        (e.Versions?.Any(v =>
            v.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            v.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) ?? false);

    /// Exact (case-insensitive) name lookup — matches an entry's own name,
    /// or the name of one of its versions/variants (resolves to the parent).
    public static RegistryEntry? Find(string name) =>
        All().FirstOrDefault(e =>
            string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase) ||
            (e.Versions?.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)) ?? false));
}
