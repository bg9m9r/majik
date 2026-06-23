using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Glint-Horn Buccaneer (Commander Legends, {1}{R}{R}).
///
/// Creature — Minotaur Pirate 2/4. Oracle text (verified against Scryfall
/// 2026-06-23):
///   "Haste
///    Whenever you discard a card, this creature deals 1 damage to each
///    opponent.
///    {1}{R}, Discard a card: Draw a card. Activate only if this creature is
///    attacking."
///
/// A red discard-payoff attacker: every discard it sees pings each opponent,
/// and while it's swinging it can loot at instant speed. Pairs with its OWN
/// activated ability (each {1}{R} discard both draws AND pings) and any other
/// rummage / loot / cycling effect.
///
/// ## Shape source
/// Card identity (name, {1}{R}{R}, 2/4, Creature — Minotaur Pirate, Haste) is
/// loaded from <c>Majik.Core/CardData/Cards/glint-horn-buccaneer.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/> (Haste surfaces as a
/// <see cref="KeywordAbility"/> marker, CR 702.10). The discard trigger and the
/// attacking-only loot ability are layered on here — the JSON ability schema
/// expresses neither a "whenever you discard" damage trigger nor an
/// "activate only if attacking" gated activated ability.
///
/// ## Implemented (v1)
///
/// - <b>2/4 Creature — Minotaur Pirate at {1}{R}{R} with Haste (CR 702.10).</b>
///
/// - <b>Discard trigger (CR 603.1 / 701.8)</b> — "Whenever you discard a card,
///   this creature deals 1 damage to each opponent." Wired over the dedicated
///   <see cref="DiscardedEvent"/> surface via <see cref="Triggers.OnDiscard"/>,
///   gated to the controller ("you discard" — CR 109.5). On resolution each
///   opponent of the controller takes 1 damage (CR 119.3) via
///   <see cref="Fx.DealDamage"/>. The "each opponent" clause (CR 109.5 — no
///   targets, global) reads from the LIVE resolution context
///   (<see cref="ContextOpponents.Of"/>) rather than a captured resolver, so the
///   ping is correct on the production routed build (resolver-null bug class —
///   mirrors Marauding Blight-Priest / Hired Claw). Unlike Conspiracy Theorist
///   there is NO nonland gate — CR 701.8 "discard a card" counts every card type.
///
/// - <b>{1}{R}, Discard a card: Draw a card (CR 602)</b> — an ordinary
///   activated ability with a <see cref="ManaCostCost"/> of {1}{R} + a
///   <see cref="DiscardACardCost"/> (CR 117.1 / 701.16a). On resolution the
///   controller draws a card (CR 121.3 via <see cref="Fx.DrawCards"/>). The
///   discard cost routes through the central discard chokepoint
///   (<see cref="Fx.DiscardCard"/>) so it ALSO feeds the "whenever you discard"
///   trigger above — activating this ability both draws AND pings each opponent.
///   <b>"Activate only if this creature is attacking" (CR 602.5c)</b> — wired as
///   the ability's <c>canActivateCheck</c> gate, which re-reads the live combat
///   each call: the ability is activatable only while this creature is among the
///   current combat's attackers (CR 508). The same combat-membership predicate
///   pattern Adanto Vanguard's while-attacking pump uses.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The discard trigger is attached
///   as a marker (not registered; no event bus) and the loot ability is attached
///   with a null combat source (so its "attacking" gate reports false). Suitable
///   for dispatcher / shape / cost-stack tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?, CombatManager?)"/> —
///   fully wired: the discard trigger registers with the
///   <see cref="TriggerManager"/> so a controller-scoped
///   <see cref="DiscardedEvent"/> queues it, and the loot ability's
///   "attacking" gate reads the live <see cref="CombatManager"/>.
///
/// CR rule references: 205.3m (Minotaur / Pirate subtypes), 702.10 (Haste),
/// 603.1 / 701.8 (discard trigger), 119.3 (damage to each opponent),
/// 109.5 (you / each opponent), 602.5c (activate only if), 508 (attacking),
/// 117.1 / 701.16a (discard cost), 121.3 (draw).
/// </summary>
[CardName("Glint-Horn Buccaneer")]
public static class GlintHornBuccaneerFactory
{
    public const string CardName = "Glint-Horn Buccaneer";
    public const string Slug = "glint-horn-buccaneer";

    /// <summary>Damage dealt to each opponent per discard (CR 119.3).</summary>
    public const int DamagePerOpponent = 1;

