using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Implement of Combustion (Aether Revolt, {1}).
///
/// Artifact. Oracle text:
///   "{R}, Sacrifice this artifact: It deals 1 damage to target player or
///    planeswalker.
///    When this artifact is put into a graveyard from the battlefield, draw
///    a card."
///
/// Closest analogues: <see cref="PyriteSpellbombFactory"/> (the {R}-sac
/// targeted-damage activated ability) and <see cref="IchorWellspringFactory"/>
/// (the Battlefield → Graveyard draw trigger). Implement of Combustion is one
/// of the Aether Revolt "Implement" cycle — a cheap sac-for-effect artifact
/// whose dies trigger replaces itself.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{R}, Sacrifice: 1 damage to target player or planeswalker</b> —
///   wired as an <see cref="ActivatedAbility"/> with a
///   <see cref="ManaCostCost"/>("{R}") plus <see cref="AdditionalCost"/>
///   .Sacrifice on the artifact itself. A single <see cref="TargetRequest"/>
///   is declared so the activating player's agent picks a player /
///   planeswalker at activation (CR 602.2b). The resolution effect reads
///   <see cref="ActivatedAbility.ChosenTargets"/> and routes through
///   <see cref="Fx.DealDamageAny"/> so a Player target loses 1 life
///   (CR 119) and a Planeswalker target loses 1 loyalty (CR 306.7). The
///   sacrifice is performed by the effect closure (mirrors Pyrite / Aether
///   Spellbomb — the generic <see cref="AdditionalCost.Pay"/> sacrifice path
///   is a stub). Illegal-on-resolution targets fail silently (CR 608.2b) —
///   the sacrifice still happens because the cost was paid.
/// - <b>Dies → draw a card</b> — <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnDies"/> (CR 700.4 / 603.6 — Battlefield →
///   Graveyard self-move; <c>OnDies</c> is permanent-agnostic despite the
///   creature-flavoured name). <c>activeZones = {Battlefield, Graveyard}</c>
///   so the gate matches whether the engine evaluates the zone just-before
///   or just-after the move (CR 603.10c last-known-information) — mirrors
///   Ichor Wellspring / Chromatic Star's LTB wiring. Resolves to
///   <see cref="Fx.DrawCards"/>(controller, 1). Empty library is a silent
///   no-op (SBAs handle the loss condition).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move so behaviour is
///   observable. Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
///   Mirrors Pyrite / Aether Spellbomb.
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter the target to "player or planeswalker only" — the resolution
///   routes through <see cref="Fx.DealDamageAny"/>, which simply no-ops a
///   non-damageable shape (CR 608.2b).
/// - <b>Live TriggerManager wiring</b>: the single-arg factory attaches the
///   dies trigger for shape inspection but does not register it with a
///   <see cref="TriggerManager"/>. The overload registers it so the bus
///   surfaces it automatically (mirrors Ichor Wellspring / Chromatic Star).
/// </summary>
[CardName("Implement of Combustion")]
public static class ImplementOfCombustionFactory
{
    public const string CardName = "Implement of Combustion";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Implement of Combustion with no live trigger-manager wiring.
    /// The dies trigger is attached to <see cref="Card.Abilities"/> so shape
    /// tests can observe it; for end-to-end firing pass a live
    /// <see cref="TriggerManager"/> via the overload.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — see <see cref="FestivalCrasherFactory"/>). Threads
    /// <c>effects.EventBus</c> into the self-sacrifice cost so paying it
    /// publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a)
    /// crediting the cost-payer. The dies-trigger registration still goes
    /// through the <see cref="TriggerManager"/> overload (separate wiring).
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, triggers: null, eventBus: effects?.EventBus);

    /// <summary>
    /// Construct Implement of Combustion with optional trigger-manager wiring.
    /// When <paramref name="triggers"/> is supplied, the dies trigger is
    /// registered so the bus surfaces it automatically (mirrors Ichor
    /// Wellspring's two-arg pattern). <paramref name="eventBus"/> (when
    /// non-null) is threaded into the self-sacrifice <see cref="AdditionalCost"/>
    /// so the cost-payment path publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers, IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var impl = new Artifact(CardName, PrintedManaCost);
        impl.SetOwner(owner);
        impl.SetController(owner);

        // ----------------------------------------------------------------
        // {R}, Sacrifice this artifact: It deals 1 damage to target player
        // or planeswalker. CR 602 — activated ability with a single target
        // request. Resolution reads ChosenTargets and routes through
        // Fx.DealDamageAny (Player → 1 life lost CR 119; Planeswalker → 1
        // loyalty removed CR 306.7). Illegal targets fail silently
        // (CR 608.2b) — the sacrifice still resolves because the cost was
        // paid.
        // ----------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            "Implement of Combustion: 1 damage to target player or planeswalker + sac self",
            () =>
            {
                if (damageAbility != null
                    && damageAbility.ChosenTargets.Count > 0
                    && damageAbility.ChosenTargets[0].Count > 0)
                {
                    var target = damageAbility.ChosenTargets[0][0];
                    Fx.DealDamageAny(target, 1);
                }

                SacrificeSelf(impl, owner, eventBus);
            });

        damageAbility = new ActivatedAbility(
            source: impl,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{R}"),
                // CR 701.16a — thread the in-scope bus so paying the sac cost
                // publishes PermanentSacrificedEvent for aristocrat payoffs.
                AdditionalCost.Sacrifice(impl, eventBus),
            },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        impl.AddAbility(damageAbility);

        // ----------------------------------------------------------------
        // When this artifact is put into a graveyard from the battlefield,
        // draw a card. CR 700.4 / 603.6 — Battlefield → Graveyard self-move.
        // Triggers.OnDies is shape-generic over CardMovedEvent
        // (FromZone=Battlefield → ToZone=Graveyard for the source).
        // activeZones={Battlefield, Graveyard} so the gate matches whether
        // the engine evaluates pre- or post-move (mirrors Ichor Wellspring's
        // LTB trigger).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Implement of Combustion: draw a card on LTB battlefield->graveyard",
            () => Fx.DrawCards(owner, 1));

        var diesTrigger = new TriggeredAbility(
            source: impl,
            controller: owner,
            condition: Triggers.OnDies(impl),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        impl.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return impl;
    }

    /// <summary>
    /// Move <paramref name="impl"/> from the battlefield to its owner's
    /// graveyard. Idempotent — no-op if already off the battlefield. Mirrors
    /// the closure used by Pyrite / Aether Spellbomb.
    ///
    /// <para>
    /// CR 701.16a — when an <paramref name="eventBus"/> is supplied (the prod
    /// effects-aware build), route the resolve-time self-sacrifice through the
    /// bus-aware <see cref="Fx.Sacrifice(ICard, Player, IEventBus)"/> so a
    /// <see cref="PermanentSacrificedEvent"/> is published for aristocrat
    /// payoffs (Mayhem Devil, Blood Artist, It That Betrays). In the live
    /// activation path the SAC COST already moved the artifact off the
    /// battlefield, so the on-battlefield guard makes this resolve-leg
    /// sacrifice a no-op (single publish either way). Bus-less builds keep the
    /// publish-nothing direct-zone-move posture.
    /// </para>
    /// </summary>
    private static void SacrificeSelf(Artifact impl, Player owner, IEventBus? eventBus)
    {
        if (impl.Zone != ZoneType.Battlefield) return;

        if (eventBus != null)
        {
            Fx.Sacrifice(impl, impl.Controller ?? owner, eventBus);
            return;
        }

        owner.Zones.Battlefield.RemoveCard(impl);
        owner.Zones.Graveyard.AddCard(impl);
        impl.SetZone(ZoneType.Graveyard);
    }
}
