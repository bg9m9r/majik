namespace Majik.Core.ValueObjects;

/// <summary>
/// Value object representing a mana cost.
/// Immutable and validated.
/// </summary>
public class ManaCost : IEquatable<ManaCost>
{
    /// <summary>
    /// Generic mana count (colorless).
    /// </summary>
    public int Generic { get; }

    /// <summary>
    /// White mana count.
    /// </summary>
    public int White { get; }

    /// <summary>
    /// Blue mana count.
    /// </summary>
    public int Blue { get; }

    /// <summary>
    /// Black mana count.
    /// </summary>
    public int Black { get; }

    /// <summary>
    /// Red mana count.
    /// </summary>
    public int Red { get; }

    /// <summary>
    /// Green mana count.
    /// </summary>
    public int Green { get; }

    /// <summary>
    /// CR 107.4c — colorless ({C}) pips. Tracked as a TAGGED SUBSET of
    /// <see cref="Generic"/>: a {C} pip still counts toward <see cref="Generic"/>
    /// and <see cref="TotalValue"/> (preserving the long-standing "{C} buckets
    /// as generic" invariant for mana-value / inspection), but this column says
    /// how many of those generic units are specifically the colorless TYPE.
    ///
    /// <para>The distinction matters at payment: as a COST a {C} pip can be paid
    /// <em>only</em> with colorless mana (CR 106.1b — colorless is a mana type,
    /// not a color; CR 601.2g), never colored or non-colorless generic mana, and
    /// a "spend as any color" permission doesn't help (it widens color, not
    /// type). Conversely colorless mana freely pays a generic {N} pip
    /// (CR 106.1c). As a produced-mana descriptor this marks the unit colorless
    /// (Eye of Ugin, Wastes, Karn's Bastion).</para>
    /// </summary>
    public int Colorless { get; }

    /// <summary>
    /// Whether this mana cost contains X (variable cost).
    /// </summary>
    public bool HasX { get; }

    /// <summary>
    /// CR 107.4e — hybrid pips like {R/G} or {2/W}. Each pip contributes 1
    /// to <see cref="TotalValue"/> except {2/W}-style where the higher
    /// generic alternative is used (CR 202.3f).
    /// </summary>
    public IReadOnlyList<HybridPip> HybridPips { get; } = Array.Empty<HybridPip>();

    /// <summary>
    /// CR 107.4f — Phyrexian pips like {U/P}. Each pip can be paid with
    /// one mana of the named colour OR 2 life. Each contributes 1 to
    /// <see cref="TotalValue"/>.
    /// </summary>
    public IReadOnlyList<ManaColor> PhyrexianPips { get; } = Array.Empty<ManaColor>();

    /// <summary>
    /// Total mana value (generic + colored + hybrid + phyrexian).
    /// For {2/W} hybrids, uses the generic alternative (higher value).
    /// </summary>
    public int TotalValue =>
        // Colorless is a subset of Generic — NOT added again (would double-count).
        Generic + White + Blue + Black + Red + Green
        + HybridPips.Sum(h => h.GenericAlternative > 0 ? h.GenericAlternative : 1)
        + PhyrexianPips.Count;

    /// <summary>
    /// Whether this is a zero mana cost.
    /// </summary>
    public bool IsZero => TotalValue == 0 && !HasX;

    private ManaCost(int generic, int white, int blue, int black, int red, int green, bool hasX,
        int colorless = 0)
    {
        Generic = generic;
        White = white;
        Blue = blue;
        Black = black;
        Red = red;
        Green = green;
        Colorless = colorless;
        HasX = hasX;
    }

    private ManaCost(int generic, int white, int blue, int black, int red, int green, bool hasX,
        IReadOnlyList<HybridPip> hybrid, IReadOnlyList<ManaColor> phyrexian, int colorless = 0)
        : this(generic, white, blue, black, red, green, hasX, colorless)
    {
        HybridPips = hybrid;
        PhyrexianPips = phyrexian;
    }

