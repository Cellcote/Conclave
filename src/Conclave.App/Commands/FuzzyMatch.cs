namespace Conclave.App.Commands;

// Subsequence-based fuzzy matcher for the command palette. All query chars must
// appear in order in the candidate. Score rewards consecutive matches and matches
// at word boundaries — the "ns" in "**N**ew **s**ession" beats "**n**ew sessio**n**s"
// even though both match.
//
// Returns (score, matchedIndices) so the UI can highlight the matched chars. Score
// of 0 means no match; higher is better.
public static class FuzzyMatch
{
    public static (int Score, int[] Indices) Score(string query, string candidate)
    {
        if (string.IsNullOrEmpty(query)) return (1, Array.Empty<int>());
        if (string.IsNullOrEmpty(candidate)) return (0, Array.Empty<int>());

        var indices = new int[query.Length];
        int score = 0;
        int qi = 0;
        int lastMatch = -2;

        for (int ci = 0; ci < candidate.Length && qi < query.Length; ci++)
        {
            char qc = char.ToLowerInvariant(query[qi]);
            char cc = char.ToLowerInvariant(candidate[ci]);
            if (qc != cc) continue;

            indices[qi] = ci;
            // Bonus: consecutive match (no gap from previous match).
            if (ci == lastMatch + 1) score += 5;
            // Bonus: matched at start of string.
            if (ci == 0) score += 8;
            // Bonus: matched at a word boundary (after space/punctuation).
            else if (IsBoundary(candidate[ci - 1])) score += 4;
            // Base: every match contributes a small amount so longer matches outscore shorter
            // (when ties happen on bonuses).
            score += 1;
            lastMatch = ci;
            qi++;
        }

        if (qi < query.Length) return (0, Array.Empty<int>()); // unmatched chars left
        // Penalty: shorter candidates of equal match are preferable. Subtract a tiny bit
        // per unused char so "new session" beats "newest sessions" for "new s".
        score -= candidate.Length / 8;
        return (Math.Max(score, 1), indices);
    }

    private static bool IsBoundary(char c) => c is ' ' or '-' or '_' or '.' or '/' or ':';
}
