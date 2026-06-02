using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Primitives;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Borborygmos Enraged (Gatecrash, {4}{R}{R}{G}{G}).
///
/// Legendary Creature — Cyclops 7/6. Oracle text (Scryfall, verified):
///   "Trample
///    Whenever Borborygmos Enraged deals combat damage to a player, reveal
///    the top three cards of your library. Put all land cards revealed this
///    way into your hand and the rest into your graveyard.
///    Discard a land card: Borborygmos Enraged deals 3 damage to any target."
///
/// ## Shape source
/// Card identity (name, {4}{R}{R}{G}{G}, 7/6, Legendary Creature — Cyclops)
/// is loaded from <c>Majik.Core/CardData/Cards/borborygmos-enraged.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/>. Trample, the combat-damage
/// reveal trigger, and the discard-a-land burn ability are attached in code
/// — the JSON ability schema does not express keyword markers, combat-damage
/// triggers, or activated abilities with a non-mana discard cost (same
/// posture as <see cref="PiaAndKiranNalaarFactory"/> /
/// <see cref="SengirVampireFactory"/>).
///
/// ## Implemented (v1)
/// - 7/6 <see cref="Creature"/> — Legendary (CR 205.4a), Cyclops, mana cost
///   {4}{R}{R}{G}{G}.
/// - <b>Trample (CR 702.19)</b> — <see cref="KeywordAbility"/> marker so
///   combat code reads it the same way as every other printed Trample
///   creature (<see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>).
/// - <b>Combat-damage reveal trigger (CR 603.1)</b>: "Whenever Borborygmos
///   Enraged deals combat damage to a player, reveal the top three cards of
///   your library. Put all land cards revealed this way into your hand and
///   the rest into your graveyard." Binds the
///   <see cref="CombatDamageDealtEvent"/> subclass (combat-only, unlike
///   Curiosity's any-damage bind) and fires when this card is the source and
///   the target is a <b>player</b> (CR 510.1c). On resolution it reveals the
///   top three cards (clamped to library size — CR 701.21; empty library →
///   clean no-op), deterministically partitions them: every <b>land</b> card
///   (CR 305.1 — basics, nonbasics, land-typed duals all qualify) goes to the
///   controller's <b>hand</b>, the rest to the controller's <b>graveyard</b>.
///   No agent choice — the printed text moves <i>all</i> lands, so this is a
///   straight partition (not a <see cref="RevealAndChoose"/> pick-one). Zone
///   moves route through <see cref="ZoneServiceRegistry"/> when registered so
///   downstream observers fire; raw zone mutation is the shape-test fallback,
///   mirroring <see cref="RevealAndChoose"/>.
/// - <b>Discard-a-land burn ability (CR 602.1 / CR 118.5)</b>: "Discard a
///   land card: Borborygmos Enraged deals 3 damage to any target." Cost is a
///   single <see cref="DiscardALandCardCost"/> (no mana — CR 118.3 a cost
///   need not include mana); a single 1..1 "any target"
///   <see cref="TargetRequest"/>. On resolution the closure reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> (Player → life loss CR 119.3, Creature →
///   marked damage CR 120.3, Planeswalker → loyalty removal CR 306.7) — the
///   same any-target damage primitive as Pia and Kiran Nalaar / Lightning
///   Bolt. Illegal-on-resolution targets fail silently (CR 608.2b).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: no <c>CardsRevealedEvent</c> is published — the
///   reveal is folded into the partition. Same gap as every reveal factory
///   (Satyr Wayfinder, Malevolent Rumble); no live observer cares yet.
/// - <b>Agent-driven discard pick</b>: <see cref="DiscardALandCardCost"/>
///   defaults to the first land card in hand when no <c>Target</c> is set —
///   the shared deferred discard-prompt queue (Faithless Looting / Liliana).
///
/// ## Overloads
/// - <see cref="Create(Player)"/> — card shape + Trample + both abilities
///   attached (combat trigger NOT registered with a
///   <see cref="TriggerManager"/>; no event-bus subscription). The overload
///   <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, IEventBus?)"/> — also subscribes the combat
///   trigger to the supplied bus so a qualifying
///   <see cref="CombatDamageDealtEvent"/> fires the reveal automatically.
/// </summary>
[CardName("Borborygmos Enraged")]
public static class BorborygmosEnragedFactory
{
    public const string CardName = "Borborygmos Enraged";
    public const string Slug = "borborygmos-enraged";

    /// <summary>How many cards are revealed off the top on combat damage.</summary>
    public const int RevealCount = 3;

