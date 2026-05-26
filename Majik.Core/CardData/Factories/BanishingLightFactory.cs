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
/// Named-card factory for Banishing Light (Journey into Nyx, {2}{W}).
///
/// Enchantment. Oracle text:
///   "When Banishing Light enters, exile target nonland permanent an
///    opponent controls until Banishing Light leaves the battlefield."
///
/// The original "Oblivion Ring" template — exile a problem permanent
/// while the enchantment sticks; return it if the enchantment dies.
/// Shares the exile-on-ETB / return-on-LTB shape with Brain Maggot
/// (hand variant), Spell Queller (stack variant), and Skyclave
/// Apparition (token-spawning variant) — all built on the same
/// per-source closure that captures the exiled card between the two
/// triggered abilities.
///
/// ## Implemented (v1)
/// - <b>Enchantment {2}{W}</b>. Owner / controller wired.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.21):
///   <list type="bullet">
///     <item>Single 1..1 "target nonland permanent an opponent
///       controls" <see cref="TargetRequest"/>.</item>
///     <item>On resolve: CR 608.2b legality re-check (still on the
///       battlefield, still nonland, controlled by an opponent of the
///       Banishing Light controller). If legal, exile via raw zone
///       move. A reference to the exiled card AND its previous owner /
///       controller is captured in a per-Banishing-Light closure
///       shared with the LTB ability.</item>
///   </list>
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires
///   whenever Banishing Light moves OUT of the battlefield (any
///   destination — covers dies + bounce + flicker, matching "leaves
///   the battlefield" wording, same posture as Spell Queller /
///   Skyclave Apparition). On resolve: if a card was exiled and is
///   still in exile, it is returned to the battlefield under its
///   owner's control (CR 110.2 — "under its owner's control" maps
///   Controller := Owner on the way back).
///
/// ## Deferred (v1 gaps)
/// - <b>Multiple-permanent "until this leaves" stacking</b>: a single
///   Banishing Light only ever exiles one card per ETB resolution
///   (the printed "target" is singular). The per-instance closure
///   captures one card; subsequent ETBs of new Banishing Light
///   instances use their own closures. No re-trigger of the ETB on
///   the same Banishing Light is possible without leaving and
///   re-entering, which would be a fresh ICard identity.
/// - <b>Flicker race</b>: if Banishing Light is flickered, the LTB
///   returns the exiled card to the battlefield before the flickered
///   Banishing Light re-enters. The re-entered Banishing Light is a
///   new object (CR 400.7) so its closure starts empty — matching
///   real MTG. The factory captures owner + controller separately
///   so even when ownership changes (e.g. Switcheroo), the return is
///   routed to the captured owner.
/// </summary>
[CardName("Banishing Light")]
public static class BanishingLightFactory
{
    public const string CardName = "Banishing Light";
    public const string PrintedManaCost = "{2}{W}";

    /// <summary>
    /// Construct Banishing Light with no runtime services. Both
    /// triggered abilities are attached to the card shape; neither is
    /// registered with a <see cref="TriggerManager"/>. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Banishing Light with optional runtime services. When
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

        WireExileEnchantmentTriggers(card, owner, triggers);
        return card;
    }

    /// <summary>
    /// Shared wiring for the "exile target nonland permanent an
    /// opponent controls until this leaves" ETB / LTB pair used by
    /// Banishing Light AND <see cref="ConclaveTribunalFactory"/>
    /// (identical oracle modulo Convoke + cost). Public so the sister
    /// factory can call into it without duplicating the closure shape.
    /// </summary>
    /// <param name="card">The Banishing Light-shaped enchantment the
    /// triggers are attached to.</param>
    /// <param name="owner">Card owner / initial controller — used as
    /// the trigger controller.</param>
    /// <param name="triggers">Optional live
    /// <see cref="TriggerManager"/> for bus-driven trigger firing.</param>
    public static void WireExileEnchantmentTriggers(
        Enchantment card,
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);

        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21.
        //   "When [this] enters, exile target nonland permanent an
        //    opponent controls until [this] leaves the battlefield."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{card.Name}: exile target nonland permanent an opponent controls (CR 701.21)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution checks.
                if (target.Zone != ZoneType.Battlefield) return;
                if (target.HasType(CardType.Land)) return;
                // CR 109.5 — must be an opponent's permanent at resolution.
                if (ReferenceEquals(target.Controller, card.Controller ?? owner)) return;

                // CR 701.21 — exile (Battlefield → Exile). Routed through
                // the target's owner's zones — same posture as Skyclave
                // Apparition.
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
                    Description: "target nonland permanent an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "until [this] leaves the battlefield" — when this leaves,
        //   return the exiled card to the battlefield under its owner's
        //   control.
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{card.Name}: return the exiled card to the battlefield under its owner's control",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;
                // CR 400.7 — if the exiled card has since left exile
                // (extraction, processed by Eldrazi, etc.), skip.
                if (exiled.Zone != ZoneType.Exile) return;

                exiledOwner.Zones.Exile.RemoveCard(exiled);
                exiledOwner.Zones.Battlefield.AddCard(exiled);
                exiled.SetZone(ZoneType.Battlefield);
                // CR 110.2 — "under its owner's control" maps Controller
                // := Owner on the way back. ChangeController lives on
                // the concrete Card type; every named-factory permanent
                // is a Card subclass so the cast is safe.
                if (exiled is Card returned) returned.ChangeController(exiledOwner);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last
            // existed on the battlefield (same "looks back" semantics
            // used by Spell Queller, Skyclave Apparition).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);
    }
}
