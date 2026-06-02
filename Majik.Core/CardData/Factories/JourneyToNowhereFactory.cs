using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Journey to Nowhere (Zendikar, {1}{W}).
///
/// Enchantment. Oracle text:
///   "When this enchantment enters, exile target creature.
///    When this enchantment leaves the battlefield, return the exiled
///    card to the battlefield under its owner's control."
///
/// A one-target subset of the Oblivion Ring / Banishing Light "exile a
/// problem permanent until this leaves" template (see
/// <see cref="BanishingLightFactory"/>). Two differences from Banishing
/// Light's printed text:
/// <list type="bullet">
///   <item>The ETB target is "target creature" — ANY creature, not
///     restricted to "an opponent controls". Self-targeting is legal
///     (Rule 115.4 — a spell/ability can target a permanent its
///     controller controls unless it says "you don't control" /
///     "an opponent controls").</item>
///   <item>The target must be a creature specifically (not any nonland
///     permanent). A creature can never be a land here, so the
///     resolution-time "still a creature on the battlefield" check
///     suffices; no separate nonland gate is needed.</item>
/// </list>
///
/// ## Implemented (v1)
/// - <b>Enchantment {1}{W}</b>. Owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "target creature" <see cref="TargetRequest"/>.</item>
///     <item>On resolve: CR 608.2b legality re-check (still on the
///       battlefield, still a creature). If legal, exile via raw zone
///       move. A reference to the exiled card AND its owner is captured
///       in a per-Journey-to-Nowhere closure shared with the LTB
///       ability.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires
///   whenever Journey to Nowhere moves OUT of the battlefield (any
///   destination — covers dies + bounce + flicker, matching "leaves the
///   battlefield" wording). On resolve: if a card was exiled and is
///   still in exile, it is returned to the battlefield under its owner's
///   control (CR 110.2 — "under its owner's control" maps Controller :=
///   Owner on the way back).
///
/// ## Deferred (v1 gaps)
/// - <b>Flicker race</b>: identical posture to Banishing Light. A
///   flickered Journey to Nowhere re-enters as a new object (CR 400.7)
///   with an empty closure — matching real MTG.
/// </summary>
[CardName("Journey to Nowhere")]
public static class JourneyToNowhereFactory
{
    public const string CardName = "Journey to Nowhere";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>
    /// Construct Journey to Nowhere with no runtime services. Both
    /// triggered abilities are attached to the card shape; neither is
    /// registered with a <see cref="TriggerManager"/>. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Journey to Nowhere with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB
    /// abilities are registered so the bus drives them via
    /// <see cref="CardMovedEvent"/>.
    /// </summary>
    public static Enchantment Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        WireExileCreatureTriggers(card, owner, triggers);
        return card;
    }

    /// <summary>
    /// Wiring for the "exile target creature until this leaves" ETB / LTB
    /// pair. Shares the per-source closure shape with
    /// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/>
    /// but with a creature-only target and no "an opponent controls"
    /// gate.
    /// </summary>
    private static void WireExileCreatureTriggers(
        Enchantment card,
        Player owner,
        TriggerManager? triggers)
    {
        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21.
        //   "When [this] enters, exile target creature."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{card.Name}: exile target creature (CR 701.21)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution checks. The target
                // must still be a creature on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;

                // CR 701.21 — exile (Battlefield → Exile). Routed through
                // the target's owner's zones — same posture as Banishing
                // Light / Skyclave Apparition.
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
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "When [this] leaves the battlefield, return the exiled card to
        //    the battlefield under its owner's control."
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{card.Name}: return the exiled card to the battlefield under its owner's control",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 400.7 — if the exiled card has since left exile, skip.
                if (exiled.Zone != ZoneType.Exile) return;

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Battlefield.AddCard(exiled);
                exiled.SetZone(ZoneType.Battlefield);
                // CR 110.2 — "under its owner's control" maps Controller
                // := Owner on the way back.
                if (exiled is Card returned) returned.ChangeController(exiledOwner);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last
            // existed on the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);
    }
}