    /// <summary>
    /// Create a mana cost from a string representation.
    /// Examples: "3", "2RR", "1WU", "X", "3XRR"
    /// </summary>
    public static ManaCost Parse(string manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
        {
            return Zero;
        }

        int generic = 0, white = 0, blue = 0, black = 0, red = 0, green = 0, colorless = 0;
        bool hasX = false;
        var hybrid = new List<HybridPip>();
        var phyrexian = new List<ManaColor>();

        // Extract braced symbols first: {R/G}, {2/W}, {U/P}, {W}, {2}, {X}.
        var braceRegex = new System.Text.RegularExpressions.Regex(@"\{([^}]+)\}");
        var stripped = braceRegex.Replace(manaCost, m =>
        {
            var inner = m.Groups[1].Value.ToUpperInvariant();
            if (inner.Contains('/'))
            {
                var parts = inner.Split('/');
                if (parts.Length == 2 && parts[1] == "P")
                {
                    phyrexian.Add(ParseColor(parts[0][0]));
                }
                else if (parts.Length == 2)
                {
                    var c1 = ParseColorOrGeneric(parts[0]);
                    var c2 = ParseColorOrGeneric(parts[1]);
                    var genAlt = int.TryParse(parts[0], out var g) ? g : 0;
                    hybrid.Add(new HybridPip(c1, c2, genAlt));
                }
                return "";
            }
            // Plain {2} or {W} — append to remainder for the legacy parser.
            return inner;
        });

        var upper = stripped.ToUpperInvariant();

        // Check for X
        if (upper.Contains('X'))
        {
            hasX = true;
            upper = upper.Replace("X", "");
        }

        // Parse generic mana (digits at the start — also handles unbraced "2").
        var genericMatch = System.Text.RegularExpressions.Regex.Match(upper, @"^(\d+)");
        if (genericMatch.Success)
        {
            generic = int.Parse(genericMatch.Groups[1].Value);
            upper = upper.Substring(genericMatch.Length);
        }

        // Parse remaining digit clusters that may appear after symbols (rare,
        // but Parse used to be position-tolerant).
        var trailing = System.Text.RegularExpressions.Regex.Match(upper, @"(\d+)");
        if (trailing.Success && trailing.Index > 0)
        {
            generic += int.Parse(trailing.Groups[1].Value);
            upper = upper.Remove(trailing.Index, trailing.Length);
        }

        foreach (var c in upper)
        {
            switch (c)
            {
                case 'W': white++; break;
                case 'U': blue++; break;
                case 'B': black++; break;
                case 'R': red++; break;
                case 'G': green++; break;
                // {C} = colourless mana (CR 107.4c). Counts toward Generic (the
                // long-standing "{C} buckets as generic" mana-value invariant)
                // AND is tagged colorless so a {C} COST pip demands colorless
                // mana (CR 106.1b) while colorless mana still pays generic pips.
                case 'C': generic++; colorless++; break;
                // {S} = snow mana (CR 107.4g). Snow is restricted source —
                // some costs (Skred, Marit Lage's Slumber) require {S}-
                // specific payment. MVP treats as +1 generic so snow lands
                // can pay regular costs; snow-specific gating deferred.
                case 'S': generic++; break;
            }
        }

        return new ManaCost(generic, white, blue, black, red, green, hasX, hybrid, phyrexian, colorless);
    }

    private static ManaColor ParseColor(char c) => c switch
    {
        'W' => ManaColor.White,
        'U' => ManaColor.Blue,
        'B' => ManaColor.Black,
        'R' => ManaColor.Red,
        'G' => ManaColor.Green,
        'C' => ManaColor.Colorless,
        _ => ManaColor.Generic,
    };

    private static ManaColor ParseColorOrGeneric(string s)
    {
        if (int.TryParse(s, out _)) return ManaColor.Generic;
        return ParseColor(s[0]);
    }

    /// <summary>
    /// Zero mana cost.
    /// </summary>
    public static ManaCost Zero => new(0, 0, 0, 0, 0, 0, false);

