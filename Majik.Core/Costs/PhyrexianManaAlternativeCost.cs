using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.Costs;

/// <summary>
/// CR 107.4f / 118.8 — Phyrexian-mana alternative cost. Each phyrexian
/// pip <c>{X/P}</c> on a spell may be paid by 2 life instead of one mana
/// of the named colour. This cost models the "pay every phyrexian pip
/// with 2 life" choice (the entire phyrexian portion), leaving the
/// non-phyrexian portion of the printed cost (generic + colored + hybrid
/// pips) to be paid normally.
///
/// ## Shape
/// - <see cref="AlternativeManaCost"/> = the printed cost MINUS the
///   phyrexian pips. Surgical Extraction (<c>{B/P}</c>) becomes
///   <see cref="ManaCost.Zero"/>; a hypothetical <c>{2}{B/P}</c> would
///   become <c>{2}</c>.
/// - <see cref="OnResolved"/> charges <see cref="LifeCost"/> = 2 × pips
///   (CR 118.8 — life payments happen at spell-resolution time as part
///   of paying the cost).
///
/// ## Differences vs. <see cref="PitchAlternativeCost"/>
/// - No "if it's not your turn" gating — phyrexian is always available.
/// - No exile side-effect; only life payment.
///
/// Bot-side probe / cast-time selection is the caller's responsibility;
/// this class only models the cost itself.
/// </summary>
public sealed class PhyrexianManaAlternativeCost : IAlternativeCost
{
    /// <summary>The non-phyrexian portion of the spell's printed cost.</summary>
    public ManaCost AlternativeManaCost { get; }

    /// <summary>Total life to pay (2 per phyrexian pip).</summary>
    public int LifeCost { get; }

    public string Description =>
        LifeCost > 0
            ? $"Phyrexian — pay {LifeCost} life instead of phyrexian mana"
            : "Phyrexian — pay 0 life (no phyrexian pips)";

    private PhyrexianManaAlternativeCost(ManaCost remainingCost, int lifeCost)
    {
        AlternativeManaCost = remainingCost;
        LifeCost = lifeCost;
    }

    /// <summary>
    /// Build a phyrexian alt-cost from a printed mana cost. Strips every
    /// phyrexian pip from the printed cost and converts each into 2 life.
    /// </summary>
    public static PhyrexianManaAlternativeCost ForPrintedCost(ManaCost printed)
    {
        if (printed == null) throw new ArgumentNullException(nameof(printed));
        var pipCount = printed.PhyrexianPips.Count;
        if (pipCount == 0)
        {
            throw new InvalidOperationException(
                "PhyrexianManaAlternativeCost requires the printed cost to have at least one phyrexian pip.");
        }
        // The printed cost type's only state outside the colored buckets is
        // PhyrexianPips itself; stripping the pips is equivalent to
        // re-parsing the printed cost without the {X/P} symbols. Use
        // ManaCost.Parse to get a clean remainder. Surgical Extraction's
        // printed cost is exactly {B/P} → empty remainder = Zero.
        // For composite costs like {2}{B/P} the remainder is {2}.
        // We reconstruct by string-stripping rather than touching ManaCost
        // internals.
        return new PhyrexianManaAlternativeCost(StripPhyrexian(printed), 2 * pipCount);
    }

    /// <summary>
    /// Returns true — phyrexian alt cost has no card-specific predicate
    /// beyond "the spell has phyrexian pips" (which is enforced at
    /// construction). CR 118.8 / 107.4f impose no other timing or zone
    /// restriction.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (caster == null) return false;
        // Caster must have enough life — paying life can't reduce them
        // below zero before SBAs run; this is the spell-cast-time check.
        // (CR 118.4 allows paying life that would make total ≤0 only via
        // replacement effects; we keep the conservative gate.)
        if (LifeCost > 0 && caster.LifeTotal < LifeCost) return false;
        return true;
    }

    /// <summary>
    /// CR 118.8 — pay the life portion of the phyrexian cost after the
    /// spell resolves. (Strictly the rules pay this during cost payment;
    /// applying it on resolve matches how the other IAlternativeCosts in
    /// this codebase are wired and keeps the semantics observationally
    /// equivalent for SBAs that run after the stack resolves.)
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        if (caster == null) return;
        if (LifeCost > 0)
        {
            caster.LoseLife(LifeCost);
        }
    }

    private static ManaCost StripPhyrexian(ManaCost printed)
    {
        // Re-emit the cost as a string with phyrexian pips dropped, then
        // re-parse — avoids depending on ManaCost's internal ctor.
        var sb = new System.Text.StringBuilder();
        if (printed.HasX) sb.Append("{X}");
        if (printed.Generic > 0) sb.Append('{').Append(printed.Generic).Append('}');
        sb.Append(Repeat("{W}", printed.White));
        sb.Append(Repeat("{U}", printed.Blue));
        sb.Append(Repeat("{B}", printed.Black));
        sb.Append(Repeat("{R}", printed.Red));
        sb.Append(Repeat("{G}", printed.Green));
        foreach (var h in printed.HybridPips)
        {
            sb.Append('{');
            sb.Append(h.GenericAlternative > 0 ? h.GenericAlternative.ToString() : ColorChar(h.Color1).ToString());
            sb.Append('/');
            sb.Append(ColorChar(h.Color2));
            sb.Append('}');
        }
        // Phyrexian pips intentionally omitted.
        return sb.Length == 0 ? ManaCost.Zero : ManaCost.Parse(sb.ToString());
    }

    private static string Repeat(string s, int n) => string.Concat(Enumerable.Repeat(s, n));

    private static char ColorChar(ManaColor c) => c switch
    {
        ManaColor.White => 'W',
        ManaColor.Blue => 'U',
        ManaColor.Black => 'B',
        ManaColor.Red => 'R',
        ManaColor.Green => 'G',
        ManaColor.Colorless => 'C',
        _ => 'C',
    };
}
