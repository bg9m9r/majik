namespace Majik.Core.ValueObjects;

/// <summary>
/// Value object representing a player's mana pool.
/// Immutable and validated.
/// </summary>
public class ManaPool : IEquatable<ManaPool>
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
    /// Total mana in pool.
    /// </summary>
    public int Total => Generic + White + Blue + Black + Red + Green;

    /// <summary>
    /// Whether the mana pool is empty.
    /// </summary>
    public bool IsEmpty => Total == 0;

    private ManaPool(int generic, int white, int blue, int black, int red, int green)
    {
        if (generic < 0 || white < 0 || blue < 0 || black < 0 || red < 0 || green < 0)
        {
            throw new ArgumentException("Mana amounts cannot be negative");
        }

        Generic = generic;
        White = white;
        Blue = blue;
        Black = black;
        Red = red;
        Green = green;
    }

    /// <summary>
    /// Create an empty mana pool.
    /// </summary>
    public static ManaPool Empty => new(0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Add mana to the pool.
    /// </summary>
    public ManaPool Add(ManaCost manaCost)
    {
        if (manaCost == null)
        {
            throw new ArgumentNullException(nameof(manaCost));
        }

        return new ManaPool(
            Generic + manaCost.Generic,
            White + manaCost.White,
            Blue + manaCost.Blue,
            Black + manaCost.Black,
            Red + manaCost.Red,
            Green + manaCost.Green
        );
    }

    /// <summary>
    /// Add generic mana to the pool.
    /// </summary>
    public ManaPool AddGeneric(int amount)
    {
        if (amount < 0)
        {
            throw new ArgumentException("Amount cannot be negative", nameof(amount));
        }

        return new ManaPool(Generic + amount, White, Blue, Black, Red, Green);
    }

    /// <summary>
    /// Add colored mana to the pool.
    /// </summary>
    public ManaPool AddColored(int white = 0, int blue = 0, int black = 0, int red = 0, int green = 0)
    {
        if (white < 0 || blue < 0 || black < 0 || red < 0 || green < 0)
        {
            throw new ArgumentException("Mana amounts cannot be negative");
        }

        return new ManaPool(
            Generic,
            White + white,
            Blue + blue,
            Black + black,
            Red + red,
            Green + green
        );
    }

    /// <summary>
    /// Remove mana from the pool (for paying costs).
    /// Returns the new pool and whether the payment was successful.
    /// </summary>
    public (ManaPool NewPool, bool Success) Pay(ManaCost cost)
    {
        if (cost == null)
        {
            throw new ArgumentNullException(nameof(cost));
        }

        // Check if we have enough mana
        if (!CanPay(cost))
        {
            return (this, false);
        }

        // Pay colored mana first, then generic
        var remainingGeneric = Generic;
        var remainingWhite = White;
        var remainingBlue = Blue;
        var remainingBlack = Black;
        var remainingRed = Red;
        var remainingGreen = Green;

        // Pay colored mana
        remainingWhite = Math.Max(0, remainingWhite - cost.White);
        remainingBlue = Math.Max(0, remainingBlue - cost.Blue);
        remainingBlack = Math.Max(0, remainingBlack - cost.Black);
        remainingRed = Math.Max(0, remainingRed - cost.Red);
        remainingGreen = Math.Max(0, remainingGreen - cost.Green);

        // Pay generic mana (can use any mana)
        var genericNeeded = cost.Generic;
        var totalAvailable = remainingGeneric + remainingWhite + remainingBlue + remainingBlack + remainingRed + remainingGreen;

        if (totalAvailable < genericNeeded)
        {
            return (this, false);
        }

        // Pay generic from available mana (prefer generic, then colored)
        while (genericNeeded > 0 && remainingGeneric > 0)
        {
            remainingGeneric--;
            genericNeeded--;
        }

        while (genericNeeded > 0 && remainingWhite > 0)
        {
            remainingWhite--;
            genericNeeded--;
        }

        while (genericNeeded > 0 && remainingBlue > 0)
        {
            remainingBlue--;
            genericNeeded--;
        }

        while (genericNeeded > 0 && remainingBlack > 0)
        {
            remainingBlack--;
            genericNeeded--;
        }

        while (genericNeeded > 0 && remainingRed > 0)
        {
            remainingRed--;
            genericNeeded--;
        }

        while (genericNeeded > 0 && remainingGreen > 0)
        {
            remainingGreen--;
            genericNeeded--;
        }

        var newPool = new ManaPool(remainingGeneric, remainingWhite, remainingBlue, remainingBlack, remainingRed, remainingGreen);
        return (newPool, true);
    }

    /// <summary>
    /// Check if the pool can pay the given cost.
    /// </summary>
    public bool CanPay(ManaCost cost)
    {
        if (cost == null)
        {
            return false;
        }

        // Check colored mana requirements
        if (White < cost.White || Blue < cost.Blue || Black < cost.Black || Red < cost.Red || Green < cost.Green)
        {
            return false;
        }

        // Check total mana for generic requirement
        var totalAvailable = Total;
        var coloredPaid = cost.White + cost.Blue + cost.Black + cost.Red + cost.Green;
        var genericNeeded = cost.Generic;

        return totalAvailable >= (coloredPaid + genericNeeded);
    }

    /// <summary>
    /// Empty the mana pool (happens at end of steps/phases per Rule 500.4).
    /// </summary>
    public ManaPool EmptyPool()
    {
        return Empty;
    }

    public bool Equals(ManaPool? other)
    {
        if (other == null) return false;
        return Generic == other.Generic &&
               White == other.White &&
               Blue == other.Blue &&
               Black == other.Black &&
               Red == other.Red &&
               Green == other.Green;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ManaPool);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Generic, White, Blue, Black, Red, Green);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Generic > 0) parts.Add($"{Generic}");
        if (White > 0) parts.Add($"{White}W");
        if (Blue > 0) parts.Add($"{Blue}U");
        if (Black > 0) parts.Add($"{Black}B");
        if (Red > 0) parts.Add($"{Red}R");
        if (Green > 0) parts.Add($"{Green}G");
        return parts.Count > 0 ? string.Join("", parts) : "Empty";
    }
}
