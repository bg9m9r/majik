using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Adanto Vanguard (Ixalan, {1}{W}). Creature —
/// Vampire Soldier 1/1. Oracle text (verified against Scryfall):
///   "As long as this creature is attacking, it gets +2/+0.
///    Pay 4 life: This creature gains indestructible until end of turn.
///    (Damage and effects that say "destroy" don't destroy it.)"
///
/// The base shape (name, Creature, Vampire/Soldier subtypes, {1}{W}, 1/1)
/// is materialised from the embedded JSON definition
/// (<c>adanto-vanguard.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed behaviours
/// are layered on here — the JSON <c>AbilityDefinition</c> schema doesn't
/// express conditional static P/T effects or pay-life activated abilities,
/// so they live in the factory (same posture as the other JSON-backed
/// cards whose behaviour outgrows the schema, e.g.
/// <see cref="StormscaleScionFactory"/>).
///
/// ## Implemented (v1)
/// - <b>"As long as this creature is attacking, it gets +2/+0"
///   (CR 613.3c static P/T modifier)</b>: a
///   <see cref="WhileAttackingPumpEffect"/> registered on the supplied
///   <see cref="ContinuousEffectsService"/>. It applies +2/+0 (Layer 7c)
///   only while the Vanguard is among the current combat's attackers —
///   the "is attacking" predicate reads
///   <see cref="CombatManager.CurrentCombat"/>'s attacker set live on every
///   layer recomputation, so the bonus appears on attacker declaration
///   (CR 508.1) and lifts the moment combat ends. Attacking it is 3/1.
/// - <b>"Pay 4 life: This creature gains indestructible until end of turn"
///   (CR 602 activated ability)</b>: an <see cref="ActivatedAbility"/> with
///   a single <see cref="PayLifeCost"/> of 4 (CR 119.4 — can't pay life you
///   don't have; gates activation). Resolution registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Indestructible"
///   (CR 702.12 / 613.1f Layer 6, expiring at cleanup CR 514.2) — same
///   grant primitive as <see cref="BorosCharmFactory"/> /
///   <see cref="SelflessSpiritFactory"/>. The ability has no mana
///   component and may be activated any number of times (CR 602.2a) while
///   the controller has ≥4 life.
///
/// ## Deferred (v1 gaps)
/// - <b>No-service / no-combat shape path</b>: the shape-only
///   <see cref="Create(Player)"/> overload attaches the activated ability
///   structurally but registers no continuous effects (no layers service);
///   the attacking pump and the indestructible grant are no-ops on that
///   path. Functional behaviour requires the wiring overload with a live
///   <see cref="ContinuousEffectsService"/> + <see cref="CombatManager"/>.
/// - <b>Pump persistence across combat-object churn</b>: the pump effect is
///   registered once and stays on the service; its "is attacking" predicate
///   re-reads the current combat each compute, so it tracks correctly across
///   multiple combats without re-registration (it never expires —
///   <see cref="WhileAttackingPumpEffect.IsActive"/> ≡ true — so
///   <see cref="ContinuousEffectsService.Prune"/> won't drop it).
/// </summary>
[CardName("Adanto Vanguard")]
public static class AdantoVanguardFactory
{
    public const string CardName = "Adanto Vanguard";
    public const string Slug = "adanto-vanguard";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>Power bonus while attacking (CR 613.3c).</summary>
    public const int AttackingPowerBonus = 2;

    /// <summary>Toughness bonus while attacking.</summary>
    public const int AttackingToughnessBonus = 0;

    /// <summary>Life paid to gain indestructible until end of turn (CR 119.4).</summary>
    public const int IndestructibleLifeCost = 4;

    /// <summary>
    /// Construct Adanto Vanguard with no live wiring. The pay-life
    /// indestructible ability is attached structurally; the "while
    /// attacking" pump is NOT registered (no continuous-effects service)
    /// and activating the ability grants nothing (no service to register
    /// the EOT keyword against). Suitable for shape / dispatcher tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null, combat: null);

    /// <summary>
    /// Construct a fully-wired Adanto Vanguard.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service the "while attacking"
    /// +2/+0 static is registered against, and the EOT indestructible grant
    /// is registered against on activation. Pass null to skip both (the
    /// activated ability is still attached structurally).</param>
    /// <param name="combat">Combat manager whose current attacker set the
    /// "is attacking" predicate consults. May be null — the predicate then
    /// reports "not attacking" (no pump), but the static is still registered
    /// and starts pumping as soon as a combat manager-backed predicate would
    /// see the Vanguard attacking. (In practice the live game always supplies
    /// one.)</param>
    public static Creature Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        CombatManager? combat)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Vampire/Soldier subtypes, {1}{W}, 1/1). The JSON carries no
        // abilities — both printed behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // "As long as this creature is attacking, it gets +2/+0."
        // CR 613.3c characteristic-modifying static. Registered once; the
        // injected predicate re-reads the current combat each compute, so
        // the buff appears on attacker declaration and lifts when combat
        // ends. See WhileAttackingPumpEffect for the Prune-safe rationale.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            continuousEffects.Register(new WhileAttackingPumpEffect(
                source: card,
                power: AttackingPowerBonus,
                toughness: AttackingToughnessBonus,
                isAttacking: c => IsAttacking(combat, c)));
        }

        // ----------------------------------------------------------------
        // "Pay 4 life: This creature gains indestructible until end of turn."
        // CR 602 activated ability. Cost = PayLifeCost(4) (CR 119.4 gates
        // on LifeTotal >= 4); resolution grants "Indestructible" as a
        // Layer-6 EOT keyword (CR 702.12 / 514.2). No mana component;
        // repeatable while the controller can pay (CR 602.2a).
        // ----------------------------------------------------------------
        var grantEffect = new Effect(
            $"{CardName}: gains indestructible until end of turn (CR 702.12)",
            () =>
            {
                continuousEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(card, "Indestructible"));
            });

        var indestructibleAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new PayLifeCost(IndestructibleLifeCost) },
            effects: new IEffect[] { grantEffect });

        card.AddAbility(indestructibleAbility);

        return card;
    }

    /// <summary>
    /// True iff <paramref name="creature"/> is among the current combat's
    /// attackers (CR 508). Null/ended-combat → not attacking.
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
