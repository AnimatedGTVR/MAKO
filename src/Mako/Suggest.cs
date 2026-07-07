namespace Mako;

/// Fuzzy name matching for "did you mean ...?" hints.
static class Suggest
{
    /// Returns the candidate closest to `name`, or null if nothing is close enough.
    public static string? Closest(string name, IEnumerable<string> candidates)
    {
        if (name.Length == 0) return null;

        int maxDist  = name.Length <= 3 ? 1 : name.Length <= 6 ? 2 : 3;
        int bestDist = maxDist + 1;
        string? best = null;

        foreach (var cand in candidates)
        {
            if (cand.Length == 0 || cand == name) continue;
            if (Math.Abs(cand.Length - name.Length) > maxDist) continue;

            int d = Distance(name.ToLowerInvariant(), cand.ToLowerInvariant());
            if (d < bestDist) { bestDist = d; best = cand; }
        }
        return best;
    }

    // Levenshtein edit distance (two-row DP).
    private static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur  = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;

        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
