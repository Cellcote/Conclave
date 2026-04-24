namespace Conclave.App.Sessions;

// Two-word human-friendly names. Display form uses a space ("brave otter"),
// slug form uses a hyphen ("brave-otter") for branch names and directory paths.
public static class SlugGenerator
{
    private static readonly string[] Adjectives =
    {
        "brave", "calm", "clever", "curious", "eager", "fierce", "gentle", "happy",
        "lively", "lucky", "merry", "mighty", "noble", "quick", "quiet", "silly",
        "sleepy", "swift", "tiny", "wise", "bold", "bright", "crisp", "daring",
        "dusty", "fuzzy", "humble", "jolly", "keen", "loud", "misty", "plucky",
        "proud", "shiny", "snug", "spry", "stout", "sunny", "vivid", "witty",
    };

    private static readonly string[] Nouns =
    {
        "otter", "falcon", "badger", "fox", "lynx", "panda", "raven", "tiger",
        "whale", "wolf", "mole", "owl", "hare", "stoat", "crow", "newt",
        "toad", "yak", "bison", "lark", "ibex", "mink", "seal", "swan",
        "deer", "eel", "skunk", "ferret", "heron", "moth", "pike", "shrew",
        "sparrow", "vole", "wasp", "gecko", "goose", "pelican", "robin", "turtle",
    };

    public static (string Display, string Slug) New(Random? rng = null)
    {
        rng ??= Random.Shared;
        var adj = Adjectives[rng.Next(Adjectives.Length)];
        var noun = Nouns[rng.Next(Nouns.Length)];
        return ($"{adj} {noun}", $"{adj}-{noun}");
    }
}
