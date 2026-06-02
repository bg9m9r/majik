using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brutal Cathar — transform DFC front face
/// (Innistrad: Midnight Hunt, {2}{W}). Back face: Moonrage Brute.
///
/// Creature — Human Soldier Werewolf 2/2. Oracle text (Scryfall verified, front):
///   "Whenever this creature enters or transforms into Brutal Cathar, exile
///    target creature an opponent controls until this creature leaves the
///    battlefield.
///    Daybound (If a player casts no spells during their own turn, it becomes
///    night next turn.)"
///
/// Back face (Moonrage Brute): Creature — Werewolf 3/3 (red). "First strike.
/// Ward—Pay 3 life. Nightbound (If a player casts at least two spells during
/// their own turn, it becomes day next turn.)"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Soldier Werewolf at {2}{W} (front face), owner /
///   controller set. <see cref="CardSubtype.Werewolf"/> subtype.
/// - <b>Daybound + Nightbound</b> (CR 702.145): the DFC carries a Daybound
///   marker (front) and a Nightbound marker (back), consumed by
///   <see cref="DayboundNightbound"/>. The game's untap-step day/night check
///   (CR 502.2 / 730.2, wired in <see cref="Majik.Core.Game.TurnDriver"/>)
///   flips the attached <see cref="MdfcState"/> between "Brutal Cathar"
///   (front, daybound) and "Moonrage Brute" (back, nightbound) as it becomes
///   day/night. While back-face up,
///   <see cref="Majik.Core.Effects.ContinuousEffectsService"/> seeds the 3/3
///   red Werewolf body (with First strike) from the supplied
///   <see cref="BackFaceCharacteristics"/>.
/// - <b>ETB exile-until-leaves</b> (CR 603.6a / CR 701.21 / CR 603.6c): the
///   same per-source closure shape as Banishing Light / Skyclave Apparition.
///   On the ETB trigger resolution: exile one "target creature an opponent
///   controls" (CR 608.2b legality re-check — still on the battlefield, still
///   a creature, controlled by an opponent of the Cathar's controller). The
///   exiled card + its owner are captured in a per-Cathar closure. The LTB
///   trigger fires whenever the Cathar moves OUT of the battlefield (any
///   destination — dies + bounce + flicker) and returns the exiled card to
///   the battlefield under its owner's control (CR 110.2).
/// - <b>Back-face Ward—Pay 3 life</b> (CR 702.21c): shipped as a
///   <see cref="KeywordAbility"/>("Ward") marker plus a bound
///   <see cref="WardEffect"/> via <see cref="BuildWardEffect"/> whose payment
///   is a real <see cref="PayLifeCost"/>(3) (non-mana ward). Same posture as
///   Sire of Seven Deaths' Pay-7-life ward.
/// - <b>Back-face First strike</b> (CR 702.7): carried as a back-face keyword
///   in <see cref="BackFaceCharacteristics"/> (seeded while back-face up).
///
/// ## Deferred (v1 gaps)
/// - <b>"…or transforms into Brutal Cathar" re-trigger.</b> The engine has no
///   transform-driven triggered-ability event (no <c>TransformedEvent</c> /
///   <c>Triggers.OnTransform*</c>); <see cref="MdfcState.Transform"/> only
///   bumps the CES generation. v1 fires the exile on the ENTERS half only —
///   the same fidelity band every other DFC werewolf ships at (Graveyard
///   Trespasser defers its back-face hot-swap + transform re-trigger
///   identically). Wiring a transform trigger is a shared-infra task tracked
///   for all "whenever this transforms into X" werewolves, not half-built
///   here.
/// </summary>
[CardName("Brutal Cathar")]
public static class BrutalCatharFactory
{
    public const string FrontName = "Brutal Cathar";
    public const string BackName = "Moonrage Brute";
    public const string FrontCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    public const int BackPower = 3;
    public const int BackToughness = 3;

    /// <summary>Printed back-face Ward cost — non-mana (Pay 3 life), CR 702.21c.</summary>
    public const int WardLifeAmount = 3;

    /// <summary>
    /// CR 702.21 — Moonrage Brute's printed "Ward—Pay 3 life" effect, bound to
    /// the supplied <paramref name="card"/>. The ward cost is a real
    /// <see cref="PayLifeCost"/>(3) (non-mana ward); the mana portion is
    /// <see cref="ManaCost.Zero"/>. <see cref="WardEffect.Resolve"/> counters
    /// an opponent's targeting spell/ability unless they pay 3 life (same
    /// posture as <see cref="SireOfSevenDeathsFactory.BuildWardEffect"/>).
    /// </summary>
    public static WardEffect BuildWardEffect(Creature card) =>
        new(card, new PayLifeCost(WardLifeAmount));

