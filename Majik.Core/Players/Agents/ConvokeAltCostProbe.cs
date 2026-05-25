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
/// CR 702.51 — Convoke. Surfaces a <see cref="ConvokeAdditionalCost"/>-wrapped
/// alternative-cost candidate for the bot's spell-cast enumeration. A Convoke
/// spell is identified by a <see cref="KeywordAbility"/> labeled "Convoke" on
/// the card (the shipped factory marker — see
/// <see cref="Majik.Core.CardData.Factories.ChordOfCallingFactory"/>).
///
/// <para>Mirrors <see cref="ImproviseAltCostProbe"/>'s shape: it yields a
/// single "max convoke" candidate (tap as many controlled untapped creatures
/// as the spell has total mana value, capped at the available pool) so the
/// bot's EV evaluator sees the post-convoke effective cost. The wrapper rides
/// on the <see cref="IAlternativeCost"/> probe rail because that's the
/// existing bot-discovery surface; the probe yields a
/// <see cref="ConvokeAlternativeCost"/> shim whose
/// <see cref="ConvokeAlternativeCost.AdditionalCost"/> property exposes the
/// underlying <see cref="ConvokeAdditionalCost"/> the cast flow actually
/// consumes via the additional-cost loop.</para>
///
/// <para>Probe-level filtering:
///   * From-hand owner gate (the spell must be the caster's, in hand).
///   * Skips cards with no payable pips in their printed cost (purely-X
///     spells with no fixed pips have nothing to reduce until X is bound).
///   * Skips when the caster controls no untapped creatures.</para>
///
/// <para>Selection policy: by default the probe emits a SINGLE candidate
/// that taps as many creatures as the spell has total pip count (generic +
/// coloured). Callers can swap a richer <see cref="ChoiceStrategy"/> to
/// preserve combat-relevant creatures (would-attackers, blockers,
/// activated-ability sources).</para>
///
/// <para>The selection is purely advisory — actual cast-time convoke
/// payment goes through <see cref="Majik.Core.Game.SpellCastFlow"/>'s
/// additional-cost loop with the underlying
/// <see cref="ConvokeAdditionalCost"/>, whose
/// <see cref="ConvokeAdditionalCost.Pay"/> taps the creatures.</para>
/// </summary>
public sealed class ConvokeAltCostProbe : IAlternativeCostProbe
{
    /// <summary>
    /// Strategy for picking which controlled untapped creatures to tap.
    /// Receives the available pool and the maximum count (= the spell's
    /// payable pip count); returns the selected creatures. Default: take
    /// the FIRST <c>maxCount</c> (deterministic, matches a "max convoke"
    /// policy).
    /// </summary>
    public delegate IReadOnlyList<Creature> ChoiceStrategy(
        IReadOnlyList<Creature> available, int maxCount);

    private readonly Func<ICard, bool> _isConvokeCard;
    private readonly ChoiceStrategy _chooser;

    public ConvokeAltCostProbe(
        Func<ICard, bool>? isConvokeCard = null,
        ChoiceStrategy? chooser = null)
    {
        _isConvokeCard = isConvokeCard ?? DefaultIsConvokeCard;
        _chooser = chooser ?? DefaultChooser;
    }

    public IEnumerable<IAlternativeCost> CandidatesFor(ICard card, Player caster, GameContext ctx)
    {
        // From-hand owner gate — convoke is paid on cast, so the spell
        // needs to be in the caster's hand when we surface the option.
        if (card.Zone != ZoneType.Hand) yield break;
        if (!ReferenceEquals(card.Owner, caster)) yield break;

        if (!_isConvokeCard(card)) yield break;

        var printed = ManaCost.Parse(card.ManaCost ?? string.Empty);

        // CR 702.51 — convoke reduces generic OR coloured pips. Count all
        // payable pips (generic + coloured). X-only spells with no fixed
        // pips at probe time get a zero-pip count and are skipped here;
        // the cast-flow path still applies convoke once X is announced.
        var pipBudget = printed.Generic
            + printed.White + printed.Blue + printed.Black
            + printed.Red + printed.Green;
        if (pipBudget <= 0) yield break;

        var pool = ConvokeAdditionalCost.AvailableCreatures(caster);
        if (pool.Count == 0) yield break;

        var max = Math.Min(pipBudget, pool.Count);
        var chosen = _chooser(pool, max);
        if (chosen == null || chosen.Count == 0) yield break;

        yield return new ConvokeAlternativeCost(card, printed, chosen);
    }

    /// <summary>
    /// Built-in detector: scans the card's abilities for a
    /// <see cref="KeywordAbility"/> whose <see cref="KeywordAbility.Keyword"/>
    /// is "Convoke" (case-insensitive). All shipped convoke factories
    /// attach this marker (Chord of Calling; future Devoted Druid, Knight
    /// of New Benalia, etc.).
    /// </summary>
    public static bool DefaultIsConvokeCard(ICard card)
    {
        if (card == null) return false;
        return card.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Convoke", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Default selection: take the first <paramref name="maxCount"/>
    /// available creatures (battlefield-iteration order). Deterministic; the
    /// bot doesn't yet preserve combat-relevant creatures. Callers wiring
    /// richer policies should supply a custom chooser.</summary>
    public static IReadOnlyList<Creature> DefaultChooser(
        IReadOnlyList<Creature> available, int maxCount)
    {
        if (maxCount <= 0 || available.Count == 0)
        {
            return Array.Empty<Creature>();
        }
        var take = Math.Min(maxCount, available.Count);
        return available.Take(take).ToList();
    }
}
