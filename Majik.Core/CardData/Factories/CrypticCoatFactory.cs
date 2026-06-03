using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cryptic Coat (Murders at Karlov Manor, {2}{U}).
///
/// Artifact — Equipment. Oracle text (Scryfall, verified 2026-06-02):
///   "When this Equipment enters, cloak the top card of your library, then
///    attach this Equipment to it. (To cloak a card, put it onto the
///    battlefield face down as a 2/2 creature with ward {2}. Turn it face
///    up any time for its mana cost if it's a creature card.)"
///   "Equipped creature gets +1/+0 and can't be blocked."
///   "{1}{U}: Return this Equipment to its owner's hand."
///
/// ## Why a hand-rolled C# factory (not the JSON CardDefinition path)
///
/// Same reason as the rest of the equipment cycle (<see cref="LavaspurBootsFactory"/>,
/// <see cref="ColossusHammerFactory"/>): the data-driven
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/> has no
/// cloak / attach / dynamic attached-boost / return-to-hand shapes, so a JSON
/// def alone produces only a vanilla Equipment shell.
///
/// ## Implementation
///
/// - <b>ETB — "cloak the top card of your library, then attach this Equipment
///   to it"</b> (CR 603.1 self-ETB trigger): on resolution, cloak the top
///   card via <see cref="CloakEffect.CloakCard"/> (CR 702.168 — the unblocked
///   keyword-action primitive this PR ships: a face-down 2/2 with ward {2}
///   that can be turned face up for its mana cost if it's a creature card),
///   then <see cref="Permanent.AttachTo"/> the Coat to the resulting
///   face-down creature (CR 301.5). Empty library → the cloak is a clean
///   no-op and nothing to attach to (CR 702.168).
/// - <b>Static "equipped creature gets +1/+0"</b> — <see cref="AttachedBoostEffect"/>
///   at Layer 7c, reading <see cref="Permanent.AttachedTo"/> dynamically
///   (re-equipping transfers the boost). Same as <see cref="LavaspurBootsFactory"/>.
/// - <b>Static "equipped creature can't be blocked"</b> (CR 509.1b) — a
///   predicate-mode <see cref="CombatRestrictionEffect"/>
///   (<see cref="CombatRestriction.CannotBeBlocked"/>) whose predicate matches
///   the live <see cref="Permanent.AttachedTo"/> creature and whose active
///   gate keeps it live only while the Coat is on the battlefield AND
///   attached. The combat validator consults it via
///   <see cref="ContinuousEffectsService.HasRestriction"/>.
/// - <b>"{1}{U}: Return this Equipment to its owner's hand"</b> (CR 602.5) —
///   an <see cref="ActivatedAbility"/> that bounces the Coat to its owner's
///   hand on resolution (detaching it first, CR 701.3d / 704.5q). The
///   bounce routes through the supplied <see cref="ZoneService"/> when given
///   so LTB triggers fire; otherwise a raw-zone fallback is used.
///
/// ## Lifecycle
///
/// The single-arg <see cref="Create(Player)"/> overload produces the correct
/// card shape only (no service wiring — factory-shape / dispatch tests). The
/// boost / unblockable continuous effects and the ETB cloak trigger are wired
/// only on the fully-serviced overload.
///
/// ## Deferred
///
/// - <b>Ward {2} resolution</b> — the cloaked creature's ward {2} marker
///   (CR 702.168a) is not yet consulted by the spell-resolution path
///   (engine-wide ward gap; tracked with the rest of the marker-keyword ward
///   cards). The cloak primitive ships the {2} marker the resolution-path
///   consultation will read once that wiring lands.
///
/// CR rule references: 702.168 (Cloak) / 702.168a (cloak ward {2}), 708.2 /
/// 708.6 (face-down permanents + turn face up), 603.1 (self-ETB trigger),
/// 301.5 (attach Equipment), 613 Layer 7c (P/T boost), 509.1b (can't be
/// blocked), 602.5 / 701.3d (return to hand).
/// </summary>
[CardName("Cryptic Coat")]
public static class CrypticCoatFactory
{
    public const string CardName = "Cryptic Coat";
    public const string PrintedManaCost = "{2}{U}";
    public const string ReturnCost = "{1}{U}";
    public const int BoostPower = 1;
    public const int BoostToughness = 0;

