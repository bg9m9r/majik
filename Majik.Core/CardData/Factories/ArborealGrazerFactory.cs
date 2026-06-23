using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arboreal Grazer (Ravnica Allegiance, {G}).
///
/// Creature — Sloth Beast 0/3 with Reach. Oracle text:
///   "Reach
///    When this creature enters, you may put a land card from your hand
///    onto the battlefield tapped."
///
/// ## Shape source
/// Card identity (name, {G}, 0/3, Creature — Sloth Beast, Reach) is loaded
/// from <c>Majik.Core/CardData/Cards/arboreal-grazer.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (Reach attaches as a
/// <see cref="KeywordAbility"/> marker from the JSON keywords line). The
/// single ETB triggered ability is attached in code below.
///
/// This combines the ETB-trigger shape of <see cref="FarhavenElfFactory"/>
/// with the land-from-hand resolution of <see cref="SakuraTribeScoutFactory"/>
/// (which pulls a land from hand rather than tutoring from library), enters
/// it tapped, and gates the whole thing behind the printed "you may".
///
/// ## Implemented (v1)
/// - 0/3 Sloth Beast, mana cost {G}, Reach (CR 702.9).
/// - <b>ETB trigger (CR 603.6a)</b>: "you may put a land card from your hand
///   onto the battlefield tapped." Consults the controller's
///   <see cref="IPlayerAgent"/> for the "you may" opt-in
///   (<see cref="IPlayerAgent.ChooseYesNoAsync"/>, intent
///   <see cref="BotIntent.Ramp"/>) and the which-land pick
///   (<see cref="IPlayerAgent.ChooseFromHandAsync"/>); no-agent fallback
///   auto-accepts and takes the first land in hand deterministically
///   (same posture as the Sakura-Tribe Scout family). Moves the land
///   Hand → Battlefield through <see cref="ZoneService"/> when supplied so
///   ETB triggers / replacements on the played land fire (CR 603.6a /
///   CR 614 — e.g. shock-land "pay 2 life?" or bounce-land ETB bounce),
///   then applies the printed "tapped" rider (CR 701.18). Raw zone
///   manipulation fallback for shape tests.
///
/// ## Why "put a land onto the battlefield" is NOT a land drop
/// CR 305.9 / 113.6c — putting a land directly onto the battlefield via an
/// effect bypasses CR 305.2's per-turn / main-phase / empty-stack gate, so
/// this never touches <see cref="Majik.Core.Game.LandDropTracker"/>.
///
/// ## Deferred (v1)
/// - "You may" auto-accepts when no agent is registered — consistent with
///   the rest of the optional-ETB factory family.
/// </summary>
[CardName("Arboreal Grazer")]
public static class ArborealGrazerFactory
{
    public const string CardName = "Arboreal Grazer";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("arboreal-grazer");

    /// <summary>
    /// Construct Arboreal Grazer with its ETB trigger attached to the card
    /// shape but NOT registered with a <see cref="TriggerManager"/> and with
    /// no live <see cref="ZoneService"/> wiring. Suitable for shape /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Arboreal Grazer with optional <see cref="TriggerManager"/>
    /// and <see cref="ZoneService"/> wiring. When <paramref name="triggers"/>
    /// is supplied the ETB trigger is registered so the entering
    /// <c>CardMovedEvent</c> places it on the stack automatically (CR 603.3).
    /// When <paramref name="zoneService"/> is supplied the played land's
    /// Hand → Battlefield move routes through it so ETB triggers /
    /// replacements on that land fire (CR 603.6a / CR 614).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, you may put a land card from your
        //    hand onto the battlefield tapped."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: put a land card from hand onto the battlefield tapped",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                await PutLandFromHandTappedAsync(controller, ctx, zoneService).ConfigureAwait(false);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    /// <summary>
    /// Resolve the "you may put a land card from your hand onto the
    /// battlefield tapped" ETB. Candidate set = every land card in
    /// <paramref name="player"/>'s hand. Consults the agent for the optional
    /// opt-in and the which-land pick; deterministic first-land fallback when
    /// no agent is registered. Moves Hand → Battlefield (via
    /// <paramref name="zoneService"/> when supplied so ETB triggers /
    /// replacements on the played land fire — CR 603.6a / CR 614), then
    /// applies the printed "tapped" rider (CR 701.18).
    /// </summary>
    private static async ValueTask PutLandFromHandTappedAsync(
        Player player, ResolutionContext ctx, ZoneService? zoneService)
    {
        var candidates = player.Zones.Hand.GetCards()
            .Where(c => c.HasType(CardType.Land))
            .ToList();
        if (candidates.Count == 0) return; // No lands → "may" no-op.

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        // "You may" — CR 117.1a optional gesture. Agent path via
        // ChooseYesNoAsync(BotIntent.Ramp); no-agent fallback auto-accepts.
        if (agent != null)
        {
            var optIn = await agent.ChooseYesNoAsync(
                    "Put a land card from your hand onto the battlefield tapped?",
                    BotIntent.Ramp).ConfigureAwait(false);
            if (!optIn) return;
        }

        // Which land. Agent-driven via ChooseFromHandAsync with candidates
        // pre-filtered to lands; no-agent fallback takes the first.
        ICard? land;
        if (agent != null)
        {
            land = await agent.ChooseFromHandAsync(player, candidates, BotIntent.Ramp).ConfigureAwait(false);
            // Re-validate at resolution (CR 608.2b).
            if (land == null || !candidates.Contains(land)) return;
        }
        else
        {
            land = candidates[0];
        }

        // Hand → Battlefield. Prefer ZoneService so ETB triggers +
        // replacements on the played land fire (CR 603.6a / CR 614).
        if (zoneService != null)
        {
            await zoneService.MoveCardAsync(
                land, ZoneType.Hand, ZoneType.Battlefield, ctx, player)
                .ConfigureAwait(false);
        }
        else
        {
            player.Zones.Hand.RemoveCard(land);
            player.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
            land.SetController(player);
        }

        // CR 701.18 — the printed "tapped" rider, applied after the move.
        if (land is Permanent perm && !perm.IsTapped) perm.Tap();
    }
}
