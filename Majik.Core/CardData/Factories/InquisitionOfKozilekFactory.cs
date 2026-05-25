using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Inquisition of Kozilek (Rise of the Eldrazi, {B}).
///
/// Sorcery. Oracle text:
///   "Target player reveals their hand. You choose a nonland card from it
///    with mana value 3 or less. That player discards that card."
///
/// ## Why a dedicated factory
/// <see cref="InquisitionOfKozilekPatternTemplate"/> already binds the
/// printed text generically (regex with a captured mv cap), but the data-
/// driven path picks the first eligible card deterministically rather than
/// asking the caster's agent which one to take. The named factory threads
/// the caster's <see cref="IPlayerAgent"/> via
/// <see cref="AgentRegistry"/>, so the heuristic bot + future portal client
/// pick the most painful card (CR 701.16 — "you choose"). When no agent is
/// registered the factory falls back to the same first-eligible behaviour
/// as the template so unit fixtures keep their deterministic shape.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}, owner / controller.
/// - <see cref="BuildSpellDefinition"/> declares one 1..1 "target player"
///   <see cref="TargetRequest"/> with a live candidate gatherer that
///   surfaces the caster's opponents only (CR 115.6 — "target opponent" is
///   actually "target player" on Inquisition's printed text, but the
///   self-pick is dominated for the heuristic).
/// - Resolution (CR 701.16):
///   1. Target player reveals their hand — one
///      <see cref="CardRevealedEvent"/> per card via
///      <see cref="RevealHelper.RevealHand"/>.
///   2. Caster's agent picks one nonland card from the revealed hand with
///      <see cref="Card.ManaCostValue"/>.TotalValue &lt;= 3 (BotIntent:
///      <see cref="BotIntent.Discard"/>). The candidate list is pre-
///      filtered so the agent only sees legal picks.
///   3. Target player discards that card (CR 701.16) — hand → graveyard.
/// - "Nothing in hand satisfies nonland + mv ≤ 3" → no-op (CR 701.16 —
///   "if no card can be chosen, this part of the effect doesn't happen").
///
/// ## Deferred (v1 gaps)
/// - <b>No life loss</b> — Inquisition's defining differentiator vs.
///   Thoughtseize is implicit (no clause). Nothing to defer.
/// - <b>Self-target</b>: the printed text allows targeting yourself. The
///   gatherer enumerates all players; the caster scoring path (handled by
///   <see cref="HeuristicBotAgent"/>) ranks opponents higher for Discard
///   intent. Self-target still resolves correctly if forced.
/// </summary>
[CardName("Inquisition of Kozilek")]
public static class InquisitionOfKozilekFactory
{
    public const string CardName = "Inquisition of Kozilek";
    public const string PrintedManaCost = "{B}";
    public const int ManaValueCap = 3;

    public const string OracleText =
        "Target player reveals their hand. You choose a nonland card from it with mana value 3 or less. That player discards that card.";

    /// <summary>
    /// Build an Inquisition of Kozilek sorcery owned by
    /// <paramref name="owner"/>. Card shape only — the resolve-time target
    /// request + reveal-and-discard effect is built on demand via
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
    /// Build the <see cref="SpellDefinition"/> used when Inquisition is
    /// cast. Single 1..1 "target player" request; on resolution the
    /// target reveals their hand, the caster's agent picks a nonland card
    /// with mana value ≤ 3, and the target discards it.
    /// </summary>
    /// <param name="caster">Cast-time controller — used to publish the
    /// reveal event reason and look up the chooser agent.</param>
    /// <param name="resolver">Target resolver (chosen target → live game
    /// object).</param>
    /// <param name="eventBus">Optional event bus for publishing
    /// <see cref="CardRevealedEvent"/> per card in the revealed hand.
    /// No-op when null (unit fixtures may bind without a bus).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Discard,
                    CandidateGatherer: ctx => ctx.AllPlayers.Cast<object>().ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: reveal + agent-pick discard mv≤{ManaValueCap}", () =>
                    {
                        // CR 608.2b — illegal-target check.
                        if (raw is not Player victim) return;

                        // CR 701.16 — "Target player reveals their hand."
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 701.16 — "You choose a nonland card from it
                        // with mana value 3 or less." Pre-filter so the
                        // agent only sees legal picks (mirrors Sneak
                        // Attack's candidate filtering).
                        var candidates = victim.Zones.Hand.GetCards()
                            .OfType<Card>()
                            .Where(c => !c.HasType(CardType.Land)
                                        && c.ManaCostValue.TotalValue <= ManaValueCap)
                            .Cast<ICard>()
                            .ToList();
                        if (candidates.Count == 0) return;

                        // Agent path: ask the caster which card to take.
                        // No agent → first eligible (deterministic v1, same
                        // as the InquisitionOfKozilekPatternTemplate stub).
                        var agent = AgentRegistry.Get(caster);
                        ICard? pick = agent is not null
                            ? agent.ChooseFromHandAsync(caster, candidates, BotIntent.Discard)
                                .GetAwaiter().GetResult()
                            : candidates[0];

                        if (pick is not Card chosen) return;
                        // Sanity — pick must still be in target's hand.
                        if (chosen.Zone != ZoneType.Hand) return;
                        if (!ReferenceEquals(chosen.Owner, victim)) return;

                        // CR 701.16 — discard: hand → graveyard.
                        victim.Zones.Hand.RemoveCard(chosen);
                        victim.Zones.Graveyard.AddCard(chosen);
                        chosen.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
