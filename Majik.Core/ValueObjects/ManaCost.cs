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
    /// Whether this mana cost contains X (variable cost).
    /// </summary>
    public bool HasX { get; }

    /// <summary>
    /// Total mana value (generic + colored).
    /// </summary>
    public int TotalValue => Generic + White + Blue + Black + Red + Green;

    /// <summary>
    /// Whether this is a zero mana cost.
    /// </summary>
    public bool IsZero => TotalValue == 0 && !HasX;

    private ManaCost(int generic, int white, int blue, int black, int red, int green, bool hasX)
    {
        Generic = generic;
        White = white;
        Blue = blue;
        Black = black;
        Red = red;
        Green = green;
        HasX = hasX;
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

        int generic = 0;
        int white = 0;
        int blue = 0;
        int black = 0;
        int red = 0;
        int green = 0;
        bool hasX = false;

        // Simple parser for basic mana costs
        // This can be extended for more complex costs
        var upper = manaCost.ToUpperInvariant();
        
        // Check for X
        if (upper.Contains('X'))
        {
            hasX = true;
            upper = upper.Replace("X", "");
        }

        // Parse generic mana (digits at the start)
        var genericMatch = System.Text.RegularExpressions.Regex.Match(upper, @"^(\d+)");
        if (genericMatch.Success)
        {
            generic = int.Parse(genericMatch.Groups[1].Value);
            upper = upper.Substring(genericMatch.Length);
        }

        // Parse colored mana symbols
        foreach (var c in upper)
        {
            switch (c)
            {
                case 'W':
                    white++;
                    break;
                case 'U':
                    blue++;
                    break;
                case 'B':
                    black++;
                    break;
                case 'R':
                    red++;
                    break;
                case 'G':
                    green++;
                    break;
            }
        }

        return new ManaCost(generic, white, blue, black, red, green, hasX);
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
        return new ManaCost(Generic + amount, White, Blue, Black, Red, Green, HasX);
    }

    /// <summary>Construct a new ManaCost with a different generic component
    /// (other components preserved). Used by cost-reduction effects.</summary>
    public ManaCost WithGeneric(int newGeneric)
    {
        if (newGeneric < 0) newGeneric = 0;
        return new ManaCost(newGeneric, White, Blue, Black, Red, Green, HasX);
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

        if (Generic > 0)
        {
            parts.Add(Generic.ToString());
        }

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
               HasX == other.HasX;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ManaCost);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Generic, White, Blue, Black, Red, Green, HasX);
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
