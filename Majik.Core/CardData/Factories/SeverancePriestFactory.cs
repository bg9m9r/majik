using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Severance Priest (Modern Horizons 3, {W}{B}{G}).
///
/// Creature — Djinn Cleric 3/3. Oracle text (verified against Scryfall):
///   "Deathtouch
///    When this creature enters, target opponent reveals their hand. You may
///    choose a nonland card from it. If you do, exile that card.
///    When this creature leaves the battlefield, the exiled card's owner
///    creates an X/X white Spirit creature token, where X is the mana value
///    of the exiled card."
///
/// Severance Priest is Tidehollow Sculler's bigger, three-colour cousin — the
/// same "reveal-hand-and-exile-a-nonland" ETB on a creature body with a paired
/// LTB clause. The difference is the LTB: instead of returning the exiled card
/// to its owner's hand (Sculler / Brain Maggot), the exiled card's OWNER mints
/// an X/X white Spirit creature token sized by the exiled card's mana value
/// (CR 111 / CR 111.4). The base shape (name, Creature, Djinn Cleric subtypes,
/// {W}{B}{G}, 3/3, Deathtouch keyword) is materialised from the embedded JSON
/// definition (<c>severance-priest.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the two printed closures (ETB
/// exile, LTB token) are layered on here because the JSON
/// <c>AbilityDefinition</c> schema doesn't express exile / token-mint closures
/// (same posture as Tidehollow Sculler / Brain Maggot).
///
/// ## Implemented (v1)
/// - 3/3 Creature — Djinn Cleric at {W}{B}{G} with Deathtouch (CR 702.2),
///   from the JSON <c>keywords</c> array.
/// - <b>ETB triggered ability</b> (CR 603.6a / CR 701.16 / CR 701.21):
///   single 1..1 "target opponent" <see cref="TargetRequest"/>; on resolve the
///   target opponent's hand is "revealed" (CR 701.16 — hand state is already
///   observable to all agents) and the first nonland card is exiled (CR 701.21,
///   Hand → Exile via the card owner's zones). The exiled card + its owner are
///   captured in a per-Priest closure shared with the LTB ability. v1 picks the
///   first nonland card deterministically (caster-choice prompt deferred — same
///   posture as Tidehollow Sculler / Brain Maggot / Grief).
/// - <b>LTB triggered ability</b> (CR 603.6c / CR 603.10c): fires whenever the
///   Priest moves OUT of the battlefield (any destination). On resolve, the
///   exiled card's OWNER creates one X/X white Spirit creature token where X is
///   the mana value of the exiled card (CR 111 / CR 111.4 / CR 202.3). The mana
///   value is read from the exiled card's printed <see cref="Card.ManaCostValue"/>
///   (the card sits in exile, so its converted-mana-value is stable). If no card
///   was exiled (empty / land-only hand), the LTB no-ops cleanly.
///
/// ## Deferred (v1 gaps)
/// - <b>Caster's choice prompt</b>: CR 701.16 — "You may choose a nonland card".
///   v1 picks the first nonland card deterministically.
/// - <b>Public reveal event</b>: a dedicated CardRevealedEvent for UI fan-out is
///   not synthesised by the factory shell path; the target's hand is already
///   publicly inspectable when a live event bus is wired.
/// </summary>
[CardName("Severance Priest")]
public static class SeverancePriestFactory
{
    public const string CardName = "Severance Priest";
    public const string Slug = "severance-priest";

    /// <summary>
    /// Construct Severance Priest with no runtime services. Both triggered
    /// abilities are attached to the card shape; neither is registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Severance Priest with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, both ETB and LTB abilities are
    /// registered so the bus drives them via <see cref="CardMovedEvent"/>. When
    /// <paramref name="zoneService"/> is supplied, the LTB token ETB publishes a
    /// <see cref="CardMovedEvent"/> so ETB observers (e.g. Soul Warden) see the
    /// Spirit enter.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Services.ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature, Djinn
        // Cleric subtypes, {W}{B}{G}, 3/3, Deathtouch). The JSON carries no
        // bespoke abilities — ETB exile + LTB token are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Shared closure: ETB writes (the exiled card + its owner), LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.16 / CR 701.21.
        //   "When this creature enters, target opponent reveals their hand.
        //    You may choose a nonland card from it. If you do, exile that card."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: target opponent reveals hand; exile a nonland card",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player targetOpponent) return;

                // CR 109.5 — "target opponent" must be a player other than
                // the source's controller at resolution time.
                if (ReferenceEquals(targetOpponent, card.Controller ?? owner)) return;

                // CR 701.16 — "reveals their hand" is a public state
                // transition. The engine's hand state is already observable;
                // the outer event bus / UI surfaces the public reveal.

                // v1 deterministic pick — first nonland card in the target's
                // hand. Agent-driven caster-choice deferred (same posture as
                // Tidehollow Sculler / Brain Maggot / Grief).
                var pick = targetOpponent.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(CardType.Land));

                if (pick == null) return; // empty / land-only hand → no exile.

                // CR 701.21 — exile from hand. Routed through the target's
                // own zones (the card's owner is the target opponent).
                targetOpponent.Zones.Hand.RemoveCard(pick);
                targetOpponent.Zones.Exile.AddCard(pick);
                pick.SetZone(ZoneType.Exile);

                exiled = pick;
                exiledOwner = targetOpponent;
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
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "When this creature leaves the battlefield, the exiled card's
        //    owner creates an X/X white Spirit creature token, where X is the
        //    mana value of the exiled card."
        // Fires whenever the Priest moves OUT of the battlefield (any
        // destination — dies + bounce + flicker + exile, same posture as
        // Tidehollow Sculler / Brain Maggot / Skyclave Apparition).
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{CardName}: exiled card's owner creates an X/X white Spirit token",
            () =>
            {
                if (exiled == null || exiledOwner == null) return;

                // CR 202.3 — X is the mana value of the exiled card. The card
                // sits in exile, so its printed converted-mana-value is stable;
                // parse it from the printed mana cost (the ICard surface
                // exposes the cost string, not the parsed VO).
                var x = ManaCost.Parse(exiled.ManaCost).TotalValue;

                // CR 111 / CR 111.4 — the exiled card's OWNER creates one X/X
                // white Spirit creature token (note: NOT Severance Priest's
                // controller — the printed text gives it to the exiled card's
                // owner). Minted through the uniform TokenFactory seam.
                var spec = new TokenFactory.TokenSpec(
                    Name: "Spirit",
                    Power: x,
                    Toughness: x,
                    Subtypes: new[] { CardSubtype.Spirit },
                    Colors: new[] { ManaColor.White });

                TokenFactory.CreateOnBattlefield(spec, exiledOwner, zoneService);
            });

        var ltbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: ltbCondition,
            effects: new IEffect[] { ltbEffect },
            // CR 603.6d — LTB triggers see the permanent as it last existed on
            // the battlefield (same "looks back" semantics as Tidehollow
            // Sculler / Brain Maggot / Skyclave Apparition).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);

        return card;
    }
}