    /// <summary>
    /// Constructs Cryptic Coat with no live service wiring (the shape /
    /// dispatcher path). Neither the +1/+0 boost, the can't-be-blocked
    /// restriction, nor the ETB cloak trigger are registered.
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null, zones: null);

    /// <summary>
    /// Constructs Cryptic Coat. When <paramref name="continuousEffects"/> is
    /// supplied the +1/+0 boost (Layer 7c) and the can't-be-blocked
    /// restriction (CR 509.1b) are registered, each gating on the Coat being
    /// on the battlefield AND attached. When <paramref name="triggers"/> is
    /// supplied the ETB "cloak + attach" trigger (CR 603.1) is registered so
    /// it surfaces on the stack when the Coat enters. <paramref name="zones"/>
    /// routes the cloak / return-to-hand zone moves through the
    /// <see cref="ZoneService"/> (ETB / LTB triggers fire) when supplied.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers = null,
        ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: PrintedManaCost,
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // "Equipped creature gets +1/+0 and can't be blocked."
        // Both gate on the Coat being on the battlefield AND attached.
        // --------------------------------------------------------------
        if (continuousEffects != null)
        {
            // CR 613 Layer 7c — P/T modification.
            continuousEffects.Register(
                new AttachedBoostEffect(card, power: BoostPower, toughness: BoostToughness));

            // CR 509.1b — "can't be blocked". Predicate-mode restriction
            // matching the live equipped creature; gated on the Coat being
            // on the battlefield AND attached. Persistent (not "this turn").
            continuousEffects.Register(new CombatRestrictionEffect(
                CombatRestriction.CannotBeBlocked,
                predicate: c => ReferenceEquals(card.AttachedTo, c),
                isActiveGate: () =>
                    card.Zone == ZoneType.Battlefield && card.AttachedTo != null,
                expiresAtEndOfTurn: false));
        }

        // --------------------------------------------------------------
        // ETB — "cloak the top card of your library, then attach this
        // Equipment to it." CR 603.1 self-ETB trigger.
        // --------------------------------------------------------------
        var capturedZones = zones;
        var etbEffect = new Effect(
            $"{CardName}: cloak top card, then attach (CR 702.168 / 301.5)",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 702.168 — cloak the top card of the controller's
                // library. Empty library → clean no-op; nothing to attach.
                var cloaked = CloakEffect.Cloak(controller, capturedZones);
                if (cloaked is null) return;

                // CR 301.5 — attach this Equipment to the cloaked creature.
                card.AttachTo(cloaked);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // --------------------------------------------------------------
        // "{1}{U}: Return this Equipment to its owner's hand." CR 602.5.
        // --------------------------------------------------------------
        var returnEffect = new Effect(
            $"{CardName}: return to owner's hand (CR 602.5)",
            () =>
            {
                // CR 301.5e / 704.5q — an Equipment that leaves the
                // battlefield detaches; detach first so the boost /
                // unblockable grants stop applying.
                if (card.AttachedTo != null)
                {
                    card.Unattach();
                }

                var returnOwner = card.Owner ?? owner;
                if (capturedZones is not null)
                {
                    capturedZones.MoveCardTo(card, ZoneType.Hand, returnOwner);
                }
                else
                {
                    (card.Controller ?? returnOwner).Zones.Battlefield.RemoveCard(card);
                    returnOwner.Zones.Hand.AddCard(card);
                    card.SetZone(ZoneType.Hand);
                }
            });

        var returnAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(ReturnCost) },
            effects: new IEffect[] { returnEffect });

        card.AddAbility(returnAbility);

        return card;
    }
}
