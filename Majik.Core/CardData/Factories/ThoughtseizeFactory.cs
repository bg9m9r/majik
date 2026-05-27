using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thoughtseize (Lorwyn / Theros / many reprints, {B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Target player reveals their hand. You choose a nonland card from
///    it. That player discards that card. You lose 2 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target
///   player" request. On resolution:
///     1. <b>Reveal</b> (CR 701.16) — every card in the target's hand
///        becomes public via
///        <see cref="RevealHelper.RevealHand"/>, publishing one
///        <see cref="CardRevealedEvent"/> per card so clients (portal)
///        can flash them.
///     2. <b>Caster picks a nonland card</b> (CR 700.2 — choice made
///        on resolution since "you choose" follows the reveal). The
///        candidate list is pre-filtered to nonland cards in the
///        target's hand; the caster's
///        <see cref="IPlayerAgent.ChooseFromHandAsync"/> drives the
///        pick (intent = <see cref="BotIntent.HandHate"/>). A null /
///        out-of-set / land pick falls back deterministically to the
///        first nonland card (parity with
///        <see cref="ThoughtKnotSeerFactory"/>'s ETB exile pick).
///     3. <b>Discard</b> (CR 701.16 — "discards that card") — the
///        chosen card moves Hand → Graveyard.
///     4. <b>Caster loses 2 life</b> (CR 119.3) — unconditionally, as
///        part of the same resolution. Lands-only hand still triggers
///        the life loss (the printed text doesn't gate it on a
///        successful discard, just like Inquisition of Kozilek's
///        "discards that card" branch resolves to a no-op when the
///        condition fails but the spell still resolves).
///
/// ## Why a named factory when a template already exists
///
/// <see cref="ThoughtseizePatternTemplate"/> covers the oracle-text
/// pattern with a deterministic v1 pick (first nonland card). The
/// named factory upgrades the pick to agent-driven
/// (<see cref="IPlayerAgent.ChooseFromHandAsync"/>) — heuristic bot can
/// now prefer the highest-value nonland card instead of
/// alphabetical-first. Source-generated dispatch
/// (<see cref="NamedCardFactory"/>) prefers the factory shape; the
/// template remains as fallback for foreign-printed Thoughtseize-shaped
/// oracle text not yet listed by name.
///
/// ## Deferred (v1 gaps)
/// - <b>Forced reveal-then-pick prompt UI</b>: the agent gets the
///   nonland subset directly. A future revision could surface the full
///   revealed hand to the picker UI (Magic Online's posture) and let
///   the picker pick from the full hand while the engine validates the
///   pick against the nonland filter.
/// </summary>
[CardName("Thoughtseize")]
public static class ThoughtseizeFactory
{
    public const string CardName = "Thoughtseize";
    public const string PrintedManaCost = "{B}";

    /// <summary>CR 119.3 — printed life cost on resolution.</summary>
    public const int LifeLoss = 2;

    /// <summary>
    /// Build a Thoughtseize sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time
    /// target request + reveal/pick/discard/life-loss body is built on
    /// demand via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Thoughtseize is
    /// cast. Single 1..1 "target player" request; on resolution the
    /// target reveals their hand, the caster picks a nonland card via
    /// <paramref name="agent"/>, that card is discarded (Hand →
    /// Graveyard), and the caster loses
    /// <see cref="LifeLoss"/> life.
    /// </summary>
    /// <param name="caster">Cast-time controller. Used to surface the
    /// reveal event with a stable reason string, host the agent pick,
    /// and apply the 2-life loss on resolution.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="agent">Optional player-agent used for the nonland
    /// pick. When null, the pick falls back deterministically to the
    /// first nonland card in the revealed hand (matches the legacy
    /// <see cref="ThoughtseizePatternTemplate"/> posture and the
    /// Grief / Liliana of the Veil discard fallback).</param>
    /// <param name="eventBus">Optional event bus for publishing
    /// <see cref="CardRevealedEvent"/> per card in the revealed hand.
    /// No-op when null (test fixtures may bind without a bus).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        IPlayerAgent? agent,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(caster);
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
                    new Effect("Thoughtseize: reveal → caster picks nonland → discard → 2 life", () =>
                    {
                        // CR 608.2b — illegal-target check (target player
                        // left the game, etc.). The cast-flow's own pass
                        // catches most of these; guard defensively. Even
                        // on an illegal target the caster's life-loss
                        // clause does NOT trigger — the spell is treated
                        // as "does nothing" per the single-target rule
                        // (CR 608.2b final clause). Parity with
                        // Lightning Helix's fizzle posture.
                        if (raw is not Player victim) return;

                        // CR 701.16 — "Target player reveals their hand."
                        // RevealHelper publishes one CardRevealedEvent
                        // per card so portal clients can flash the hand.
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 700.2 — "You choose a nonland card from
                        // it." Pre-filter to nonland cards; ask the
                        // agent (intent = HandHate) for the pick.
                        // Deterministic fallback when the agent is
                        // missing / returns an illegal pick = first
                        // nonland card (matches ThoughtKnotSeer ETB
                        // exile fallback).
                        var nonland = victim.Zones.Hand.GetCards()
                            .Where(c => !c.HasType(CardType.Land))
                            .ToList();

                        ICard? pick = null;
                        if (nonland.Count > 0)
                        {
                            if (agent != null)
                            {
                                pick = agent
                                    .ChooseFromHandAsync(victim, nonland, BotIntent.HandHate)
                                    .GetAwaiter().GetResult();
                                if (pick == null
                                    || pick.Zone != ZoneType.Hand
                                    || pick.HasType(CardType.Land)
                                    || !ReferenceEquals(pick.Owner, victim))
                                {
                                    pick = nonland[0];
                                }
                            }
                            else
                            {
                                pick = nonland[0];
                            }
                        }

                        // CR 701.16 — "That player discards that card."
                        // No-op when the hand was empty or lands-only
                        // (Thoughtseize against a topdecked land hand is
                        // a real game state). The life-loss clause
                        // below still fires.
                        if (pick != null)
                        {
                            victim.Zones.Hand.RemoveCard(pick);
                            victim.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }

                        // CR 119.3 — "You lose 2 life." Always runs as
                        // part of the same resolution, regardless of
                        // whether a card was actually discarded.
                        caster.LoseLife(LifeLoss);
                    }),
                };
            });
    }
}
