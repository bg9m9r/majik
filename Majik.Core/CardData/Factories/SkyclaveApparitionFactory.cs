using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skyclave Apparition (Zendikar Rising, {1}{W}{W}).
///
/// Creature — Kor Spirit 2/2. Oracle text:
///   "When Skyclave Apparition enters the battlefield, exile up to one target
///    nonland, nontoken permanent an opponent controls with mana value 4 or less.
///    When Skyclave Apparition leaves the battlefield, that permanent's controller
///    creates an X/X blue Illusion creature token, where X is the exiled card's
///    mana value."
///
/// ## Implemented (v1)
/// - 2/2 Kor Spirit at {1}{W}{W} with correct identity / owner / controller.
/// - <b>ETB triggered ability</b> (CR 603.6a): declares an "up to one" target
///   (<see cref="TargetRequest"/> with MinTargets=0, MaxTargets=1). Filter:
///   opponent's permanent, not a land (CR 305.6), not a token (CR 111.6), mana
///   value ≤ 4 (CR 202.3). On resolution: if a target was chosen and still
///   passes the CR 608.2b legality check (still on the battlefield, mv ≤ 4,
///   nonland, nontoken), the permanent is exiled via raw zone move. A reference
///   to the exiled card is captured in a per-Apparition closure shared with the
///   LTB ability. If 0 targets were chosen (the "up to one" floor), the effect
///   no-ops cleanly.
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever the
///   Apparition moves out of the battlefield (any destination — "leaves the
///   battlefield" covers dies + bounce + flicker, same as Spell Queller). On
///   resolution: if an exiled card was captured, X = that card's printed mana
///   value; creates an X/X blue Illusion creature token under the exiled card's
///   last controller (CR 111.6). If no card was exiled (0-target ETB), no token
///   is created.
/// - Controller of the original permanent receives the token (the oracle says
///   "that permanent's controller"). v1 captures the controller at exile time
///   — same reference semantics as Spell Queller capturing the exiled card's
///   owner for the free-cast callback.
///
/// ## Deferred (v1 gaps)
/// - <b>Token colour</b>: The Illusion token is printed as "blue". v1 does not
///   inject colour identity into tokens — the engine's token colour system
///   (same gap as Crashing Footfalls' green Rhinos, Pact of the Titan's red
///   Giant). The token is created colourless by the Creature constructor; a
///   future colour-layer pass will populate this.
/// - <b>Legality prompt</b>: CR 601.2c — choosing 0 targets requires player
///   confirmation. v1 trusts the caller to supply 0 or 1 chosen targets via
///   <see cref="TriggeredAbility.SetChosenTargets"/>; the agent prompt is
///   deferred.
/// </summary>
public static class SkyclaveApparitionFactory
{
    public const string CardName = "Skyclave Apparition";
    public const string PrintedManaCost = "{1}{W}{W}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int MaxTargetManaValue = 4;

    /// <summary>
    /// Construct Skyclave Apparition with no runtime services. Both triggered
    /// abilities are attached to the card shape; neither is registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Skyclave Apparition with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / controller.</param>
    /// <param name="eventBus">Event bus. When supplied, the LTB condition
    /// uses <see cref="CardMovedEvent"/> routing. Not strictly required for
    /// the current direct-invoke test style, but wired for completeness.</param>
    /// <param name="triggers">When supplied, both triggered abilities are
    /// registered so their respective events land them on the stack
    /// automatically.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Kor, CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledController = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21 (Exile).
        //   "When Skyclave Apparition enters, exile up to one target nonland,
        //    nontoken permanent an opponent controls with mana value 4 or less."
        // MinTargets = 0 (the "up to one" clause). Target must be an opponent's
        // nonland, nontoken permanent with mv ≤ 4.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            "Skyclave Apparition — exile up to one target nonland nontoken permanent mv ≤ 4 (CR 701.21)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                // 0 targets chosen — "up to one" floor, no-op.
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution checks.
                // Must still be on the battlefield.
                if (target.Zone != ZoneType.Battlefield) return;
                // Must still be nonland, nontoken, mv ≤ 4.
                if (target.HasType(CardType.Land)) return;
                if (target.IsToken) return;
                var mv = target.ManaCostValue.TotalValue;
                if (mv > MaxTargetManaValue) return;

                // Capture controller BEFORE exile so the LTB can create the
                // token under the correct player.
                exiledController = target.Controller;

                // CR 701.21 — exile (zone change: Battlefield → Exile).
                var targetOwner = target.Owner;
                if (targetOwner != null)
                {
                    targetOwner.Zones.Battlefield.RemoveCard(target);
                    targetOwner.Zones.Exile.AddCard(target);
                }
                target.SetZone(ZoneType.Exile);

                exiled = target;
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target nonland nontoken permanent an opponent controls with mana value 4 or less",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "When Skyclave Apparition leaves the battlefield, that permanent's
        //    controller creates an X/X blue Illusion creature token, where X is
        //    the exiled card's mana value."
        // Fires whenever Apparition moves OUT of Battlefield (any destination).
        // If no card was exiled (0-target ETB), no token is created.
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            "Skyclave Apparition — that permanent's controller creates X/X blue Illusion token (CR 111.6)",
            () =>
            {
                if (exiled == null || exiledController == null) return;
                // CR 400.7 — if the exiled card has since left exile, skip
                // (e.g. extraction effects). This mirrors Spell Queller's LTB check.
                if (exiled.Zone != ZoneType.Exile) return;

                // X = exiled card's printed mana value (CR 202.3 / 202.3b).
                // ManaCostValue lives on Card (not ICard); all named-factory
                // permanents are Card subclasses, so the cast is safe here.
                var x = exiled is Card exiledCard
                    ? exiledCard.ManaCostValue.TotalValue
                    : 0;

                // Create X/X blue Illusion token under the permanent's controller.
                // CR 111.6 — tokens are created on the battlefield.
                // NOTE (v1): token colour (blue) is not wired — same gap as
                // Crashing Footfalls' green Rhinos / Pact of the Titan's red Giant.
                var spec = new TokenFactory.TokenSpec(
                    Name: "Illusion",
                    Power: x,
                    Toughness: x,
                    Subtypes: new[] { CardSubtype.Illusion });

                TokenFactory.CreateOnBattlefield(spec, exiledController, zones: null);
            });

        var ltb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last existed
            // on the battlefield. ActiveZones = Battlefield here matches the
            // "looks back" semantics used by Spell Queller, Wurmcoil Engine.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltb);
        triggers?.RegisterTriggeredAbility(ltb);

        return card;
    }
}
