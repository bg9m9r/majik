using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Seal Away (Dominaria, {1}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Flash
///    When this enchantment enters, exile target tapped creature an opponent
///    controls until this enchantment leaves the battlefield."
///
/// A targeted, Flash-speed member of the Banishing Light / Oblivion Ring
/// "exile-until-this-leaves" (O-Ring) template — exile a problem permanent
/// while the enchantment sticks; return it if the enchantment leaves the
/// battlefield. Shares the ETB-exile / LTB-return per-source closure used by
/// <see cref="BanishingLightFactory"/> and <see cref="OblivionRingFactory"/>,
/// with TWO printed-text differences from Banishing Light:
///   - <b>Flash</b> (CR 702.8) — castable at instant speed.
///   - The ETB target is narrowed to "target <b>tapped creature</b> an
///     opponent controls": creature-only, tapped-only, opponent-controlled.
///
/// ## Shape source
/// Card identity (name, {1}{W}, Enchantment) is loaded from
/// <c>Majik.Core/CardData/Cards/seal-away.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> — same posture as
/// <see cref="OblivionRingFactory"/>. The Flash marker and the
/// exile-until-leaves ETB / LTB pair are attached in code below (the JSON
/// ability schema does not express the cross-trigger closure that captures the
/// exiled card).
///
/// ## Implemented (v1)
/// - <b>Enchantment {1}{W}</b>. Owner / controller wired.
/// - <b>Flash</b> keyword marker (CR 702.8) — same surface as
///   <see cref="TheWanderingEmperorFactory"/>'s Flash grant.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "target tapped creature an opponent controls"
///       <see cref="TargetRequest"/>.</item>
///     <item>On resolve: CR 608.2b legality re-check (still on the
///       battlefield, still a creature, still tapped, and — CR 109.5 — still
///       controlled by an opponent of the Seal Away controller). If legal,
///       exile via raw zone move routed through the target's owner's zones. A
///       reference to the exiled card AND its owner is captured in a
///       per-Seal-Away closure shared with the LTB ability.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever
///   Seal Away moves OUT of the battlefield (any destination — covers dies +
///   bounce + flicker, matching "leaves the battlefield" wording, same posture
///   as Banishing Light / Oblivion Ring). On resolve: if a card was exiled and
///   is still in exile, it is returned to the battlefield under its owner's
///   control (CR 110.2 — "under its owner's control" maps Controller := Owner
///   on the way back).
///
/// ## Deferred (v1 gaps)
/// - Mirrors <see cref="BanishingLightFactory"/>'s deferrals: a single Seal
///   Away exiles one card per ETB resolution (printed "target" is singular);
///   the per-instance closure captures one card. Flicker creates a fresh ICard
///   identity (CR 400.7) with an empty closure, matching real MTG.
/// </summary>
[CardName("Seal Away")]
public static class SealAwayFactory
{
    public const string CardName = "Seal Away";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("seal-away");

    /// <summary>
    /// Construct Seal Away with the Flash marker + both triggered abilities
    /// attached to the card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Seal Away with optional <see cref="TriggerManager"/> wiring.
    /// When <paramref name="triggers"/> is supplied, both ETB and LTB abilities
    /// are registered so the bus drives them via <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed. Keyword marker so
        // the engine treats the card as having Flash (same surface as
        // SpellQueller / The Wandering Emperor).
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21.
        //   "When this enchantment enters, exile target tapped creature an
        //    opponent controls until this enchantment leaves the battlefield."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{CardName}: exile target tapped creature an opponent controls (CR 701.21)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution checks.
                if (target.Zone != ZoneType.Battlefield) return;
                // Printed "creature" — only creatures are legal targets.
                if (!target.HasType(CardType.Creature)) return;
                // Printed "tapped" — must still be tapped at resolution.
                if (!target.IsTapped) return;
                // CR 109.5 — must be an opponent's permanent at resolution.
                if (ReferenceEquals(target.Controller, card.Controller ?? owner)) return;

                // CR 701.21 — exile (Battlefield → Exile). Routed through the
                // target's owner's zones — same posture as Banishing Light /
                // Oblivion Ring.
                var targetOwner = target.Owner;
                if (targetOwner != null)
                {
                    targetOwner.Zones.Battlefield.RemoveCard(target);
                    targetOwner.Zones.Exile.AddCard(target);
                }
                target.SetZone(ZoneType.Exile);

                exiled = target;
                exiledOwner = targetOwner;
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target tapped creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "until this enchantment leaves the battlefield" — when this leaves,
        //   return the exiled card to the battlefield under its owner's control.
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName}: return the exiled card to the battlefield under its owner's control",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 400.7 — if the exiled card has since left exile, skip.
                if (exiled.Zone != ZoneType.Exile) return;

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Battlefield.AddCard(exiled);
                exiled.SetZone(ZoneType.Battlefield);
                // CR 110.2 — "under its owner's control" maps Controller :=
                // Owner on the way back.
                if (exiled is Card returned) returned.ChangeController(exiledOwner);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last existed on
            // the battlefield (same "looks back" semantics as Banishing Light).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }
}