    /// <summary>
    /// Add N generic mana to this cost (e.g. paying X as part of a spell cost).
    /// </summary>
    public ManaCost AddGenericCost(int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        return new ManaCost(Generic + amount, White, Blue, Black, Red, Green, HasX,
            HybridPips, PhyrexianPips, Colorless);
    }

    /// <summary>Construct a new ManaCost with a different generic component
    /// (other components preserved). Used by cost-reduction effects. Generic
    /// reduction never eats {C} colorless pips (they remain part of Generic as a
    /// subset, CR 106.1b / 117.7e), so newGeneric is clamped to at least the
    /// colorless count to preserve the Colorless ≤ Generic invariant.</summary>
    public ManaCost WithGeneric(int newGeneric)
    {
        if (newGeneric < Colorless) newGeneric = Colorless;
        return new ManaCost(newGeneric, White, Blue, Black, Red, Green, HasX,
            HybridPips, PhyrexianPips, Colorless);
    }

    /// <summary>
    /// CR 609.4b — return a payment-equivalent cost where every <em>colored</em>
    /// pip is collapsed into the generic component, so any single mana of any
    /// color (or generic mana) can satisfy what was a colored requirement. This
    /// is the read-side relaxation a "you may spend mana as though it were mana
    /// of any color (or type)" permission (Robber of the Rich, Fist of Suns,
    /// Cascading Cataracts) grants: the total mana value is unchanged, only the
    /// color requirement is dropped (CR 106.6 — such permissions don't reduce
    /// the cost, they widen which mana qualifies). Hybrid / Phyrexian pips are
    /// preserved unchanged — those are handled by the prompt path, and a
    /// "spend as any color" permission doesn't change how many mana / life they
    /// demand, only the color the colored alternative accepts (out of scope for
    /// this folded-cost shortcut; the colored pips it folds are the ones the
    /// bucketed <see cref="Majik.Core.ValueObjects.ManaPool"/> color-matches).
    /// </summary>
    public ManaCost WithColoredFoldedToGeneric()
    {
        var coloredPips = White + Blue + Black + Red + Green;
        if (coloredPips == 0)
        {
            return this;
        }

        // CR 107.4c — colorless ({C}) pips are NOT folded: "spend mana as
        // though it were any color" widens which COLOR satisfies a colored pip;
        // it never lets generic / colored mana pay a colorless pip (colorless
        // is a mana type, not a color). The {C} demand survives the fold.
        return new ManaCost(
            Generic + coloredPips,
            white: 0, blue: 0, black: 0, red: 0, green: 0,
            HasX, HybridPips, PhyrexianPips, Colorless);
    }

    /// <summary>
    /// Convert to string representation.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();

        if (HasX)
        {
            parts.Add("X");
        }

        // Colorless is a subset of Generic; the non-colorless remainder prints
        // as the generic number, the colorless portion as "C" pips (CR 107.4c).
        var genericOnly = Generic - Colorless;
        if (genericOnly > 0)
        {
            parts.Add(genericOnly.ToString());
        }

        parts.AddRange(Enumerable.Repeat("C", Colorless));
        parts.AddRange(Enumerable.Repeat("W", White));
        parts.AddRange(Enumerable.Repeat("U", Blue));
        parts.AddRange(Enumerable.Repeat("B", Black));
        parts.AddRange(Enumerable.Repeat("R", Red));
        parts.AddRange(Enumerable.Repeat("G", Green));

        return string.Join("", parts);
    }

    public bool Equals(ManaCost? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        
        return Generic == other.Generic &&
               White == other.White &&
               Blue == other.Blue &&
               Black == other.Black &&
               Red == other.Red &&
               Green == other.Green &&
               Colorless == other.Colorless &&
               HasX == other.HasX;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ManaCost);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Generic, White, Blue, Black, Red, Green, Colorless, HasX);
    }

    public static bool operator ==(ManaCost? left, ManaCost? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(ManaCost? left, ManaCost? right)
    {
        return !Equals(left, right);
    }
}