    /// <summary>Damage dealt by the discard-a-land burn ability.</summary>
    public const int BurnDamage = 3;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Shape overload — attaches Trample + both abilities without wiring the
    /// combat trigger to a live event bus. The overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Borborygmos Enraged. When <paramref name="eventBus"/> is
    /// supplied the combat-damage reveal trigger subscribes so a qualifying
    /// <see cref="CombatDamageDealtEvent"/> automatically reveals the top
    /// three and partitions lands-to-hand / rest-to-graveyard.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.19 — Trample. KeywordAbility marker; combat code reads it via
        // CombatAbilities.HasTrample.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Combat-damage reveal trigger — CR 603.1.
        //   "Whenever Borborygmos Enraged deals combat damage to a player,
        //    reveal the top three cards of your library. Put all land cards
        //    revealed this way into your hand and the rest into your
        //    graveyard."
        //
        // Combat-only: binds CombatDamageDealtEvent (CR 510 / 510.1c — damage
        // to a player), NOT the parent DamageDealtEvent. Matches when this
        // card is the source and the damage target is a player.
        // ----------------------------------------------------------------
        var revealEffect = new Effect(
            $"{CardName}: reveal top {RevealCount}, lands to hand, rest to graveyard",
            () =>
            {
                var controller = card.Controller ?? owner;
                RevealTopAndPartition(controller);
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
                ReferenceEquals(e.SourceCard, card) && e.TargetPlayer != null),
            effects: new IEffect[] { revealEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);

        if (eventBus != null)
        {
            // CR 603.2 — the triggered ability fires when the combat-damage
            // event matches. (Direct subscription rather than a TriggerManager
            // so the reveal runs end to end in tests / unattended runs without
            // a full stack/priority loop, matching the bus-wired posture of
            // SengirVampireFactory.)
            eventBus.Subscribe<CombatDamageDealtEvent>(e =>
            {
                if (!ReferenceEquals(e.SourceCard, card)) return;
                if (e.TargetPlayer == null) return;
                var controller = card.Controller ?? owner;
                RevealTopAndPartition(controller);
            });
        }

        // ----------------------------------------------------------------
        // Discard a land card: Borborygmos Enraged deals 3 damage to any
        // target. CR 602.1 activated ability; CR 118.5 (discard as a cost).
        // The any-target damage routes through Fx.DealDamageAny so Player /
        // Creature / Planeswalker targets each take the right shape of damage
        // (CR 119.3 / 120.3 / 306.7).
        // ----------------------------------------------------------------
        ActivatedAbility? burnAbility = null;
        var burnEffect = new Effect(
            $"{CardName}: deal {BurnDamage} damage to any target",
            () =>
            {
                if (burnAbility == null) return;
                if (burnAbility.ChosenTargets.Count == 0) return;
                if (burnAbility.ChosenTargets[0].Count == 0) return;

                var target = burnAbility.ChosenTargets[0][0];
                Fx.DealDamageAny(target, BurnDamage); // CR 608.2b — gated per shape
            });

        burnAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new DiscardALandCardCost() },
            effects: new IEffect[] { burnEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(burnAbility);

        return card;
    }

    /// <summary>
    /// Reveal the top <see cref="RevealCount"/> cards of
    /// <paramref name="controller"/>'s library and deterministically
    /// partition them: every land card (CR 305.1) into hand, the rest into
    /// the graveyard. Library underflow reveals what's there (CR 701.21);
    /// empty library is a clean no-op. Zone moves route through
    /// <see cref="ZoneServiceRegistry"/> when registered (so observers fire),
    /// falling back to raw zone mutation — the same two-mode posture as
    /// <see cref="RevealAndChoose"/>.
    /// </summary>
    private static void RevealTopAndPartition(Player controller)
    {
        var revealed = controller.Zones.Library.GetCards().Take(RevealCount).ToList();
        if (revealed.Count == 0) return;

        var zones = ZoneServiceRegistry.Get(controller);
        foreach (var revealedCard in revealed)
        {
            // CR 305.1 — "land card" = any card whose type set includes Land
            // (basics, nonbasics, land-typed duals all qualify).
            var dest = revealedCard.HasType(CardType.Land)
                ? ZoneType.Hand
                : ZoneType.Graveyard;

            if (zones != null)
            {
                zones.MoveCard(revealedCard, ZoneType.Library, dest, controller);
            }
            else
            {
                controller.Zones.Library.RemoveCard(revealedCard);
                var destZone = dest == ZoneType.Hand
                    ? controller.Zones.Hand
                    : controller.Zones.Graveyard;
                destZone.AddCard(revealedCard);
                revealedCard.SetZone(dest);
            }
        }
    }
}
