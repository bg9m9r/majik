using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 601.2f + CR 117.7c — "March" additional-cost-with-cost-reduction
/// mechanism. The Strixhaven "March of …" cycle prints:
///
///   "As an additional cost to cast this spell, you may exile any number
///    of [color] cards from your hand. This spell costs {2} less to cast
///    for each card exiled this way."
///
/// <para>The cost is OPTIONAL (the caster may exile zero cards). For each
/// card exiled, the cast's generic cost is reduced by {2}, floored at
/// zero per CR 117.7c — the colour pip and the announced X are
/// preserved verbatim.</para>
///
/// <para><b>Shape (mirror of <see cref="ConvokeAdditionalCost"/> /
/// <see cref="ImproviseAdditionalCost"/>):</b> the caller (player agent,
/// bot probe, or test) pre-selects the cards to exile and hands the
/// resulting <see cref="MarchAdditionalCost"/> to
/// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/> via the
/// <c>additionalCosts</c> list. The cast flow's CR 601.2f additional-cost
/// loop calls <see cref="CanPay"/> then <see cref="Pay"/>;
/// <see cref="Pay"/> exiles the selected cards (CR 701.21), and the
/// cast-flow's mana-cost computation calls <see cref="ApplyTo"/> to
/// subtract {2N} generic pips before prompting the agent for the
/// remaining mana payment.</para>
///
/// <para><b>Why a dedicated class:</b> Convoke (CR 702.51) and Improvise
/// (CR 702.127) are both 1-tap-per-{1} reductions tied to permanents the
/// caster controls; March is a 1-exile-per-{2} reduction tied to a
/// COLOUR-filtered subset of the caster's HAND. The selection predicate
/// is different and the per-card multiplier differs, so a separate value
/// type is the cleanest model.</para>
///
/// <para><b>Cycle members served</b>:
///   * <i>March of Wretched Sorrow</i> — {X}{B} — black hand exile.
///   * <i>March of Otherworldly Light</i> — {X}{W} — white hand exile.
///   * <i>March of Burgeoning Life</i> — {X}{G} — green hand exile.
///   * <i>March of Reckless Joy</i> — {X}{R} — red hand exile.
///   * <i>March of Swirling Mist</i> — {X}{U} — blue hand exile.
/// All five reuse this primitive verbatim with a different
/// <see cref="RequiredColor"/>.</para>
///
/// <para>CR rule references: 601.2f (additional-cost timing),
/// 117.7c (cost-reduction floor at zero), 701.21 (exile zone move),
/// 605.1 (mana abilities resolve first), 107.3 (X is locked in at cast).</para>
/// </summary>
public sealed class MarchAdditionalCost : IAdditionalCost
{
    /// <summary>The spell being cast with the March cost. The exiled
    /// cards must be in the caster's hand AND distinct from this card —
    /// the spell itself is mid-cast and not eligible self-fuel.</summary>
    public ICard Source { get; }

    /// <summary>The colour the exiled cards must include. Set per cycle
    /// member: black for March of Wretched Sorrow, white for March of
    /// Otherworldly Light, etc.</summary>
    public ManaColor RequiredColor { get; }

    /// <summary>
    /// The cards the caster has selected to exile from hand. Each
    /// contributes {2} of generic-mana reduction (CR 117.7c floored at
    /// zero). Order is preserved for diagnostics + future downstream
    /// effects that count "cards exiled this way".
    /// </summary>
    public IReadOnlyList<ICard> Chosen { get; }

    /// <summary>Convenience accessor — number of cards exiled.</summary>
    public int ExiledCount => Chosen.Count;

    /// <summary>Generic-mana reduction granted by the exile selection —
    /// {2} per card. Always non-negative.</summary>
    public int ReductionAmount => Chosen.Count * 2;

    public string Description =>
        Chosen.Count == 0
            ? $"March — exile {RequiredColor} cards from hand (none)"
            : $"March — exile {Chosen.Count} {RequiredColor} card(s) from hand "
              + $"for {{{ReductionAmount}}}";

    public MarchAdditionalCost(ICard source, ManaColor requiredColor, IReadOnlyList<ICard> chosen)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (chosen == null) throw new ArgumentNullException(nameof(chosen));
        RequiredColor = requiredColor;
        // Defensive copy — value object semantics; mirrors ConvokeAdditionalCost.
        Chosen = chosen.ToList();
    }

    /// <summary>
    /// Legality check. Each chosen card must be in the caster's hand,
    /// owned by the caster, include the required colour, and NOT be the
    /// spell being cast (the source moves Hand → Stack later in the cast
    /// flow; it isn't a legal additional-cost payment for itself).
    /// Duplicates are rejected (CR 118.12 — each cost is paid once).
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;

        var seen = new HashSet<ICard>(ReferenceEqualityComparer.Instance);
        foreach (var c in Chosen)
        {
            if (c == null) return false;
            if (!ReferenceEquals(c.Owner, caster)) return false;
            if (c.Zone != ZoneType.Hand) return false;
            if (ReferenceEquals(c, Source)) return false;
            if (!CardColors.GetColors(c).Contains(RequiredColor)) return false;
            if (!seen.Add(c)) return false;
        }

        return true;
    }

    /// <summary>
    /// CR 701.21 — exile each chosen card from the caster's hand. The
    /// cast-flow additional-cost loop calls this AFTER
    /// <see cref="CanPay"/> so we treat an illegal selection as a no-op
    /// and report failure rather than half-exiling.
    /// </summary>
    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;

        foreach (var c in Chosen)
        {
            caster.Zones.Hand.RemoveCard(c);
            caster.Zones.Exile.AddCard(c);
            c.SetZone(ZoneType.Exile);
        }

        return true;
    }

    /// <summary>
    /// Compute the post-March mana cost: reduce generic by
    /// <see cref="ReductionAmount"/> (= {2} per exiled card), floored at
    /// 0 per CR 117.7c. The coloured pip (e.g. {B} for March of Wretched
    /// Sorrow) is preserved verbatim — March only consumes the generic
    /// portion, including the generic mana the announced X folds in
    /// (CR 107.3 + cast-flow ordering: X is added to Generic BEFORE
    /// March's reduction is applied, so the same exile selection
    /// reduces the X portion uniformly).
    /// </summary>
    public ManaCost ApplyTo(ManaCost printedCost)
    {
        if (printedCost == null) throw new ArgumentNullException(nameof(printedCost));
        var newGeneric = Math.Max(0, printedCost.Generic - ReductionAmount);
        return printedCost.WithGeneric(newGeneric);
    }

    /// <summary>
    /// Convenience: enumerate cards in the caster's hand that include
    /// the required colour AND are not the spell being cast. Mirrors
    /// <see cref="ImproviseAdditionalCost.AvailableArtifacts"/> and
    /// <see cref="ConvokeAdditionalCost.AvailableCreatures"/>. Returned
    /// in hand-iteration order.
    /// </summary>
    public static IReadOnlyList<ICard> AvailableHandCards(
        Player caster, ICard source, ManaColor requiredColor)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        if (source == null) throw new ArgumentNullException(nameof(source));

        return caster.Zones.Hand.GetCards()
            .Where(c => !ReferenceEquals(c, source)
                        && CardColors.GetColors(c).Contains(requiredColor))
            .ToList();
    }
}
