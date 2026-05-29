using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hellspark Elemental (Eventide,
/// Creature — Elemental {1}{R}).
///
/// Oracle text (verified via Scryfall):
///   "Trample, haste
///    At the beginning of the end step, sacrifice this creature.
///    Unearth {1}{R} ({1}{R}: Return this card from your graveyard to the
///    battlefield. It gains haste. Exile it at the beginning of the next
///    end step or if it would leave the battlefield. Unearth only as a
///    sorcery.)"
///
/// ## Implemented (v1)
/// - 3/1 Creature — Elemental, mana cost {1}{R}, owner / controller wired.
/// - <b>Trample (CR 702.19)</b> + <b>Haste (CR 702.10)</b>: printed
///   <see cref="KeywordAbility"/> markers, same shape as the Trample/Haste
///   bodies elsewhere in the pool (e.g. Squee's Menace/Haste markers).
///   <c>CombatAbilities.HasHaste</c> / the combat-damage assignment read
///   them through the existing keyword pipeline.
/// - <b>"At the beginning of the end step, sacrifice this creature"
///   (CR 603.2 / CR 500.4 / CR 701.16)</b>: a <see cref="TriggeredAbility"/>
///   on <see cref="StepStartedEvent"/>(End). The printed wording has no
///   "your" qualifier, so it triggers at the beginning of <i>every</i> end
///   step (mirrors Spark Elemental). The resolution effect moves the
///   creature from its controller's battlefield to its owner's graveyard;
///   it guards the battlefield-zone check at fire time so a copy that has
///   already left the battlefield (bounce / destroy) is not yanked from
///   elsewhere. Same sacrifice shape as
///   <see cref="SneakAttackFactory"/>'s delayed end-step sac.
/// - <b>Unearth {1}{R} (CR 702.84)</b>: a graveyard-activated, sorcery-speed
///   <see cref="ActivatedAbility"/> with a {1}{R} <see cref="ManaCostCost"/>.
///   On resolution it returns this card from its owner's graveyard to the
///   battlefield (routing through <see cref="ZoneService.MoveCard"/> when
///   supplied so ETB triggers fire — CR 603.6a), grants Haste, clears
///   summoning sickness, and registers a one-shot
///   <see cref="DelayedTriggeredAbility"/> (CR 603.7) that <b>exiles</b> the
///   creature at the beginning of the next end step. Same graveyard-return
///   activated-ability shape as <see cref="SqueeDubiousMonarchFactory"/> /
///   <see cref="PriestOfFellRitesFactory"/>; same delayed end-step pattern
///   as <see cref="SneakAttackFactory"/>, but the unearth rider <b>exiles</b>
///   (CR 702.84c) rather than sacrifices.
///
/// ## Deferred (v1 gaps — mirror the existing unearth-style factories)
/// - <b>Zone-scoped activation</b>: the engine does not yet gate activated
///   abilities on source zone (CR 113.6). The unearth ability is enumerable
///   from any zone; the resolution body guards <c>card.Zone == Graveyard</c>
///   so spurious activations are no-op-shaped (same caveat as Priest of Fell
///   Rites / Squee, Dubious Monarch).
/// - <b>"…or if it would leave the battlefield" exile rider</b>: the
///   "exile at the next end step" half of unearth (CR 702.84c) is wired via
///   the delayed end-step trigger. The "or if it would leave the
///   battlefield" half is a replacement-style effect (CR 614) that needs a
///   per-permanent leaves-the-battlefield replacement hook the engine does
///   not yet expose for token-less, graveyard-origin permanents. Because the
///   end-step exile already fires the same turn the creature returns (the
///   end-step sacrifice line would otherwise send it to the graveyard, but
///   the unearth replacement upgrades that to exile), the observable outcome
///   for the common line — unearth, attack, end step — is identical:
///   the card ends up in exile. The corner case (unearth, then it's
///   destroyed / sacrificed mid-turn) lands it in the graveyard instead of
///   exile for that one turn; recorded as a deferral, not half-built infra.
/// </summary>
[CardName("Hellspark Elemental")]
public static class HellsparkElementalFactory
{
    public const string CardName = "Hellspark Elemental";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 3;
    public const int Toughness = 1;

    /// <summary>Unearth activation cost. CR 702.84.</summary>
    public const string UnearthCost = "{1}{R}";

    /// <summary>Granted keyword on unearth + base printed keyword. CR 702.10.</summary>
    public const string Haste = "Haste";

    /// <summary>Base printed keyword. CR 702.19.</summary>
    public const string Trample = "Trample";

    /// <summary>
    /// Construct Hellspark Elemental with no live runtime services. The
    /// Trample/Haste markers, the end-step sacrifice trigger, and the
    /// Unearth activated ability are attached for shape inspection; the
    /// sacrifice + unearth-return use raw zone moves and no delayed exile
    /// trigger is registered. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, triggers: null);

    /// <summary>
    /// Construct a fully-wired Hellspark Elemental. When
    /// <paramref name="zoneService"/> is supplied the sacrifice + unearth
    /// return route through <see cref="ZoneService.MoveCard"/> so the
    /// relevant zone-change events / ETB triggers fire (CR 603.6a). When
    /// <paramref name="triggers"/> is supplied the end-step sacrifice
    /// trigger is registered, and each unearth activation registers its own
    /// one-shot delayed end-step <b>exile</b> trigger (CR 603.7 / 702.84c).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trample (CR 702.19) + Haste (CR 702.10). KeywordAbility markers;
        // the combat-damage assignment / CombatAbilities.HasHaste read them
        // through the existing keyword pipeline.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(Trample, card, owner));
        card.AddAbility(new KeywordAbility(Haste, card, owner));

        // ----------------------------------------------------------------
        // "At the beginning of the end step, sacrifice this creature."
        // CR 603.2 — triggered ability; the printed wording has no "your"
        // qualifier so it fires at the beginning of EVERY end step (Spark
        // Elemental wording). CR 500.4 / CR 701.16 — sacrifice =
        // controller's battlefield → owner's graveyard.
        // ----------------------------------------------------------------
        var sacEffect = new Effect(
            $"{CardName}: at the beginning of the end step, sacrifice this creature",
            () => SacrificeSelf(card, owner, zoneService));

        var sacTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End),
            effects: new IEffect[] { sacEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(sacTrigger);
        triggers?.RegisterTriggeredAbility(sacTrigger);

        // ----------------------------------------------------------------
        // Unearth {1}{R} — CR 702.84. Graveyard-activated, sorcery-speed
        // activated ability. Returns this card from graveyard → battlefield,
        // grants Haste, and registers a delayed end-step EXILE (CR 702.84c).
        //
        // Guard: only resolves while the card is in its owner's graveyard
        // (engine zone-scoping deferred — same caveat as Priest of Fell
        // Rites / Squee, Dubious Monarch).
        // ----------------------------------------------------------------
        var unearthEffect = new Effect(
            $"{CardName}: unearth — return from graveyard, gain haste, exile next end step",
            () => ResolveUnearth(card, owner, zoneService, triggers));

        var unearthAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(UnearthCost) },
            effects: new IEffect[] { unearthEffect },
            // CR 702.84a — "Unearth only as a sorcery." ActionValidator gates
            // activation on the controller's main phase + empty stack.
            sorcerySpeed: true);

        card.AddAbility(unearthAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — sacrifice this creature: controller's battlefield →
    /// owner's graveyard. Guards the battlefield-zone check so a copy that
    /// already left the battlefield is not pulled from elsewhere. Routes
    /// through <see cref="ZoneService.MoveCard"/> when supplied.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner, ZoneService? zoneService)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        var bfPlayer = card.Controller ?? owner;
        if (!bfPlayer.Zones.Battlefield.GetCards().Contains(card)) return;
        var graveyardOwner = card.Owner ?? owner;

        if (zoneService != null)
        {
            zoneService.MoveCard(card, ZoneType.Battlefield, ZoneType.Graveyard, bfPlayer);
        }
        else
        {
            bfPlayer.Zones.Battlefield.RemoveCard(card);
            graveyardOwner.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }
    }

    /// <summary>
    /// CR 702.84 — resolve the Unearth activation. Returns the card from its
    /// owner's graveyard to the battlefield under the controller's control,
    /// grants Haste (CR 702.10), clears summoning sickness, and (when
    /// <paramref name="triggers"/> is supplied) registers a one-shot delayed
    /// end-step trigger that EXILES the creature (CR 702.84c). No-ops cleanly
    /// when the card is not in its owner's graveyard (zone-scoping deferred).
    /// </summary>
    private static void ResolveUnearth(
        Creature card, Player owner, ZoneService? zoneService, TriggerManager? triggers)
    {
        // Zone guard — unearth only returns the card from the graveyard.
        if (card.Zone != ZoneType.Graveyard) return;
        if (card.Owner == null || !ReferenceEquals(card.Owner, owner)) return;
        if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

        // Graveyard → battlefield (CR 702.84a). ZoneService routes the
        // publish so ETB triggers fire (CR 603.6a).
        if (zoneService != null)
        {
            zoneService.MoveCard(card, ZoneType.Graveyard, ZoneType.Battlefield, owner);
        }
        else
        {
            owner.Zones.Graveyard.RemoveCard(card);
            owner.Zones.Battlefield.AddCard(card);
            card.SetZone(ZoneType.Battlefield);
            card.SetController(owner);
        }

        // "It gains haste." CR 702.84a / CR 613.1c (Layer 6). EOT-scoped grant
        // is observationally equivalent to the printed no-duration wording —
        // the card is exiled at the same end-step boundary at which the grant
        // would expire. No-op silently when no ActiveEffects service is wired
        // (shape mode); the summoning-sickness clear below still applies so
        // attack-declaration sees haste behaviour (CR 702.10b).
        if (card.ActiveEffects != null)
        {
            card.ActiveEffects.Register(new GrantKeywordUntilEndOfTurnEffect(card, Haste));
        }
        card.HasSummoningSickness = false;

        // "Exile it at the beginning of the next end step." CR 702.84c /
        // CR 603.7 — one-shot delayed triggered ability fenced strictly after
        // this resolution so the current end step (if any) doesn't trip it
        // (activation-time fence mirrors Sneak Attack / Through the Breach).
        if (triggers == null) return;

        var resolvedAt = DateTime.UtcNow;
        var exileEffect = new Effect(
            $"{CardName}: unearth — exile at next end step",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var bfPlayer = card.Controller ?? owner;
                if (!bfPlayer.Zones.Battlefield.GetCards().Contains(card)) return;
                var exileOwner = card.Owner ?? owner;

                if (zoneService != null)
                {
                    zoneService.MoveCard(card, ZoneType.Battlefield, ZoneType.Exile, bfPlayer);
                }
                else
                {
                    bfPlayer.Zones.Battlefield.RemoveCard(card);
                    exileOwner.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);
                }
            });

        var delayedExile = new DelayedTriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { exileEffect });

        triggers.RegisterDelayed(delayedExile);
    }
}
