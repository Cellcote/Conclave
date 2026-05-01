using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Conclave.App.Design;

namespace Conclave.App.Views.Shell;

// Tiny markdown subset renderer: bold (**), inline code (`), code fences (```),
// bullet lists (- /+ /*), numbered lists (1.), ATX headers (# .. ######), and
// GFM pipe tables. No links — just what claude actually emits in normal replies.
// Enough to make assistant messages stop looking like raw text.
public sealed class MarkdownView : UserControl
{
    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<MarkdownView, string?>(nameof(Source));

    public static readonly StyledProperty<Tokens?> TokensProperty =
        AvaloniaProperty.Register<MarkdownView, Tokens?>(nameof(Tokens));

    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public Tokens? Tokens
    {
        get => GetValue(TokensProperty);
        set => SetValue(TokensProperty, value);
    }

    private readonly StackPanel _root;

    public MarkdownView()
    {
        _root = new StackPanel { Spacing = 8 };
        Content = _root;
    }

    static MarkdownView()
    {
        SourceProperty.Changed.AddClassHandler<MarkdownView>((v, _) => v.Rebuild());
        TokensProperty.Changed.AddClassHandler<MarkdownView>((v, _) => v.Rebuild());
    }

    private void Rebuild()
    {
        _root.Children.Clear();
        if (string.IsNullOrEmpty(Source) || Tokens is null) return;
        foreach (var block in Parse(Source))
            _root.Children.Add(block.Render(Tokens));
    }

    // --- Parsing ---

    private static IEnumerable<Block> Parse(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i];

            // Skip pure blank lines between blocks.
            if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

