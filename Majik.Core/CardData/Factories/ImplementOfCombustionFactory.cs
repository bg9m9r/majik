using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
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
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Implement of Combustion with optional trigger-manager wiring.
    /// When <paramref name="triggers"/> is supplied, the dies trigger is
    /// registered so the bus surfaces it automatically (mirrors Ichor
    /// Wellspring's two-arg pattern).
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
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

                SacrificeSelf(impl, owner);
            });

        damageAbility = new ActivatedAbility(
            source: impl,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{R}"),
                AdditionalCost.Sacrifice(impl),
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
    /// </summary>
    private static void SacrificeSelf(Artifact impl, Player owner)
    {
        if (impl.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(impl);
        owner.Zones.Graveyard.AddCard(impl);
        impl.SetZone(ZoneType.Graveyard);
    }
}
