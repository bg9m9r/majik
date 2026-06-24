using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Roxanne, Starfall Savant (Outlaws of Thunder
/// Junction, {3}{R}{G}).
///
/// Legendary Creature — Cat Druid 4/3. Oracle text (verified against the
/// embedded Scryfall seed):
///   "Whenever Roxanne enters or attacks, create a tapped colorless artifact
///    token named Meteorite with 'When this token enters, it deals 2 damage
///    to any target' and '{T}: Add one mana of any color.'
///    Whenever you tap an artifact token for mana, add one mana of any type
///    that artifact token produced."
///
/// ## Implemented (v1)
/// - 4/3 <see cref="Creature"/> — Legendary (CR 205.4a), Cat Druid, mana cost
///   {3}{R}{G}, owner/controller wired. Base shape materialised from the
///   embedded JSON definition (<c>roxanne-starfall-savant.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> (same posture as
///   <see cref="PiaAndKiranNalaarFactory"/> / <see cref="LegionWarbossFactory"/>).
/// - <b>"Whenever Roxanne enters or attacks"</b> — modelled as TWO
///   <see cref="TriggeredAbility"/>s sharing one effect body, one over
///   <see cref="CardMovedEvent"/> (CR 603.6a — "enters", via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>) and one over
///   <see cref="CreatureAttacksEvent"/> (CR 508.1f — "attacks", via
///   <see cref="Triggers.OnAttackSelf"/>). Each, on resolution, mints one
///   <b>tapped</b> colorless artifact token named Meteorite (CR 111 / 111.10)
///   carrying its own two abilities (see <see cref="CreateMeteorite"/>). The
///   token is created tapped (CR 111.8 — "create a tapped … token"); its
///   {T} mana ability is therefore unusable until its controller untaps it.
/// - <b>Meteorite ETB — "When this token enters, it deals 2 damage to any
///   target"</b> (CR 603.6a): a <see cref="TriggeredAbility"/> on the token
///   over <see cref="CardMovedEvent"/> matching the token itself; on
///   resolution it routes through <see cref="Fx.DealDamageAny"/> (Player →
///   life loss CR 119.3, Creature → marked damage CR 120.3, Planeswalker →
///   loyalty removal CR 306.7) — the same any-target damage primitive as Pia
///   and Kiran Nalaar / Pyrite Spellbomb. Illegal-on-resolution targets fail
///   silently (CR 608.2b).
/// - <b>Meteorite "{T}: Add one mana of any color"</b> — five
///   <see cref="ManaAbility"/> options (one per WUBRG), the same shape Birds
///   of Paradise / Ornithopter of Paradise use, so the bot's mana picker can
///   satisfy any single colour pip via a Meteorite. Unlike Treasure these are
///   REPEATABLE (no sacrifice cost) — they tap the token for one mana of the
///   chosen colour.
/// - <b>"Whenever you tap an artifact token for mana, add one mana of any
///   type that artifact token produced."</b> (CR 605.1b — a triggered mana
///   ability that triggers on mana being produced and itself produces mana):
///   a <see cref="TriggeredAbility"/> subscribing to
///   <see cref="ManaAbilityActivatedEvent"/> (published by
///   <see cref="Majik.Core.Services.ManaAbilityActivator"/> after the
///   activator's pool is topped up). Identical mechanic to
///   <see cref="MirariWakeFactory"/>'s "whenever you tap a land for mana"
///   doubler — here the gate is "the tapped source is an artifact TOKEN you
///   control" (CR 111 — <see cref="Permanent.IsToken"/> + Artifact type)
///   rather than a Land. The effect re-adds the event's
///   <see cref="ManaAbilityActivatedEvent.ManaGenerated"/> to that
///   controller's mana pool ("any type that artifact token produced",
///   CR 106.6), read straight off the event so a multi-pip or {C} producer
///   doubles correctly.
///
/// ## Deferred (v1 gaps — shared, not specific to this card)
/// - <b>Live TriggerManager / ZoneService wiring</b>: the single-arg
///   dispatcher path (the overload <see cref="NamedCardFactory"/> dispatches
///   to) attaches Roxanne's enters/attacks triggers + the mana-doubler to the
///   card shape and registers the mana-doubler with the ambient
///   <see cref="TriggerManagerRegistry"/> (same posture as
///   <see cref="MirariWakeFactory"/>). Pass a live <see cref="ZoneService"/> /
///   <see cref="TriggerManager"/> to the wired overload so the Meteorite's ETB
///   <see cref="CardMovedEvent"/> fires and its damage trigger surfaces as
///   pending end-to-end.
/// - <b>"Any target" choose-time legality</b>: the Meteorite ETB's
///   <see cref="TargetRequest.LegalCandidates"/> list is left empty; the live
///   any-target pool is supplied by the resolving agent / candidate gatherer,
///   and the resolution closure re-validates via <see cref="Fx.DealDamageAny"/>
///   (CR 608.2b). Same posture as Pia and Kiran Nalaar.
/// </summary>
[CardName("Roxanne, Starfall Savant")]
public static class RoxanneStarfallSavantFactory
{
    public const string CardName = "Roxanne, Starfall Savant";
    public const string Slug = "roxanne-starfall-savant";
    public const string MeteoriteTokenName = "Meteorite";
    public const int MeteoriteDamage = 2;

    /// <summary>
    /// Construct Roxanne with no live runtime services. Suitable for card-shape
    /// / dispatcher tests — the enters/attacks triggers are attached to the
    /// card shape (so <see cref="ICard.Abilities"/> includes them) but the
    /// Meteorite minted on resolution enters via the no-<see cref="ZoneService"/>
    /// branch (no <see cref="CardMovedEvent"/> for the token, so its ETB damage
    /// trigger doesn't surface) and the enters/attacks triggers aren't
    /// registered with any <see cref="TriggerManager"/>. The "tap an artifact
    /// token for mana" doubler IS registered with the ambient
    /// <see cref="TriggerManagerRegistry"/> when one is present (Mirari's Wake
    /// posture). This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, zoneService: null, triggers: TriggerManagerRegistry.Get());

    /// <summary>
    /// Construct a fully-wired Roxanne.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zoneService">Optional zone service so a minted Meteorite's
    /// ETB <see cref="CardMovedEvent"/> fires (its damage trigger + downstream
    /// ETB listeners observe it). Pass <c>null</c> for raw zone moves.</param>
    /// <param name="triggers">TriggerManager to register Roxanne's
    /// enters/attacks triggers, the "tap an artifact token" mana-doubler, and
    /// each minted Meteorite's ETB damage trigger against. May be null — the
    /// triggers are still attached to their card shapes.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (Legendary, Creature,
        // Cat + Druid subtypes, {3}{R}{G}, 4/3). The triggers are layered on
        // below — none is expressible in the current JSON AbilityDefinition
        // schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "Whenever Roxanne enters or attacks, create a tapped colorless
        //  artifact token named Meteorite …" — two triggers, one effect body
        //  (CR 603.6a "enters" + CR 508.1f "attacks").
        // ----------------------------------------------------------------
        IEffect MakeMeteoriteEffect() => new Effect(
            $"{CardName}: create a tapped colorless Meteorite artifact token",
            () =>
            {
                var controller = card.Controller ?? owner;
                CreateMeteorite(controller, zoneService, triggers);
            });

        var entersTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { MakeMeteoriteEffect() },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(entersTrigger);
        triggers?.RegisterTriggeredAbility(entersTrigger);

        var attacksTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { MakeMeteoriteEffect() },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(attacksTrigger);
        triggers?.RegisterTriggeredAbility(attacksTrigger);

        // ----------------------------------------------------------------
        // "Whenever you tap an artifact token for mana, add one mana of any
        //  type that artifact token produced." CR 605.1b — triggered mana
        //  ability over ManaAbilityActivatedEvent. Identical shape to
        //  Mirari's Wake's "whenever you tap a land for mana" doubler, gated
        //  on the tapped source being an artifact TOKEN the controller
        //  controls (CR 111) instead of a Land.
        // ----------------------------------------------------------------
        ManaCost? pendingBonus = null;
        Player? pendingController = null;

        var tapCondition = new EventTriggerCondition<ManaAbilityActivatedEvent>((e, _) =>
        {
            // "you tap" — the activator must be Roxanne's current controller
            // (CR 109.5).
            var you = card.Controller ?? owner;
            if (!ReferenceEquals(e.Player, you)) return false;
            // "an artifact token … for mana" — the tapped source must be an
            // artifact permanent that is a token (CR 111). The Meteorite the
            // controller's other Roxanne made, a Treasure, etc.
            if (e.Source is not Permanent p) return false;
            if (!p.IsToken) return false;
            if (!p.HasType(CardType.Artifact)) return false;
            // "add one mana of any type that artifact token produced"
            // (CR 106.6) — re-add exactly what the token's mana ability
            // produced, read off the event.
            pendingBonus = e.ManaGenerated;
            pendingController = e.Player;
            return true;
        });

        var addManaEffect = new Effect(
            $"{CardName} — add one mana of the type the artifact token produced",
            () =>
            {
                var controller = pendingController;
                var bonus = pendingBonus;
                pendingController = null;
                pendingBonus = null;
                if (controller != null && bonus != null)
                {
                    controller.AddManaToPool(bonus);
                }
            });

        var tapTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: tapCondition,
            effects: new IEffect[] { addManaEffect },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(tapTrigger);
        triggers?.RegisterTriggeredAbility(tapTrigger);

        return card;
    }

    /// <summary>
    /// CR 111 / 111.10 — create one <b>tapped</b> colorless artifact token
    /// named Meteorite under <paramref name="controller"/>'s control, carrying
    /// "When this token enters, it deals 2 damage to any target" (a
    /// <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>) and
    /// "{T}: Add one mana of any color" (five repeatable <see cref="ManaAbility"/>
    /// options, one per WUBRG). The token is created tapped (CR 111.8) so its
    /// {T} mana ability is unusable until its controller untaps it.
    /// </summary>
    /// <param name="controller">Token controller / owner.</param>
    /// <param name="zoneService">Optional zone service so the Meteorite's ETB
    /// <see cref="CardMovedEvent"/> fires (its own damage trigger + downstream
    /// listeners observe it). Pass <c>null</c> for a raw zone move.</param>
    /// <param name="triggers">Optional trigger manager to register the
    /// Meteorite's ETB damage trigger against so it surfaces as pending. May be
    /// null — the trigger is still attached to the token shape.</param>
    public static Artifact CreateMeteorite(
        Player controller,
        ZoneService? zoneService = null,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var token = new Artifact(MeteoriteTokenName, "")
        {
            Owner = controller,
            Controller = controller,
            IsToken = true,
        };
        // CR 111.10 — Meteorite tokens are colourless artifacts.
        token.SetTokenColors(Array.Empty<ManaColor>());

        // "{T}: Add one mana of any color." — five repeatable ManaAbility
        // options (one per colour), the Birds of Paradise / Ornithopter of
        // Paradise shape. No sacrifice (contrast Treasure) — the token stays
        // on the battlefield and re-taps each turn.
        foreach (var pip in new[] { "W", "U", "B", "R", "G" })
        {
            token.AddAbility(new ManaAbility(
                source: token,
                controller: controller,
                manaGenerated: ManaCost.Parse(pip),
                canActivateCheck: () => !token.IsTapped
                                        && token.Zone == ZoneType.Battlefield));
        }

        // "When this token enters, it deals 2 damage to any target." CR 603.6a
        // ETB trigger; the any-target damage routes through Fx.DealDamageAny
        // (Player / Creature / Planeswalker each take the right shape — CR
        // 119.3 / 120.3 / 306.7). Same primitive as Pia and Kiran Nalaar.
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            $"{MeteoriteTokenName}: deal {MeteoriteDamage} damage to any target",
            () =>
            {
                if (etbTrigger == null) return;
                if (etbTrigger.ChosenTargets.Count == 0) return;
                if (etbTrigger.ChosenTargets[0].Count == 0) return;

                var target = etbTrigger.ChosenTargets[0][0];
                Fx.DealDamageAny(target, MeteoriteDamage); // CR 608.2b — gated per shape
            });

        etbTrigger = new TriggeredAbility(
            source: token,
            controller: controller,
            condition: Triggers.OnEnterBattlefieldSelf(token),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });
        token.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Put the token onto the battlefield. Sentinel-library pattern shared
        // by TokenFactory.CreateTreasure so CardMovedEvent fires correctly (the
        // Meteorite's own ETB damage trigger observes it).
        token.SetZone(ZoneType.Library);
        controller.Zones.Library.AddCard(token);
        if (zoneService != null)
        {
            zoneService.MoveCardTo(token, ZoneType.Battlefield, controller);
        }
        else
        {
            controller.Zones.Library.RemoveCard(token);
            token.SetZone(ZoneType.Battlefield);
            controller.Zones.Battlefield.AddCard(token);
        }

        // CR 111.8 — "create a tapped … token": the Meteorite enters tapped.
        // Tapped AFTER the zone move (entering the battlefield clears tapped
        // status, CR 614 — see Permanent.ClearTappedOnLeaveBattlefield) so the
        // final state is tapped.
        if (!token.IsTapped)
        {
            token.Tap();
        }

        return token;
    }
}
