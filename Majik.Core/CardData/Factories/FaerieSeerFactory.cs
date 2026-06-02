using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Faerie Seer (Modern Horizons 2, {U}).
///
/// Creature — Faerie Wizard 1/1. Oracle text (verified against Scryfall):
///   "Flying.
///    When this creature enters, scry 2."
///
/// ## Shape source
/// Card identity (name, {U}, 1/1, Creature — Faerie Wizard) is loaded from
/// <c>Majik.Core/CardData/Cards/faerie-seer.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="BorderlandRangerFactory"/>. The Flying keyword and the ETB
/// scry-2 trigger are attached in code below: the JSON ability schema does
/// not yet express keyword markers or a scry effect.
///
/// ## Implemented (v1)
/// - 1/1 Faerie Wizard (CR 205.3m) at {U}. Color identity blue (derived from
///   the {U} pip per CR 202.2c). Mana value 1 (CR 202.3).
/// - <b>Flying</b> (CR 702.9): <see cref="KeywordAbility"/> marker read by
///   <c>CombatAbilities.HasFlying</c> for evasion in the combat validator —
///   same wire-up shape as <see cref="CloudkinSeerFactory"/>.
/// - <b>ETB triggered ability</b> (CR 603.1 / CR 603.6a): "When this creature
///   enters, scry 2." Unconditional self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — no intervening-if clause
///   (CR 603.4 does not apply). Resolution runs the standard
///   <see cref="ScryAction"/> pipeline for N=2 (CR 701.20), consulting the
///   registered <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseScryDecisionAsync"/> when one is present;
///   otherwise it falls back to all-bottom — identical scry body to
///   <see cref="CharmingPrinceFactory"/>'s mode-0 / <see cref="PreordainFactory"/>.
///   The controller closure re-resolves at execute time so blink /
///   control-change scenarios scry for the correct player.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The ETB trigger is attached
///   to the card for shape inspection; not registered with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired. When
///   <paramref name="triggers"/> is supplied the ETB trigger is registered so
///   the relevant <c>CardMovedEvent</c> places it on the stack (CR 603.3).
/// </summary>
[CardName("Faerie Seer")]
public static class FaerieSeerFactory
{
    public const string CardName = "Faerie Seer";
    private const int ScryAmount = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("faerie-seer");

    /// <summary>
    /// Construct Faerie Seer with no live wiring. The ETB trigger is attached
    /// to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Faerie Seer with optional <see cref="TriggerManager"/> wiring.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so the relevant <c>CardMovedEvent</c> places it on the stack
    /// automatically (CR 603.3).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Flying (CR 702.9). Keyword marker — CombatAbilities.HasFlying
        // reads this for evasion in the combat validator. Same wire-up
        // shape as Cloudkin Seer.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, scry 2."
        // Unconditional self-ETB — no intervening-if (CR 603.4 does not
        // apply here). The controller closure re-resolves at execute time so
        // blink / control-change scenarios scry for the correct player.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: scry {ScryAmount} (when this creature enters)",
            ctx =>
            {
                var controller = card.Controller ?? owner;
                return ExecuteScryAsync(controller, ctx);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Scry 2 (CR 701.20). Look at the top two cards of the library; the
    /// registered agent (when present) decides how many go to the bottom and
    /// the order of the rest. Pre-agent default: all peeked cards to the
    /// bottom (same fallback as <see cref="CharmingPrinceFactory"/> mode 0 and
    /// <c>LibrarySpellFactory.ScryNSpell</c>). An empty / short library peeks
    /// up to N cards and is a clean no-op.
    /// </summary>
    private static async ValueTask ExecuteScryAsync(Player controller, ResolutionContext ctx)
    {
        var peeked = ScryAction.Peek(controller, ScryAmount);
        if (peeked.Count == 0) return;

        var agent = ctx.Agent ?? AgentRegistry.Get(controller);
        ScryAction.ScryDecision decision;
        if (agent != null)
        {
            decision = await agent.ChooseScryDecisionAsync(ctx.Game, peeked)
                .ConfigureAwait(false);
        }
        else
        {
            // Pre-agent default: all peeked cards to the bottom.
            decision = new ScryAction.ScryDecision(
                ToBottom: peeked.ToList(),
                TopOrder: Array.Empty<ICard>());
        }

        ScryAction.Apply(controller, peeked.Count, decision);
    }
}
