using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rattlechains (Shadows over Innistrad, {1}{W}).
///
/// Creature — Spirit 2/2. Oracle text:
///   "Flash
///    Flying
///    When this creature enters, target Spirit you control gains hexproof
///    until end of turn.
///    You may cast Spirit spells as though they had flash."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Spirit, mana cost {1}{W}, owner / controller wired.
/// - <b>Flash</b> + <b>Flying</b> keyword markers (CR 702.8 / 702.9) via
///   <see cref="KeywordAbility"/>.
/// - <b>ETB hexproof rider</b> (CR 603.6a): a <see cref="TriggeredAbility"/>
///   declaring a 1..1 <see cref="TargetRequest"/> for "target Spirit you
///   control". On resolution:
///   <list type="number">
///     <item>If a target was chosen and is still on the battlefield as a
///       Spirit controlled by Rattlechains' controller (CR 608.2b), a
///       <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting
///       "Hexproof" is registered on the supplied
///       <see cref="ContinuousEffectsService"/>.</item>
///     <item>If no service is supplied (shape / unit tests) the effect is
///       a clean no-op — the trigger still fires but the keyword grant
///       has nowhere to live.</item>
///   </list>
///   <c>BotIntent.Protection</c> on the request so the bot picks the
///   threatened spirit.
/// - <b>Spirit-flash printed static</b> "You may cast Spirit spells as
///   though they had flash" (CR 117.1 / 702.8): wired via the existing
///   generic <see cref="FlashGrantStaticEffect"/> primitive (Sigarda's Aid
///   uses the same surface). While Rattlechains is on the battlefield,
///   <see cref="Majik.Core.Rules.FlashGrantRegistry"/> carries a predicate
///   matching every card owned by Rattlechains' controller whose subtypes
///   include Spirit; <see cref="Majik.Core.Rules.TimingRules.CanCastAtInstantSpeed"/>
///   consults the registry after the printed Instant / Flash check, so
///   Spirit cards in the controller's hand pass instant-speed cast checks
///   (Mausoleum Wanderer, Selfless Spirit, Supreme Phantom, Spell Queller,
///   another Rattlechains, …). Owner-keyed per CR 108.4 (controller of a
///   card outside the battlefield is its owner) — same posture as
///   Sigarda's Aid.
///
/// ## Deferred (v1 gaps)
/// - <b>Target prompt for ETB hexproof</b>: <c>LegalCandidates</c> is left
///   empty (same posture as Pestermite / Snapcaster Mage — production
///   agent enumerates the live battlefield itself).
/// - <b>Hexproof-from-source-controller distinction</b>: vanilla
///   "Hexproof" is granted; finer-grained "from your opponents" variants
///   aren't relevant here (printed text is plain "hexproof").
/// </summary>
[CardName("Rattlechains")]
public static class RattlechainsFactory
{
    public const string CardName = "Rattlechains";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Rattlechains with no live event bus, trigger manager, or
    /// continuous-effects service. The ETB trigger is attached
    /// structurally; the Spirit-flash grant lifecycle is created but not
    /// attached so <see cref="Majik.Core.Rules.FlashGrantRegistry"/> stays
    /// pristine for shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, continuousEffects: null);

    /// <summary>
    /// Construct Rattlechains with optional runtime services. When
    /// <paramref name="eventBus"/> is supplied, the Spirit-flash grant
    /// lifecycle attaches (registering with
    /// <see cref="Majik.Core.Rules.FlashGrantRegistry"/> while Rattlechains
    /// is on the battlefield, releasing on LTB). When
    /// <paramref name="triggers"/> is supplied, the ETB-hexproof rider is
    /// registered for bus-driven firing. When
    /// <paramref name="continuousEffects"/> is supplied, the ETB grants a
    /// real Layer-6 hexproof keyword on resolution.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 / CR 702.9 — Flash + Flying keyword markers.
        card.AddAbility(new KeywordAbility("Flash", card, owner));
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Printed static — "You may cast Spirit spells as though they had
        // flash." (CR 117.1 / 702.8.) FlashGrantStaticEffect handles
        // ETB/LTB lifecycle via CardMovedEvent. Predicate keys off owner:
        // per CR 108.4 the controller of a card outside the battlefield
        // is its owner, so for cards in hand this is the cast-time
        // controller. Mirrors Sigarda's Aid plumbing.
        // ----------------------------------------------------------------
        var flashGrant = new FlashGrantStaticEffect(
            source: card,
            eventBus: eventBus,
            predicate: c =>
            {
                if (c == null) return false;
                if (!ReferenceEquals(c.Owner, owner)) return false;
                return c.HasSubtype(CardSubtype.Spirit);
            });
        flashGrant.Attach();

        // ----------------------------------------------------------------
        // ETB trigger — "When this creature enters, target Spirit you
        // control gains hexproof until end of turn." (CR 603.6a + CR
        // 702.11.) 1..1 TargetRequest for the Spirit, resolution-time
        // legality re-check (CR 608.2b) before registering a Layer-6
        // GrantKeywordUntilEndOfTurnEffect("Hexproof").
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;
        var condition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName} — grant target Spirit hexproof EOT",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Creature target) return;

                // CR 608.2b — resolution-time illegal-target check. The
                // target must still be a Spirit on the battlefield
                // controlled by Rattlechains' controller.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasSubtype(CardSubtype.Spirit)) return;
                if (!ReferenceEquals(target.Controller, owner)) return;

                if (continuousEffects == null) return;

                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(target, "Hexproof"));
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target Spirit you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