    /// <summary>Mana component of the loot activated ability (CR 602).</summary>
    public const string LootManaCost = "{1}{R}";

    /// <summary>
    /// Construct Glint-Horn Buccaneer with no live runtime services (the shape /
    /// dispatcher path). The discard trigger is attached as a marker (not
    /// registered — no bus, so no ping accrues) and the loot ability is attached
    /// with a null combat source, so its "activate only if attacking" gate
    /// reports false. Suitable for factory-shape / dispatch / cost-stack tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, combat: null);

    /// <summary>
    /// Construct Glint-Horn Buccaneer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved — the discard trigger is driven by the
    /// supplied <paramref name="triggers"/> manager off bus-published
    /// <see cref="DiscardedEvent"/>s. May be null.</param>
    /// <param name="triggers">TriggerManager the discard trigger registers with
    /// so a controller-scoped <see cref="DiscardedEvent"/> queues it
    /// (CR 603.3). May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="combat">Combat manager whose current attacker set the loot
    /// ability's "activate only if this creature is attacking" gate consults
    /// (CR 602.5c / 508). May be null — the gate then reports "not attacking"
    /// (the ability is never activatable).</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        AddDiscardPingTrigger(card, owner, triggers);
        AddAttackingLootAbility(card, owner, combat);

        _ = eventBus;
        return card;
    }

    // -----------------------------------------------------------------------
    // Discard trigger — "Whenever you discard a card, this creature deals 1
    // damage to each opponent." (CR 603.1 / 701.8.)
    // -----------------------------------------------------------------------
    private static void AddDiscardPingTrigger(
        Creature card,
        Player owner,
        TriggerManager? triggers)
    {
        var pingEffect = new Effect(
            $"{CardName}: deal {DamagePerOpponent} damage to each opponent (you discarded a card)",
            ctx =>
            {
                // "Each opponent" is read from the LIVE resolution context —
                // NOT a captured resolver, which would be null on the routed
                // prod build and make the ping INERT in real games (resolver-null
                // bug class; mirrors Marauding Blight-Priest / Hired Claw).
                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    // CR 119.3 — Fx.DealDamage routes Player → Player.LoseLife
                    // (CR 119.8). No nonland gate (CR 701.8 — "discard a card"
                    // counts every card type).
                    Fx.DealDamage(opp, DamagePerOpponent);
                }
                return ValueTask.CompletedTask;
            });

        var discardTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            // CR 701.8 / 109.5 — "Whenever you discard a card" over the
            // dedicated DiscardedEvent surface, gated to the controller.
            condition: Triggers.OnDiscard(card.Controller ?? owner),
            effects: new IEffect[] { pingEffect },
            // CR 113.6 — the ability functions only from the battlefield.
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(discardTrigger);
        triggers?.RegisterTriggeredAbility(discardTrigger);
    }

    // -----------------------------------------------------------------------
    // Loot ability — "{1}{R}, Discard a card: Draw a card. Activate only if
    // this creature is attacking." (CR 602 / 602.5c.)
    // -----------------------------------------------------------------------
    private static void AddAttackingLootAbility(
        Creature card,
        Player owner,
        CombatManager? combat)
    {
        var drawEffect = new Effect(
            $"{CardName}: draw a card (CR 121.3)",
            () => Fx.DrawCards(card.Controller ?? owner, 1));

        var loot = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                // CR 602 — {1}{R} + discard a card. The DiscardACardCost routes
                // through the central discard chokepoint (Fx.DiscardCard), so
                // paying it ALSO feeds the "whenever you discard" trigger above.
                new ManaCostCost(LootManaCost),
                new DiscardACardCost(),
            },
            effects: new IEffect[] { drawEffect },
            // CR 602.5c — "Activate only if this creature is attacking." The
            // gate re-reads the live combat each call (CR 508).
            canActivateCheck: () => IsAttacking(combat, card));

        card.AddAbility(loot);
    }

    /// <summary>
    /// CR 508 — true iff <paramref name="creature"/> is among the current
    /// combat's attackers. Null / ended-combat → not attacking (the loot
    /// ability is not activatable). Same combat-membership read as Adanto
    /// Vanguard's while-attacking pump.
    /// </summary>
    private static bool IsAttacking(CombatManager? combat, Creature creature)
    {
        var current = combat?.CurrentCombat;
        if (current == null) return false;
        foreach (var attacker in current.Attackers)
        {
            if (ReferenceEquals(attacker.Creature, creature)) return true;
        }
        return false;
    }
}
