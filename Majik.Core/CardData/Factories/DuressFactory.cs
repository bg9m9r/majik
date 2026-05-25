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
/// Named-card factory for Duress (Urza's Saga, {B}).
///
/// Sorcery. Oracle text:
///   "Target opponent reveals their hand. You choose a noncreature, nonland
///    card from it. That player discards that card."
///
/// ## Why a dedicated factory
/// <see cref="RevealHandThenDiscardTemplate"/> binds the printed text
/// generically but documents that its v1 stub silently drops the type
/// filter and picks the first non-land card regardless — so under the
/// template path Duress would happily take a creature out of the
/// opponent's hand, contradicting the printed restriction. The dedicated
/// factory enforces the noncreature + nonland filter at the candidate
/// gathering step (CR 701.16 — "if no card can be chosen, this part of
/// the effect doesn't happen") and threads the caster's
/// <see cref="IPlayerAgent"/> via <see cref="AgentRegistry"/> so the
/// chooser actually picks (instead of always taking the first match).
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {B}, owner / controller.
/// - <see cref="BuildSpellDefinition"/> declares one 1..1 "target
///   opponent" <see cref="TargetRequest"/> with a live candidate gatherer
///   that surfaces the caster's opponents only (CR 102.2 — "opponent"
///   excludes the caster themselves).
/// - Resolution (CR 701.16):
///   1. Target opponent reveals their hand via
///      <see cref="RevealHelper.RevealHand"/>.
///   2. Caster's agent picks one noncreature, nonland card from the
///      revealed hand (BotIntent: <see cref="BotIntent.Discard"/>). The
///      candidate list is pre-filtered so the agent only sees legal
///      picks.
///   3. Target opponent discards that card — hand → graveyard.
/// - "Nothing in hand satisfies noncreature + nonland" → no-op
///   (CR 701.16 — "if no card can be chosen, this part of the effect
///   doesn't happen").
///
/// ## Deferred (v1 gaps)
/// - <b>Modal split cards / DFCs</b>: the filter inspects only the front
///   face / printed type — adventure-half / MDFC back-face shenanigans
///   (Brazen Borrower's Petty Theft on a sorcery slot, etc.) are not
///   yet considered for the noncreature gate. Same posture as
///   <see cref="InquisitionOfKozilekFactory"/>.
/// </summary>
[CardName("Duress")]
public static class DuressFactory
{
    public const string CardName = "Duress";
    public const string PrintedManaCost = "{B}";

    public const string OracleText =
        "Target opponent reveals their hand. You choose a noncreature, nonland card from it. That player discards that card.";

    /// <summary>
    /// Build a Duress sorcery owned by <paramref name="owner"/>. Card
    /// shape only — the resolve-time target request + reveal-and-discard
    /// effect is built on demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Duress is cast.
    /// Single 1..1 "target opponent" request; on resolution the target
    /// reveals their hand, the caster's agent picks a noncreature nonland
    /// card, and the target discards it.
    /// </summary>
    /// <param name="caster">Cast-time controller — used to publish the
    /// reveal event reason, look up the chooser agent, and filter the
    /// candidate-opponent set.</param>
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
                    "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Discard,
                    // CR 102.2 — "opponent" excludes the caster.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, caster))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: reveal + agent-pick noncreature, nonland discard", () =>
                    {
                        // CR 608.2b — illegal-target check.
                        if (raw is not Player victim) return;

                        // CR 701.16 — "Target opponent reveals their hand."
                        RevealHelper.RevealHand(eventBus, victim, CardName);

                        // CR 701.16 — "You choose a noncreature, nonland card."
                        var candidates = victim.Zones.Hand.GetCards()
                            .Where(c => !c.HasType(CardType.Land)
                                        && !c.HasType(CardType.Creature))
                            .ToList();
                        if (candidates.Count == 0) return;

                        // Agent path: ask the caster which card to take.
                        // No agent → first eligible (deterministic v1).
                        var agent = AgentRegistry.Get(caster);
                        ICard? pick = agent is not null
                            ? agent.ChooseFromHandAsync(caster, candidates, BotIntent.Discard)
                                .GetAwaiter().GetResult()
                            : candidates[0];

                        if (pick is null) return;
                        // Sanity — pick must still be in target's hand.
                        if (pick.Zone != ZoneType.Hand) return;
                        if (!ReferenceEquals(pick.Owner, victim)) return;

                        // CR 701.16 — discard: hand → graveyard.
                        victim.Zones.Hand.RemoveCard(pick);
                        victim.Zones.Graveyard.AddCard(pick);
                        pick.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
