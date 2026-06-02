using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Harsh Scrutiny (Amonkhet, {B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Target opponent reveals their hand. You choose a creature card from
///    it. That player discards that card. Scry 1."
///
/// ## Implemented (v1)
/// A <see cref="DespiseFactory"/>-shape targeted discard (reveal → caster
/// picks a creature card → opponent discards it) with a Scry 1 tail.
/// Resolve-time <see cref="SpellDefinition"/> (via
/// <see cref="BuildSpellDefinition"/>) declares one 1..1 "target opponent"
/// request. On resolution:
///   1. <b>Reveal</b> (CR 701.16) — the target's whole hand becomes public
///      via <see cref="RevealHelper.RevealHand"/>, publishing one
///      <see cref="CardRevealedEvent"/> per card so portal clients can
///      flash them.
///   2. <b>Caster picks a creature card</b> (CR 700.2 — the choice is made
///      on resolution, after the reveal). Candidate list is pre-filtered to
///      creature cards in the target's hand; the caster's
///      <see cref="IPlayerAgent.ChooseFromHandAsync"/> drives the pick
///      (intent = <see cref="BotIntent.HandHate"/>). A null / out-of-set /
///      non-creature pick falls back deterministically to the first creature
///      card (parity with <see cref="DespiseFactory"/>).
///   3. <b>Discard</b> (CR 701.16 — "discards that card") — the chosen card
///      moves Hand → Graveyard. No-op when the hand has no creature card
///      (a creature-less hand is a real game state); the Scry 1 below still
///      resolves.
///   4. <b>Scry 1</b> (CR 701.20) — the caster looks at the top card of
///      their library and may put it on the bottom. Decision is sourced
///      from the caster's <see cref="IPlayerAgent.ChooseScryDecisionAsync"/>
///      (passed in as <paramref name="agent"/>); with no agent the peeked
///      card is sent to the bottom (pre-agent default, matching
///      <see cref="OptFactory"/> / Preordain).
///
/// ## Deferred (v1 gaps)
/// - <b>Forced reveal-then-pick prompt UI</b>: the agent gets the creature
///   subset directly rather than the full revealed hand to pick from (the
///   engine validates the pick against the creature filter). Same posture
///   as <see cref="DespiseFactory"/> / <see cref="ThoughtseizeFactory"/>.
/// </summary>
[CardName("Harsh Scrutiny")]
public static class HarshScrutinyFactory
{
    public const string CardName = "Harsh Scrutiny";
    public const string PrintedManaCost = "{B}";

    /// <summary>CR 701.20 — the card scries 1 on resolution.</summary>
    public const int ScryAmount = 1;

    /// <summary>
    /// Build a Harsh Scrutiny sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — the resolve-time target
    /// request + reveal/pick/discard/scry body is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Harsh Scrutiny is
    /// cast. Single 1..1 "target opponent" request; on resolution the target
    /// reveals their hand, the caster picks a creature card via
    /// <paramref name="agent"/>, that card is discarded (Hand → Graveyard),
    /// and the caster scries 1.
    /// </summary>
    /// <param name="caster">Cast-time controller — the player who chooses the
    /// discard, and who scries 1 on resolution.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    /// <param name="agent">Optional player-agent. Drives the creature pick
    /// (intent <see cref="BotIntent.HandHate"/>) and the Scry 1 decision.
    /// When null, the pick falls back to the first creature card and the
    /// scry sends the peeked card to the bottom (pre-agent defaults — parity
    /// with <see cref="DespiseFactory"/> / <see cref="OptFactory"/>).</param>
    /// <param name="eventBus">Optional event bus for publishing
    /// <see cref="CardRevealedEvent"/> per revealed card. No-op when null.</param>
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
                new TargetRequest("target opponent", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Harsh Scrutiny: reveal → caster picks creature → discard → scry 1", () =>
                    {
                        // CR 608.2b — illegal-target check. The cast flow's
                        // own pass catches most of these; guard defensively.
                        // On an illegal target the spell does nothing,
                        // including the scry (single-target fizzle posture,
                        // parity with Thoughtseize / Lightning Helix).
                        if (raw is not Player victim) return;

                        // CR 701.16 — "Target opponent reveals their hand."
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 700.2 — "You choose a creature card from it."
                        // Pre-filter to creature cards; ask the agent (intent
                        // = HandHate) for the pick. Deterministic fallback
                        // when the agent is missing / returns an illegal pick
                        // = first creature card (matches Despise).
                        var creatures = victim.Zones.Hand.GetCards()
                            .Where(c => c.HasType(CardType.Creature))
                            .ToList();

                        ICard? pick = null;
                        if (creatures.Count > 0)
                        {
                            if (agent != null)
                            {
                                pick = agent
                                    .ChooseFromHandAsync(victim, creatures, BotIntent.HandHate)
                                    .GetAwaiter().GetResult();
                                if (pick == null
                                    || pick.Zone != ZoneType.Hand
                                    || !pick.HasType(CardType.Creature)
                                    || !ReferenceEquals(pick.Owner, victim))
                                {
                                    pick = creatures[0];
                                }
                            }
                            else
                            {
                                pick = creatures[0];
                            }
                        }

                        // CR 701.16 — "That player discards that card."
                        // No-op on a creature-less hand; the scry below
                        // still resolves.
                        if (pick != null)
                        {
                            victim.Zones.Hand.RemoveCard(pick);
                            victim.Zones.Graveyard.AddCard(pick);
                            pick.SetZone(ZoneType.Graveyard);
                        }

                        // CR 701.20 — "Scry 1." The caster looks at the top
                        // card and may bottom it. Decision via the agent's
                        // ChooseScryDecisionAsync (ctx may be null in a v1
                        // effect closure); with no agent the peeked card goes
                        // to the bottom (pre-agent default). Empty library →
                        // peek is empty → no-op.
                        var peeked = ScryAction.Peek(caster, ScryAmount);
                        if (peeked.Count > 0)
                        {
                            ScryAction.ScryDecision decision;
                            if (agent != null)
                            {
                                decision = agent
                                    .ChooseScryDecisionAsync(null, peeked)
                                    .GetAwaiter().GetResult();
                            }
                            else
                            {
                                decision = new ScryAction.ScryDecision(
                                    ToBottom: peeked.ToList(),
                                    TopOrder: Array.Empty<ICard>());
                            }
                            ScryAction.Apply(caster, peeked.Count, decision);
                        }
                    }),
                };
            });
    }
}