    /// <summary>
    /// Construct Brutal Cathar with no live TriggerManager wiring (shape /
    /// dispatcher path). The ETB / LTB exile triggers and the
    /// daybound/nightbound markers are attached so structural assertions see
    /// them; the triggers are not registered with a manager.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Brutal Cathar with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, both the ETB
    /// exile trigger and the LTB return trigger are registered so their events
    /// land them on the stack automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: FrontName,
            manaCost: FrontCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier, CardSubtype.Werewolf });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 — transform DFC face tracker. Front = Brutal Cathar
        // (daybound), back = Moonrage Brute (nightbound). Starts front-face up.
        // The back-face characteristics carrier (Moonrage Brute — 3/3 red
        // Werewolf with First strike) drives the Layer-0 per-face replacement:
        // while back-face up, ContinuousEffectsService.Compute seeds the 3/3
        // red Werewolf body so the daybound/nightbound transform yields the
        // correct back-face body.
        card.MdfcState = new MdfcState(FrontName, BackName,
            BackFaceCharacteristics.Creature(
                name: BackName,
                power: BackPower,
                toughness: BackToughness,
                subtypes: new[] { CardSubtype.Werewolf },
                // CR 702.7 — First strike on the back face.
                keywords: new[] { "First strike" },
                colors: new[] { ManaColor.Red }));

        // CR 702.21 — back-face Ward—Pay 3 life. Marker keyword for discovery;
        // the functional non-mana ward rider is exposed via BuildWardEffect /
        // WardEffect.Resolve (charges PayLifeCost(3)).
        card.AddAbility(new KeywordAbility("Ward", card, owner));

        // CR 702.7 — First strike marker (back face). Kept on the card for the
        // discovery surface; gameplay reads it from BackFaceCharacteristics
        // while back-face up.
        card.AddAbility(new KeywordAbility("First strike", card, owner));

        // CR 702.145 — Daybound (front) + Nightbound (back) markers consumed
        // by DayboundNightbound. The Werewolf carries both; the transform
        // logic gates on the current face.
        card.AddAbility(new KeywordAbility(DayboundNightbound.DayboundKeyword, card, owner));
        card.AddAbility(new KeywordAbility(DayboundNightbound.NightboundKeyword, card, owner));

        WireExileUntilLeavesTriggers(card, owner, triggers);

        return card;
    }

    /// <summary>
    /// Shared wiring for the "exile target creature an opponent controls until
    /// this creature leaves the battlefield" ETB / LTB pair (CR 603.6a /
    /// 701.21 / 603.6c). Mirrors
    /// <see cref="BanishingLightFactory.WireExileEnchantmentTriggers"/> with a
    /// creature-only target filter. v1 fires on the ENTERS half only — the
    /// "or transforms into Brutal Cathar" re-trigger is deferred (no transform
    /// trigger in the engine).
    /// </summary>
    private static void WireExileUntilLeavesTriggers(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        // Shared closure: ETB writes, LTB reads.
        ICard? exiled = null;
        Player? exiledOwner = null;

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.21.
        //   "Whenever this creature enters …, exile target creature an
        //    opponent controls until this creature leaves the battlefield."
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{FrontName}: exile target creature an opponent controls (CR 701.21)",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-on-resolution checks. Must still be on
                // the battlefield, still a creature, controlled by an opponent
                // of the Cathar's controller.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;
                if (ReferenceEquals(target.Controller, card.Controller ?? owner)) return;

                // CR 701.21 — exile (Battlefield → Exile). Routed through the
                // target's OWNER's zones.
                var targetOwner = target.Owner;
                if (targetOwner == null) return;
                targetOwner.Zones.Battlefield.RemoveCard(target);
                targetOwner.Zones.Exile.AddCard(target);
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
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // LTB triggered ability — CR 603.6c / CR 603.10c.
        //   "…until this creature leaves the battlefield" — when this leaves,
        //   return the exiled card to the battlefield under its owner's
        //   control.
        // ----------------------------------------------------------------
        var ltbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card)
                      && e.FromZone == ZoneType.Battlefield);

        var ltbEffect = new Effect(
            $"{FrontName}: return the exiled card to the battlefield under its owner's control",
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
            // CR 603.6d — LTB triggers look back at the permanent as it last
            // existed on the battlefield (same posture as Banishing Light /
            // Skyclave Apparition).
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(ltbTrigger);
        triggers?.RegisterTriggeredAbility(ltbTrigger);
    }
}
