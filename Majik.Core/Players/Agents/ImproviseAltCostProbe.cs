using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Players.Agents;

/// <summary>
/// CR 702.127 — Improvise. Surfaces an
/// <see cref="ImproviseAdditionalCost"/>-wrapped alternative-cost
/// candidate for the bot's spell-cast enumeration. An Improvise spell is
/// identified by a <see cref="KeywordAbility"/> labeled "Improvise" on the
/// card (the shipped factory marker — see
/// <see cref="Majik.Core.CardData.Factories.KappaCannoneerFactory"/>).
///
/// <para>Mirrors <see cref="DelveAltCostProbe"/>'s shape: it yields a single
/// "max improvise" candidate (tap as many controlled untapped artifacts as
/// the spell has generic pips) so the bot's EV evaluator sees the
/// post-improvise effective cost (e.g. "Kappa Cannoneer for {U} instead of
/// {5}{U}" with five tapped artifacts). The wrapper rides on the
/// <see cref="IAlternativeCost"/> probe rail because that's the existing
/// bot-discovery surface; concretely the probe yields an
/// <see cref="ImproviseAlternativeCost"/> shim whose
/// <see cref="ImproviseAlternativeCost.AdditionalCost"/> property exposes
/// the underlying <see cref="ImproviseAdditionalCost"/> the cast flow
/// actually consumes via the additional-cost loop. This keeps the bot's
/// alt-cost iterator surface uniform (Pitch / Delve / Overload / Improvise
/// all behave the same way at the discovery level) without inventing a new
/// parallel API.</para>
///
/// <para>Probe-level filtering:
///   * From-hand owner gate (the spell must be the caster's, in hand).
///   * Skips cards with no generic pips in their printed cost (nothing to
///     reduce — Improvise only swaps generic mana per CR 702.127).
///   * Skips when the caster controls no untapped artifacts.</para>
///
/// <para>Selection policy: by default the probe emits a SINGLE candidate
/// that taps as many artifacts as the spell has generic pips (the
/// "max improvise" pick — what a heuristic bot wants in practice: minimum
/// remaining mana). Callers can swap a richer
/// <see cref="ChoiceStrategy"/> to preserve specific artifact payoffs
/// (Urza's Saga construct counts, mana rocks the bot wants ready
/// post-cast, etc.).</para>
///
/// <para>The selection is purely advisory — actual cast-time improvise
/// payment goes through
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s additional-cost loop
/// with the underlying <see cref="ImproviseAdditionalCost"/>, whose
/// <see cref="ImproviseAdditionalCost.Pay"/> taps the artifacts.</para>
/// </summary>
public sealed class ImproviseAltCostProbe : IAlternativeCostProbe
{
    /// <summary>
    /// Strategy for picking which controlled untapped artifacts to tap.
    /// Receives the available pool and the maximum count (= the spell's
    /// generic pips); returns the selected artifacts. Default: take the
    /// FIRST <c>maxCount</c> (deterministic, matches a "max improvise"
    /// policy).
    /// </summary>
    public delegate IReadOnlyList<Permanent> ChoiceStrategy(
        IReadOnlyList<Permanent> available, int maxCount);

    private readonly Func<ICard, bool> _isImproviseCard;
    private readonly ChoiceStrategy _chooser;

    public ImproviseAltCostProbe(
        Func<ICard, bool>? isImproviseCard = null,
        ChoiceStrategy? chooser = null)
    {
        _isImproviseCard = isImproviseCard ?? DefaultIsImproviseCard;
        _chooser = chooser ?? DefaultChooser;
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        // From-hand owner gate — improvise is paid on cast, so the spell
        // needs to be in the caster's hand when we surface the option.
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        if (!_isImproviseCard(card)) yield break;

        var printed = ManaCost.Parse(card.ManaCost ?? string.Empty);
        // CR 702.127 — improvise reduces ONLY generic pips. If the spell
        // has none, nothing to do.
        if (printed.Generic <= 0) yield break;

        var pool = ImproviseAdditionalCost.AvailableArtifacts(caster);
        if (pool.Count == 0) yield break;

        var max = Math.Min(printed.Generic, pool.Count);
        var chosen = _chooser(pool, max);
        if (chosen == null || chosen.Count == 0) yield break;

        yield return new ImproviseAlternativeCost(card, printed, chosen);
    }

