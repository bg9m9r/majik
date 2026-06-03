namespace Majik.Core.ValueObjects;

/// <summary>
/// Value object representing a player's mana pool.
/// Immutable and validated.
/// </summary>
public class ManaPool : IEquatable<ManaPool>
{
    /// <summary>
    /// Generic mana count. INCLUDES colorless mana (see <see cref="Colorless"/>):
    /// colorless mana counts toward generic for total / generic-payment purposes
    /// (CR 106.1c), and the colorless subset is tracked separately so a {C} pip
    /// can demand it specifically (CR 107.4c).
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
    /// CR 106.1b — colorless mana count, a TAGGED SUBSET of <see cref="Generic"/>
    /// (<c>Colorless &lt;= Generic</c> always). Colorless is a mana TYPE, not a
    /// color: it is the only mana that can pay a {C} colorless pip (CR 107.4c),
    /// yet it also pays generic {N} pips (CR 106.1c) — hence it lives inside the
    /// Generic count. Sources like Eye of Ugin, Wastes and Karn's Bastion add to
    /// this subset.
    /// </summary>
    public int Colorless { get; }

    /// <summary>
    /// Total mana in pool. Colorless is a subset of Generic — not added again.
    /// </summary>
    public int Total => Generic + White + Blue + Black + Red + Green;

    /// <summary>
    /// Whether the mana pool is empty.
    /// </summary>
    public bool IsEmpty => Total == 0;

    private ManaPool(int generic, int white, int blue, int black, int red, int green, int colorless = 0)
    {
        if (generic < 0 || white < 0 || blue < 0 || black < 0 || red < 0 || green < 0 || colorless < 0)
        {
            throw new ArgumentException("Mana amounts cannot be negative");
        }
        if (colorless > generic)
        {
            // Colorless is a tagged subset of Generic (CR 106.1b) — it can never
            // exceed it. A violation means a caller mis-bucketed colorless mana.
            throw new ArgumentException("Colorless mana cannot exceed generic mana");
        }

        Generic = generic;
        White = white;
        Blue = blue;
        Black = black;
        Red = red;
        Green = green;
        Colorless = colorless;
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
            Green + manaCost.Green,
            Colorless + manaCost.Colorless
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

        return new ManaPool(Generic + amount, White, Blue, Black, Red, Green, Colorless);
    }

    /// <summary>
    /// Add colored mana to the pool. The optional <paramref name="generic"/>
    /// restores plain generic units. The optional <paramref name="colorless"/>
    /// restores colorless ({C}) units — colorless is a tagged subset of generic
    /// (CR 106.1b), so each colorless unit bumps BOTH the generic count and the
    /// colorless subset. Used by the
    /// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> spend-restriction gate
    /// to put back colorless mana it temporarily withheld (Karn, Legacy
    /// Reforged).
    /// </summary>
    public ManaPool AddColored(int white = 0, int blue = 0, int black = 0, int red = 0, int green = 0, int generic = 0, int colorless = 0)
    {
        if (white < 0 || blue < 0 || black < 0 || red < 0 || green < 0 || generic < 0 || colorless < 0)
        {
            throw new ArgumentException("Mana amounts cannot be negative");
        }

        return new ManaPool(
            Generic + generic + colorless,
            White + white,
            Blue + blue,
            Black + black,
            Red + red,
            Green + green,
            Colorless + colorless
        );
    }

