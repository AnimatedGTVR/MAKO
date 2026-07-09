using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mako;

/// Live lookup for a GitHub-hosted package's metadata, without cloning the
/// repo — reads a small `mako.json` manifest at the repo root via GitHub's
/// Contents API. This is what backs `mko search github:User/Repo` and
/// `mko info github:User/Repo`: a preview of what a package is before
/// deciding to `mko get` it. Separate from PackageManager (which actually
/// clones/installs) and PackageRegistry (the local, curated registry.json).
static class GithubPackageLookup
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private sealed record Manifest
    {
        [JsonPropertyName("name")]        public string? Name { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("version")]     public string? Version { get; init; }
        [JsonPropertyName("usage")]       public string? Usage { get; init; }
    }

    private sealed record ContentsResponse
    {
        [JsonPropertyName("content")]  public string? Content { get; init; }
        [JsonPropertyName("encoding")] public string? Encoding { get; init; }
    }

    /// Parses "github:User/Repo" (or a bare "User/Repo") into (owner, repo).
    public static bool TryParseSource(string source, out string owner, out string repo)
    {
        owner = repo = "";
        var rest = source.StartsWith("github:", StringComparison.OrdinalIgnoreCase)
            ? source["github:".Length..]
            : source;
        var parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return false;
        owner = parts[0];
        repo = parts[1];
        return true;
    }

    /// Fetches mako.json from the repo root and returns it as a RegistryEntry
    /// (Kind "github"). Throws MakoError with a clear reason on any failure —
    /// repo doesn't exist, no mako.json, malformed manifest, network error.
    public static RegistryEntry Fetch(string source)
    {
        if (!TryParseSource(source, out var owner, out var repo))
            throw new MakoError($"'{source}' doesn't look like 'github:User/Repo'");

        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/mako.json";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("mako-cli");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        HttpResponseMessage resp;
        try
        {
            resp = Client.SendAsync(req).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new MakoError($"couldn't reach GitHub for '{owner}/{repo}': {ex.Message}");
        }

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new MakoError(
                $"no mako.json found in {owner}/{repo} (or the repo doesn't exist) - " +
                "this repo isn't set up as a discoverable MAKO package yet.\n" +
                "  A mako.json at the repo root needs: name, description, version");

        var raw = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!resp.IsSuccessStatusCode)
            throw new MakoError($"GitHub returned {(int)resp.StatusCode} for {owner}/{repo}: {raw}");

        ContentsResponse contents;
        try { contents = JsonSerializer.Deserialize<ContentsResponse>(raw) ?? throw new Exception("null"); }
        catch (Exception ex) { throw new MakoError($"couldn't parse GitHub's response for {owner}/{repo}: {ex.Message}"); }

        if (contents.Content == null)
            throw new MakoError($"GitHub's response for {owner}/{repo}/mako.json had no content");

        string json;
        try { json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(contents.Content.Replace("\n", ""))); }
        catch (Exception ex) { throw new MakoError($"couldn't decode mako.json from {owner}/{repo}: {ex.Message}"); }

        Manifest manifest;
        try { manifest = JsonSerializer.Deserialize<Manifest>(json) ?? throw new Exception("null"); }
        catch (Exception ex) { throw new MakoError($"{owner}/{repo}'s mako.json isn't valid JSON: {ex.Message}"); }

        if (manifest.Name == null || manifest.Description == null)
            throw new MakoError($"{owner}/{repo}'s mako.json is missing 'name' or 'description'");

        var name = manifest.Name;
        var version = manifest.Version != null ? $" (v{manifest.Version})" : "";

        return new RegistryEntry
        {
            Name = name,
            Kind = "github",
            Status = "available",
            Description = manifest.Description + version,
            Usage = manifest.Usage ?? $"using {name} from \"github:{owner}/{repo}\";",
            Source = $"github:{owner}/{repo}",
            Docs = null,
            Note = null,
            Versions = null,
        };
    }
}