    /// <summary>
    /// Built-in detector: scans the card's abilities for a
    /// <see cref="KeywordAbility"/> whose <see cref="KeywordAbility.Keyword"/>
    /// is "Improvise" (case-insensitive). All shipped improvise factories
    /// attach this marker (Kappa Cannoneer; future Reverse Engineer,
    /// Maverick Thopterist, etc.).
    /// </summary>
    public static bool DefaultIsImproviseCard(ICard card)
    {
        if (card == null) return false;
        return card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Improvise", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Default selection: take the first <paramref name="maxCount"/>
    /// available artifacts (battlefield-iteration order). Deterministic;
    /// the bot doesn't yet preserve specific artifact payoffs (Urza's Saga
    /// construct counters, ramp rocks). Callers wiring richer policies
    /// should supply a custom chooser.</summary>
    public static IReadOnlyList<Permanent> DefaultChooser(
        IReadOnlyList<Permanent> available, int maxCount)
    {
        if (maxCount <= 0 || available.Count == 0)
        {
            return Array.Empty<Permanent>();
        }
        var take = Math.Min(maxCount, available.Count);
        return available.Take(take).ToList();
    }
}

/// <summary>
/// Discovery-surface adapter that wraps an <see cref="ImproviseAdditionalCost"/>
/// as an <see cref="IAlternativeCost"/> so the bot's existing alt-cost rail
/// (<see cref="AlternativeCostProbeRegistry"/>) can stream Improvise
/// candidates alongside Pitch / Delve / Overload / Escape without inventing
/// a parallel API.
///
/// <para>This is purely a SHIM — at cast time the caller MUST unpack
/// <see cref="AdditionalCost"/> and route it through
/// <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c>
/// parameter (Improvise is an additional cost, not a true alternative —
/// CR 702.127). The shim's <see cref="AlternativeManaCost"/> reports the
/// post-improvise effective cost so EV-style evaluators can rank casting
/// options correctly; its <see cref="OnResolved"/> is a no-op because the
/// actual tap-side-effect ran during the additional-cost loop at cast
/// time.</para>
/// </summary>
public sealed class ImproviseAlternativeCost : IAlternativeCost
{
    /// <summary>The underlying additional cost — this is what the cast
    /// flow consumes via its additionalCosts loop. The bot pulls it via
    /// this property when bidding the candidate.</summary>
    public ImproviseAdditionalCost AdditionalCost { get; }

    /// <summary>Printed mana cost of the spell. Kept for description /
    /// audit; the reduced cost is exposed via
    /// <see cref="AlternativeManaCost"/>.</summary>
    public ManaCost PrintedCost { get; }

    public ManaCost AlternativeManaCost { get; }

    public string Description =>
        $"Improvise — tap {AdditionalCost.ReductionAmount} artifact(s), pay {AlternativeManaCost}";

    public ImproviseAlternativeCost(ICard source, ManaCost printedCost, IReadOnlyList<Permanent> chosen)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        PrintedCost = printedCost ?? throw new ArgumentNullException(nameof(printedCost));
        if (chosen == null) throw new ArgumentNullException(nameof(chosen));

        AdditionalCost = new ImproviseAdditionalCost(source, chosen);

        // CR 702.127 — only generic pips reduce; colored portion is
        // preserved. Mirrors DelveAlternativeCost.ctor.
        var reduction = Math.Min(AdditionalCost.ReductionAmount, printedCost.Generic);
        AlternativeManaCost = printedCost.WithGeneric(printedCost.Generic - reduction);
    }

    public bool CanCastFor(ICard card, Player caster)
    {
        if (card == null || caster == null) return false;
        if (card.Zone != ZoneType.Hand) return false;
        if (!ReferenceEquals(card.Owner, caster)) return false;
        return AdditionalCost.CanPay(caster);
    }

    /// <summary>
    /// No-op. The improvise tap-side-effect was already paid as part of
    /// the additional-cost loop in <see cref="Majik.Core.Game.SpellCastFlow"/>.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        // Intentionally empty — see class xmldoc.
    }
}
