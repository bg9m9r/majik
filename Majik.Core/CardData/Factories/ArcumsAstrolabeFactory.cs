using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Arcum's Astrolabe (Modern Horizons, {(S)}).
///
/// Snow Artifact. Oracle text (verified Scryfall):
///   "When Arcum's Astrolabe enters, draw a card.
///    {1}, {T}: Add one mana of any color."
///
/// ## Implemented (v1)
/// - Card identity (Snow Artifact, owner / controller wiring).
/// - <b>Snow supertype (CR 205.4 / 205.4d)</b> — applied via the
///   <see cref="Artifact"/> ctor's <c>supertypes</c> slot. Card-text
///   inspection / Snow-matter mechanics (Skred, Marit Lage, Coldsteel
///   Heart's {(S)} pip) consult <see cref="Card.HasSupertype"/>.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this enters, draw
///   a card." Resolution calls <see cref="Fx.DrawCards"/>(1) on the
///   controller — same shape as Silvergill Adept / Quantum Riddler.
/// - <b>{1}, {T}: Add one mana of any color</b> — five
///   <see cref="ManaAbility"/> instances (one per WUBRG), each built via
///   the additional-cost overload:
///     - <c>canActivateCheck</c> = <c>!IsTapped AND controller.ManaPool.CanPay({1})</c>
///     - <c>additionalCostPayer</c> = <c>controller.PayMana({1})</c>
///   Same painless-additional-mana shape as the filter-land cycle
///   (<see cref="FilterLandCycleFactory"/>) and the Spire of Industry
///   family. Net mana cost {1} → any single coloured pip.
///
/// ## Rules note — printed {(S)} mana cost
/// The card's printed cost is {(S)} (one snow mana). The engine's
/// <see cref="ManaCost"/> primitive has no Snow-mana slot in v1 — Snow
/// pays generic plus a provenance tag, and the provenance tag is not
/// modelled. Per the export-modern-cards seed, Arcum's Astrolabe ships
/// at its printed string "{S}"; the cast-gate today treats it as one
/// generic pip (effectively {1}) for affordability and routing. The
/// factory uses <c>PrintedManaCost = "{S}"</c> so card-text inspection
/// is honest; the Cost cast-gate honours whatever the engine resolves
/// {S} to (CR 107.4f).
///
/// ## Deferred (v1 gaps)
/// - <b>True Snow-mana primitive</b>: {(S)} should be a colour-tagged
///   pip with the Snow provenance rider so Skred / Marit Lage / Into the
///   North can read it back. v1 collapses {(S)} to generic at parse
///   time — the cast cost works (because Snow can be paid by any mana),
///   but Snow-matter consumers can't see the source provenance. Same
///   v1 gap as Coldsteel Heart and the Iceberg Cancrix family — those
///   ship the supertype marker without the mana-provenance integration.
/// - <b>Single modal-colour mana ability</b>: "Add one mana of any
///   color" is bound as five separate <see cref="ManaAbility"/>
///   instances; the bot's source-picker selects the right colour at
///   payment time. Same posture as Lotus Petal / Mox Opal / Chromatic
///   Star / Delighted Halfling.
/// </summary>
[CardName("Arcum's Astrolabe")]
public static class ArcumsAstrolabeFactory
{
    public const string CardName = "Arcum's Astrolabe";

    /// <summary>
    /// Printed mana cost — one snow mana. The engine collapses {S} to
    /// generic at <see cref="ManaCost.Parse"/> time (CR 107.4f
    /// equivalent treatment, sans provenance), so the cast-gate treats
    /// this as {1} effective. See the factory xmldoc's "Rules note" for
    /// the deferred Snow-mana primitive.
    /// </summary>
    public const string PrintedManaCost = "{S}";

    /// <summary>
    /// Construct Arcum's Astrolabe with no bus / trigger-manager wiring.
    /// The ETB trigger is attached to the card shape so dispatcher /
    /// structural tests can observe it; live firing requires the
    /// (owner, eventBus, triggers) overload. Same posture as Quantum
    /// Riddler / Chromatic Star.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Arcum's Astrolabe. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered so a
    /// <see cref="CardMovedEvent"/> to the battlefield automatically
    /// places it on the stack (CR 603.3); otherwise the trigger is
    /// attached structurally but not registered for firing.
    /// </summary>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            CardName,
            PrintedManaCost,
            supertypes: new[] { CardSupertype.Snow },
            subtypes: null);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When Arcum's Astrolabe enters, draw a card."
        //
        // Resolution: controller draws one card via Fx.DrawCards (top of
        // library → hand; empty-library stamps the SBA loss marker per
        // CR 120.3 / 704.5b). Same shape as Quantum Riddler's ETB.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: controller draws a card on ETB",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, 1);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // {1}, {T}: Add one mana of any color.
        //
        // Five sibling ManaAbility instances (WUBRG), each built via
        // the additional-cost overload of ManaAbility:
        //   canActivateCheck   = !IsTapped AND controller can pay {1}
        //   additionalCostPayer = deduct {1} from controller mana pool
        //
        // Mirrors the FilterLandCycleFactory pattern — bot's source
        // picker iterates by produced colour and picks the matching
        // ability at payment time.
        //
        // CR 605.1 — these are still mana abilities (don't use the
        // stack); the {1} extra cost is paid as part of activation,
        // atomically with the {T} tap.
        // ----------------------------------------------------------------
        var oneGeneric = ManaCost.Parse("1");
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            card.AddAbility(new ManaAbility(
                source: card,
                controller: owner,
                manaGenerated: ManaCost.Parse(color),
                canActivateCheck: () => !card.IsTapped
                                        && card.Zone == ZoneType.Battlefield
                                        && (card.Controller ?? owner).ManaPool.CanPay(oneGeneric),
                additionalCostPayer: p => p.PayMana(oneGeneric)));
        }

        return card;
    }
}
