using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Psychotic Episode (Shadowmoor, {1}{B}{B}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Target player reveals their hand and the top card of their library. You
///    choose a card revealed this way. That player puts the chosen card on the
///    bottom of their library."
///   "Madness {1}{B}"
///
/// ## Shape — reveal hand + top-of-library, the OPPONENT chooses, bottom the pick
///
/// A <see cref="DespiseFactory"/>-style reveal-and-choose, but with three twists
/// that no existing primitive composed:
///
/// <list type="number">
///   <item><b>Target is a PLAYER</b> (CR 115 — "target player", not "target
///   opponent"). It can be any player, including the caster — though in
///   practice it's pointed at an opponent.</item>
///   <item><b>Reveal pile = hand + the top card of the target's library</b>
///   (CR 701.16). Both zones become public and BOTH are eligible picks; the
///   pick can be ANY card (no type filter).</item>
///   <item><b>The CHOOSER is the spell's controller</b> — i.e. the TARGET
///   player's opponent (CR 608.2g — "you" on a spell is its controller). This
///   is the cross-player choice the deferral called out: we resolve the pick
///   through the CONTROLLER's agent (<see cref="IPlayerAgent.ChooseFromHandAsync"/>,
///   reused as a general "pick one from a candidate list" prompt over the
///   combined reveal pile), not the target player's. The chosen card then
///   moves to the BOTTOM of the target player's library
///   (<see cref="IZone.AddCard"/> appends = bottom; CR 701.21).</item>
/// </list>
///
/// Reuses <see cref="RevealHelper.RevealHand"/> for the hand reveal +
/// <see cref="CardRevealedEvent"/> for the top-of-library reveal, then a single
/// agent pick over hand ∪ {top} and a hand-or-library → library-bottom move.
///
/// Madness {1}{B} is intrinsic via
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> (the discard funnel routes
/// the card to exile + offers the madness cast) — no per-card wiring needed.
///
/// ## Prod resolution path
///
/// As with the rest of the Duress family, the LIVE cast resolves this card
/// through the matching oracle-text template
/// (<see cref="RevealHandTopBottomChosenTemplate"/>), reached by
/// <c>OracleSpellBinder</c> at cast time. This factory supplies the card's
/// IDENTITY (so <c>IsImplemented</c> flips on) and a full-fidelity,
/// agent-driven <see cref="BuildSpellDefinition"/> used by unit tests.
/// </summary>
[CardName("Psychotic Episode")]
public static class PsychoticEpisodeFactory
{
    public const string CardName = "Psychotic Episode";
    public const string PrintedManaCost = "{1}{B}{B}";

    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the reveal-hand-and-top → CONTROLLER picks → bottom-of-library
    /// <see cref="SpellDefinition"/>. Single 1..1 "target player" request.
    /// </summary>
    /// <param name="resolver">Target resolver (chosen target → live game
    /// object).</param>
    /// <param name="chooserAgent">The CONTROLLER's agent — the player who makes
    /// the choice (the target player's opponent, per CR 608.2g). When null the
    /// pick falls back deterministically to the first revealed card (hand-order,
    /// then the library top).</param>
    /// <param name="eventBus">Optional event bus for the hand +
    /// top-of-library <see cref="CardRevealedEvent"/>s. No-op when null.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver,
        IPlayerAgent? chooserAgent,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        "Psychotic Episode: reveal hand + top of library → controller picks → bottom of library",
                        () => Resolve(raw, chooserAgent, eventBus)),
                };
            });
    }

    /// <summary>
    /// Shared resolution body (used by both the factory
    /// <see cref="BuildSpellDefinition"/> and the prod
    /// <see cref="RevealHandTopBottomChosenTemplate"/>). The
    /// <paramref name="chooserAgent"/> is the SPELL CONTROLLER's agent — the
    /// cross-player choice over the target player's revealed cards.
    /// </summary>
    internal static void Resolve(object? target, IPlayerAgent? chooserAgent, IEventBus? eventBus)
    {
        // CR 608.2b — a single illegal target → the spell does nothing.
        if (target is not Player victim) return;

        // CR 701.16 — "Target player reveals their hand and the top card of
        // their library." Reveal the whole hand…
        RevealHelper.RevealHand(eventBus, victim, CardName);

        // …and the top card of the library (library index 0 is the top).
        var top = victim.Zones.Library.GetCards().FirstOrDefault();
        if (top is not null)
        {
            eventBus?.Publish(new CardRevealedEvent(top, victim, ZoneType.Library, CardName));
        }

        // CR 700.2 — "You choose a card revealed this way." The candidate pile
        // is the hand ∪ {top of library}; any card qualifies (no filter).
        var candidates = victim.Zones.Hand.GetCards().ToList();
        if (top is not null) candidates.Add(top);
        if (candidates.Count == 0) return;

        // CR 608.2g — "you" = the spell's CONTROLLER (the target player's
        // opponent), so the choice runs through the CONTROLLER's agent, not
        // the target player's. Deterministic first-revealed fallback when no
        // agent is supplied (hand-order, then the library top).
        ICard? pick = candidates[0];
        if (chooserAgent != null)
        {
            var chosen = chooserAgent
                .ChooseFromHandAsync(victim, candidates, BotIntent.HandHate)
                .GetAwaiter().GetResult();
            // Guard a misbehaving agent: the pick must be one of the revealed
            // cards still owned by the target and still in hand or on top of
            // library.
            if (chosen != null
                && candidates.Contains(chosen)
                && ReferenceEquals(chosen.Owner, victim)
                && (chosen.Zone == ZoneType.Hand || chosen.Zone == ZoneType.Library))
            {
                pick = chosen;
            }
        }

        // CR 701.21 — "That player puts the chosen card on the bottom of their
        // library." Pull it from wherever it was revealed (hand or library
        // top) and append to the library (AddCard appends = bottom).
        if (pick.Zone == ZoneType.Hand)
        {
            victim.Zones.Hand.RemoveCard(pick);
        }
        else
        {
            victim.Zones.Library.RemoveCard(pick);
        }
        victim.Zones.Library.AddCard(pick);
        pick.SetZone(ZoneType.Library);
    }
}
