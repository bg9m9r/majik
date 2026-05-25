using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
// Improvise selects "artifacts you control" by card-type predicate
// (CR 109.5 + 702.127 — controller-side artifact type membership),
// NOT by the C# <see cref="Artifact"/> class — an Artifact Creature
// (e.g. Kappa Cannoneer itself) is a <see cref="Creature"/> instance
// with CardType.Artifact additively flagged via AddCardType. Selecting
// by Permanent + HasType(Artifact) captures both shapes.

namespace Majik.Core.Costs;

/// <summary>
/// CR 702.127 — Improvise. "Your artifacts can help cast this spell. Each
/// artifact you tap after you're done activating mana abilities pays for {1}."
///
/// <para>Improvise is the artifact analogue of Convoke (CR 702.51 — uses
/// creatures). It is technically a cost-modifier rather than a true
/// alternative cost: the COLOURED pips of the printed cost still have to be
/// paid normally; only the GENERIC portion may be substituted by tapping
/// artifacts the caster controls (one tap → {1}). CR 702.127a / CR 601.2g —
/// the tap is paid AFTER mana abilities resolve (a tapped artifact can't
/// also have been tapped for mana this cast).</para>
///
/// <para>Modeled as a value object: the caller (a player agent, a bot probe,
/// or a test) pre-selects untapped artifacts the caster controls and hands
/// the resulting <see cref="ImproviseAdditionalCost"/> to
/// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/> via the
/// <c>additionalCosts</c> list. The cast flow's CR 601.2f additional-cost
/// loop calls <see cref="CanPay"/> then <see cref="Pay"/>; <see cref="Pay"/>
/// taps the selected artifacts, and the cast-flow's mana-cost computation
/// subtracts <see cref="ReductionAmount"/> generic pips before prompting
/// for the remaining mana payment.</para>
///
/// <para>Mirror of <see cref="ConvokeAlternativeCost"/> (the alt-cost
/// shaped Convoke wrapper) and <see cref="DelveCost"/> (the canonical
/// cast-flow primitive for Delve). Improvise sits on the additional-cost
/// rail because Kappa Cannoneer-style cards print Improvise as an
/// always-available cost modifier rather than a replace-the-printed-cost
/// alternative; the cast flow consults the additional-cost list AFTER
/// merging the spell's <see cref="Spells.SpellDefinition.AdditionalCosts"/>
/// (so the cost gates the cast at CR 601.2g time), and the generic
/// reduction is then folded into the mana payment.</para>
///
/// <para>IPlayerAgent does not currently expose a "tap N artifacts" prompt
/// — tests and bots construct <see cref="ImproviseAdditionalCost"/> with
/// a deterministic selection, parallel to the
/// <see cref="DelveCost"/> agent-prompt deferral. The bot's
/// <see cref="Majik.Core.Players.Agents.ImproviseAltCostProbe"/> surfaces a
/// default "tap as many as the spell has generic pips" pick.</para>
///
/// <para>CR rule references: 702.127 (Improvise), 702.51 (Convoke
/// analogue), 605.1 (mana abilities resolve first), 117.7c (cost-reduction
/// floor), 601.2f (additional-cost timing).</para>
/// </summary>
public sealed class ImproviseAdditionalCost : IAdditionalCost
{
    /// <summary>The spell being cast with improvise.</summary>
    public ICard Source { get; }

    /// <summary>
    /// The artifacts the caster has selected to tap as Improvise
    /// contributions. Each contributes {1} of generic-mana reduction
    /// (CR 702.127). Order is preserved for diagnostics + future
    /// downstream effects that count "artifacts tapped for improvise".
    /// </summary>
    public IReadOnlyList<Permanent> Chosen { get; }

    /// <summary>
    /// Generic-mana reduction granted by tapping the chosen artifacts —
    /// equal to <c>Chosen.Count</c>. Always non-negative.
    /// </summary>
    public int ReductionAmount => Chosen.Count;

    public string Description =>
        Chosen.Count == 0
            ? "Improvise"
            : $"Improvise — tap {Chosen.Count} artifact(s) for {{{Chosen.Count}}}";

    public ImproviseAdditionalCost(ICard source, IReadOnlyList<Permanent> chosen)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        if (chosen == null) throw new ArgumentNullException(nameof(chosen));
        // Defensive copy — value object semantics; mirrors DelveCost.
        Chosen = chosen.ToList();
    }

    /// <summary>
    /// CR 702.127a — legality check. Each chosen artifact must be on the
    /// battlefield, controlled by the caster, untapped, and of card type
    /// <see cref="CardType.Artifact"/>. Duplicates are rejected (tapping
    /// the same permanent twice is nonsense; CR 605.1 / 118.12 — each cost
    /// is paid once).
    /// </summary>
    public bool CanPay(Player caster)
    {
        if (caster == null) return false;

        var seen = new HashSet<Permanent>(ReferenceEqualityComparer.Instance);
        foreach (var a in Chosen)
        {
            if (a == null) return false;
            if (a.Zone != ZoneType.Battlefield) return false;
            if (!ReferenceEquals(a.Controller, caster)) return false;
            if (!a.HasType(CardType.Artifact)) return false;
            if (a.IsTapped) return false;
            if (!seen.Add(a)) return false;
        }

        return true;
    }

    /// <summary>
    /// CR 702.127a + CR 605.1 — tap each chosen artifact. The cast flow
    /// has already settled mana-ability activations (the agent's
    /// ChooseManaSources prompt fires AFTER the additional-cost loop),
    /// so tapping here happens strictly after any tap-for-mana on the
    /// same artifacts could have occurred (CR 601.2g cost-payment order).
    ///
    /// Returns false on illegal selection. Already-paid artifacts (tapped
    /// mid-flight by an outside effect) are NOT untapped — the cost
    /// short-circuits and reports failure so the cast flow can rewind
    /// per CR 601.2g.
    /// </summary>
    public bool Pay(Player caster)
    {
        if (!CanPay(caster)) return false;

        foreach (var a in Chosen)
        {
            a.Tap();
        }

        return true;
    }

    /// <summary>
    /// Compute the post-improvise mana cost: reduce generic by
    /// <see cref="ReductionAmount"/>, floored at 0 (CR 117.7c). Other mana
    /// components (coloured, hybrid, phyrexian, X) are preserved verbatim —
    /// only generic pips can be paid by improvise per CR 702.127.
    /// </summary>
    public ManaCost ApplyTo(ManaCost printedCost)
    {
        if (printedCost == null) throw new ArgumentNullException(nameof(printedCost));
        var newGeneric = Math.Max(0, printedCost.Generic - ReductionAmount);
        return printedCost.WithGeneric(newGeneric);
    }

    /// <summary>
    /// Convenience: enumerate untapped artifacts the caster currently
    /// controls. Mirrors the bot's selection-source helper used by
    /// <see cref="Majik.Core.Players.Agents.ImproviseAltCostProbe"/>.
    /// Returned in battlefield-iteration order.
    /// </summary>
    public static IReadOnlyList<Permanent> AvailableArtifacts(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        // CR 702.127 + 109.5 — "your artifacts" = battlefield permanents
        // the caster controls with CardType.Artifact. Covers pure
        // Artifacts AND Artifact Creatures (Kappa Cannoneer is a
        // Creature with CardType.Artifact additively flagged).
        return caster.Zones.Battlefield.GetCards()
            .OfType<Permanent>()
            .Where(p => p.HasType(CardType.Artifact)
                        && !p.IsTapped
                        && ReferenceEquals(p.Controller, caster))
            .ToList();
    }
}