    /// <summary>
    /// Return a copy of this pool with the given counts removed (clamped at
    /// zero). Used by the
    /// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> spend-restriction
    /// gate (CR 106.4) to model "this restricted mana is unavailable for the
    /// current spend" without mutating the real pool — the gate then checks
    /// whether the remaining (spendable) mana still covers the cost. The
    /// optional <paramref name="colorless"/> removes colorless ({C}) restricted
    /// units (Karn, Legacy Reforged's "can't be spent to cast nonartifact
    /// spells" {C}; CR 106.1b) — colorless is a tagged subset of generic, so
    /// each removed colorless unit decrements BOTH the generic count and the
    /// colorless subset.
    /// </summary>
    public ManaPool RemoveColored(int white = 0, int blue = 0, int black = 0, int red = 0, int green = 0, int generic = 0, int colorless = 0)
    {
        if (white < 0 || blue < 0 || black < 0 || red < 0 || green < 0 || generic < 0 || colorless < 0)
        {
            throw new ArgumentException("Mana amounts cannot be negative");
        }

        var newColorless = Math.Max(0, Colorless - colorless);
        return new ManaPool(
            Math.Max(0, Generic - generic - colorless),
            Math.Max(0, White - white),
            Math.Max(0, Blue - blue),
            Math.Max(0, Black - black),
            Math.Max(0, Red - red),
            Math.Max(0, Green - green),
            newColorless
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

        // Colorless is a subset of Generic; track a "plain generic" remainder
        // (generic units that are NOT colorless) so the {C}-pip spend and the
        // generic-pip spend draw from the right pools.
        var remainingColorless = Colorless;
        var remainingPlainGeneric = Generic - Colorless;
        var remainingWhite = White;
        var remainingBlue = Blue;
        var remainingBlack = Black;
        var remainingRed = Red;
        var remainingGreen = Green;

        // Pay colored mana (CR 601.2g — each colored pip needs its own color).
        remainingWhite = Math.Max(0, remainingWhite - cost.White);
        remainingBlue = Math.Max(0, remainingBlue - cost.Blue);
        remainingBlack = Math.Max(0, remainingBlack - cost.Black);
        remainingRed = Math.Max(0, remainingRed - cost.Red);
        remainingGreen = Math.Max(0, remainingGreen - cost.Green);

        // CR 107.4c — pay each {C} colorless pip from colorless mana ONLY.
        // CanPay already guaranteed enough colorless is present.
        remainingColorless = Math.Max(0, remainingColorless - cost.Colorless);

        // Generic pips remaining after the {C} pips (cost.Colorless) are already
        // satisfied above (colorless is a subset of cost.Generic, CR 106.1c).
        var genericNeeded = cost.Generic - cost.Colorless;

        // Spend generic from: plain generic, then leftover colorless, then
        // colored. (Leftover colorless can still pay a plain {N} pip.)
        while (genericNeeded > 0 && remainingPlainGeneric > 0) { remainingPlainGeneric--; genericNeeded--; }
        while (genericNeeded > 0 && remainingColorless > 0) { remainingColorless--; genericNeeded--; }
        while (genericNeeded > 0 && remainingWhite > 0) { remainingWhite--; genericNeeded--; }
        while (genericNeeded > 0 && remainingBlue > 0) { remainingBlue--; genericNeeded--; }
        while (genericNeeded > 0 && remainingBlack > 0) { remainingBlack--; genericNeeded--; }
        while (genericNeeded > 0 && remainingRed > 0) { remainingRed--; genericNeeded--; }
        while (genericNeeded > 0 && remainingGreen > 0) { remainingGreen--; genericNeeded--; }

        // Reassemble Generic = plain generic + remaining colorless (subset).
        var newGeneric = remainingPlainGeneric + remainingColorless;
        var newPool = new ManaPool(
            newGeneric, remainingWhite, remainingBlue, remainingBlack,
            remainingRed, remainingGreen, remainingColorless);
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

        // Check colored mana requirements (CR 601.2g).
        if (White < cost.White || Blue < cost.Blue || Black < cost.Black || Red < cost.Red || Green < cost.Green)
        {
            return false;
        }

        // CR 107.4c — each {C} colorless pip can be paid ONLY by colorless mana.
        // Generic / colored mana never substitutes for {C}. (cost.Colorless is a
        // subset of cost.Generic, so it is also counted in the generic check.)
        if (Colorless < cost.Colorless)
        {
            return false;
        }

        // Check total mana for the generic requirement. cost.Generic already
        // INCLUDES the {C} pip count (colorless is a subset), so a single
        // total-vs-(colored + generic) check covers both the {C} pips and the
        // plain {N} pips. Colorless mana counts toward the total (CR 106.1c).
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
               Green == other.Green &&
               Colorless == other.Colorless;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ManaPool);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Generic, White, Blue, Black, Red, Green, Colorless);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Generic > 0) parts.Add($"{Generic}");
        if (Colorless > 0) parts.Add($"{Colorless}C");
        if (White > 0) parts.Add($"{White}W");
        if (Blue > 0) parts.Add($"{Blue}U");
        if (Black > 0) parts.Add($"{Black}B");
        if (Red > 0) parts.Add($"{Red}R");
        if (Green > 0) parts.Add($"{Green}G");
        return parts.Count > 0 ? string.Join("", parts) : "Empty";
    }
}