            // Code fence: ```optional-lang
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                var lang = line.Length > 3 ? line[3..].Trim() : "";
                var code = new StringBuilder();
                i++;
                while (i < lines.Length && !lines[i].StartsWith("```", StringComparison.Ordinal))
                {
                    if (code.Length > 0) code.Append('\n');
                    code.Append(lines[i]);
                    i++;
                }
                if (i < lines.Length) i++;  // consume the closing fence
                yield return new CodeBlock(lang, code.ToString());
                continue;
            }

            // ATX header: 1-6 '#' followed by a space.
            if (LooksLikeHeader(line, out var level, out var headerText))
            {
                i++;
                yield return new HeaderBlock(level, headerText);
                continue;
            }

            // GFM pipe table: header row + separator row (---) + data rows.
            if (line.Contains('|')
                && i + 1 < lines.Length
                && IsTableSeparator(lines[i + 1]))
            {
                var headers = ParseTableRow(line);
                i += 2;  // header + separator
                var rows = new List<IReadOnlyList<string>>();
                while (i < lines.Length
                       && !string.IsNullOrWhiteSpace(lines[i])
                       && lines[i].Contains('|'))
                {
                    rows.Add(ParseTableRow(lines[i]));
                    i++;
                }
                yield return new TableBlock(headers, rows);
                continue;
            }

            // List (bullets or numbered) — consume contiguous lines.
            if (LooksLikeListItem(line, out _, out _))
            {
                bool numbered = char.IsDigit(line.TrimStart()[0]);
                var items = new List<string>();
                while (i < lines.Length && LooksLikeListItem(lines[i], out _, out var content))
                {
                    items.Add(content);
                    i++;
                }
                yield return new ListBlock(numbered, items);
                continue;
            }

            // Paragraph — consume until blank line / list / fence / header / table.
            var para = new StringBuilder();
            while (i < lines.Length
                   && !string.IsNullOrWhiteSpace(lines[i])
                   && !lines[i].StartsWith("```", StringComparison.Ordinal)
                   && !LooksLikeListItem(lines[i], out _, out _)
                   && !LooksLikeHeader(lines[i], out _, out _)
                   && !(lines[i].Contains('|')
                        && i + 1 < lines.Length
                        && IsTableSeparator(lines[i + 1])))
            {
                if (para.Length > 0) para.Append(' ');
                para.Append(lines[i].TrimEnd());
                i++;
            }
            yield return new ParagraphBlock(para.ToString());
        }
    }

    private static bool LooksLikeHeader(string line, out int level, out string text)
    {
        level = 0;
        text = "";
        var trimmed = line.TrimStart();
        int n = 0;
        while (n < trimmed.Length && trimmed[n] == '#' && n < 6) n++;
        if (n == 0 || n >= trimmed.Length || trimmed[n] != ' ') return false;
        level = n;
        // CommonMark optional closing '#' run: only stripped when preceded by
        // whitespace (or when it makes up the entire body), so "## C#" keeps
        // its trailing '#' but "## Heading ##" does not.
        var raw = trimmed[(n + 1)..].TrimEnd();
        int end = raw.Length;
        while (end > 0 && raw[end - 1] == '#') end--;
        if (end < raw.Length && (end == 0 || raw[end - 1] == ' ' || raw[end - 1] == '\t'))
            text = raw[..end].TrimEnd();
        else
            text = raw;
        return true;
    }

    // GFM table separator: cells of optional ':' + dashes + optional ':',
    // separated by '|'. Outer pipes are optional. e.g. "|---|:--:|--:|".
    private static bool IsTableSeparator(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || !trimmed.Contains('|')) return false;
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        var cells = trimmed.Split('|');
        if (cells.Length == 0) return false;
        foreach (var raw in cells)
        {
            var c = raw.Trim();
            if (c.Length == 0) return false;
            int start = 0, end = c.Length;
            if (c[0] == ':') start = 1;
            if (c[^1] == ':') end = c.Length - 1;
            if (end <= start) return false;
            for (int k = start; k < end; k++)
                if (c[k] != '-') return false;
        }
        return true;
    }

    private static IReadOnlyList<string> ParseTableRow(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|')) trimmed = trimmed[1..];
        if (trimmed.EndsWith('|')) trimmed = trimmed[..^1];
        return trimmed.Split('|').Select(c => c.Trim()).ToList();
    }

    private static bool LooksLikeListItem(string line, out string marker, out string content)
    {
        marker = "";
        content = "";
        var trimmed = line.TrimStart();
        if (trimmed.Length < 2) return false;

        // Bullet markers
        if ((trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+')
            && trimmed[1] == ' ')
        {
            marker = "•";
            content = trimmed[2..];
            return true;
        }

        // Numbered markers like "1. ", "12. "
        int n = 0;
        while (n < trimmed.Length && char.IsDigit(trimmed[n])) n++;
        if (n > 0 && n + 1 < trimmed.Length && trimmed[n] == '.' && trimmed[n + 1] == ' ')
        {
            marker = trimmed[..(n + 1)];
            content = trimmed[(n + 2)..];
            return true;
        }
        return false;
    }

    // --- Inline parsing: bold (**) and inline code (`) ---

    private static IReadOnlyList<Inline> ParseInlines(string text, Tokens tokens)
    {
        var list = new List<Inline>();
        var buf = new StringBuilder();

        void FlushPlain()
        {
            if (buf.Length == 0) return;
            list.Add(new Run(buf.ToString()));
            buf.Clear();
        }

        int i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            // Inline code (`...`) — single backtick form. Doesn't span newlines.
            if (c == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    FlushPlain();
                    var code = text.Substring(i + 1, close - i - 1);
                    list.Add(new Run(code)
                    {
                        FontFamily = MonoFamily,
                        FontSize = 12.5,
                        Background = tokens.Panel2,
                        Foreground = tokens.Text,
                    });
                    i = close + 1;
                    continue;
                }
            }
            // Bold (**...**)
            if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (close > i + 1)
                {
                    FlushPlain();
                    var inner = text.Substring(i + 2, close - i - 2);
                    var bold = new Bold();
                    bold.Inlines!.Add(new Run(inner));
                    list.Add(bold);
                    i = close + 2;
                    continue;
                }
            }
            buf.Append(c);
            i++;
        }
        FlushPlain();
        return list;
    }

    // --- Block types ---

    private abstract record Block
    {
        public abstract Control Render(Tokens tokens);
    }

    private sealed record ParagraphBlock(string Text) : Block
    {
        public override Control Render(Tokens tokens)
        {
            var tb = new SelectableTextBlock
            {
                FontSize = 13.5,
                LineHeight = 21,
                Foreground = tokens.Text,
                TextWrapping = TextWrapping.Wrap,
            };
            foreach (var inline in ParseInlines(Text, tokens)) tb.Inlines!.Add(inline);
            return tb;
        }
    }

    private sealed record HeaderBlock(int Level, string Text) : Block
    {
        public override Control Render(Tokens tokens)
        {
            // Sizes step down per level. H1 is rare in chat; keep it punchy but
            // not so big it dominates the whole bubble.
            var (size, lineHeight, weight) = Level switch
            {
                1 => (20.0, 28.0, FontWeight.Bold),
                2 => (17.0, 24.0, FontWeight.Bold),
                3 => (15.0, 22.0, FontWeight.SemiBold),
                _ => (14.0, 22.0, FontWeight.SemiBold),
            };
            var tb = new SelectableTextBlock
            {
                FontSize = size,
                LineHeight = lineHeight,
                FontWeight = weight,
                Foreground = tokens.Text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            };
            foreach (var inline in ParseInlines(Text, tokens)) tb.Inlines!.Add(inline);
            return tb;
        }
    }

    private sealed record TableBlock(
        IReadOnlyList<string> Headers,
        IReadOnlyList<IReadOnlyList<string>> Rows) : Block
    {
        public override Control Render(Tokens tokens)
        {
            var cols = Headers.Count;
            var grid = new Grid();
            for (int c = 0; c < cols; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            // Let the last column stretch so the table fills available width.
            if (cols > 0)
                grid.ColumnDefinitions[cols - 1] = new ColumnDefinition(GridLength.Star);
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int r = 0; r < Rows.Count; r++)
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            for (int c = 0; c < cols; c++)
            {
                var cell = MakeCell(Headers[c], tokens, isHeader: true,
                    isLastRow: Rows.Count == 0, isLastCol: c == cols - 1);
                Grid.SetRow(cell, 0);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }

            for (int r = 0; r < Rows.Count; r++)
            {
                var row = Rows[r];
                for (int c = 0; c < cols; c++)
                {
                    var text = c < row.Count ? row[c] : "";
                    var cell = MakeCell(text, tokens, isHeader: false,
                        isLastRow: r == Rows.Count - 1, isLastCol: c == cols - 1);
                    Grid.SetRow(cell, r + 1);
                    Grid.SetColumn(cell, c);
                    grid.Children.Add(cell);
                }
            }

            return new Border
            {
                BorderBrush = tokens.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = tokens.RadSmCorner,
                ClipToBounds = true,
                Child = grid,
            };
        }

        private static Control MakeCell(string text, Tokens tokens,
            bool isHeader, bool isLastRow, bool isLastCol)
        {
            var tb = new SelectableTextBlock
            {
                FontSize = 13,
                LineHeight = 19,
                Foreground = tokens.Text,
                TextWrapping = TextWrapping.Wrap,
                FontWeight = isHeader ? FontWeight.SemiBold : FontWeight.Normal,
            };
            foreach (var inline in ParseInlines(text, tokens)) tb.Inlines!.Add(inline);
            return new Border
            {
                Background = isHeader ? tokens.Panel2 : null,
                BorderBrush = tokens.Border,
                BorderThickness = new Thickness(
                    0, 0,
                    isLastCol ? 0 : 1,
                    isLastRow ? 0 : 1),
                Padding = new Thickness(10, 6),
                Child = tb,
            };
        }
    }

    private sealed record CodeBlock(string Language, string Code) : Block
    {
        public override Control Render(Tokens tokens) =>
            new Border
            {
                Background = tokens.Panel2,
                BorderBrush = tokens.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = tokens.RadSmCorner,
                Padding = new Thickness(12, 9),
                Child = new SelectableTextBlock
                {
                    Text = Code,
                    FontFamily = MonoFamily,
                    FontSize = 12,
                    Foreground = tokens.Text,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                },
            };
    }

    private sealed record ListBlock(bool Numbered, IReadOnlyList<string> Items) : Block
    {
        public override Control Render(Tokens tokens)
        {
            var stack = new StackPanel { Spacing = 4 };
            for (int idx = 0; idx < Items.Count; idx++)
            {
                var marker = Numbered ? $"{idx + 1}." : "•";
                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                };
                Grid.SetColumn(grid, 0);
                var markerTb = new TextBlock
                {
                    Text = marker,
                    FontSize = 13.5,
                    Foreground = tokens.TextDim,
                    Margin = new Thickness(0, 0, 8, 0),
                    MinWidth = 18,
                };
                if (Numbered) markerTb.FontFamily = MonoFamily;
                grid.Children.Add(markerTb);

                var bodyTb = new SelectableTextBlock
                {
                    FontSize = 13.5,
                    LineHeight = 21,
                    Foreground = tokens.Text,
                    TextWrapping = TextWrapping.Wrap,
                };
                Grid.SetColumn(bodyTb, 1);
                foreach (var inline in ParseInlines(Items[idx], tokens))
                    bodyTb.Inlines!.Add(inline);
                grid.Children.Add(bodyTb);

                stack.Children.Add(grid);
            }
            return stack;
        }
    }

    private static readonly FontFamily MonoFamily =
        new("ui-monospace,SFMono-Regular,Menlo,Consolas,monospace");
}
