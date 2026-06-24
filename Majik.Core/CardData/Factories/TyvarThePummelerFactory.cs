using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tyvar, the Pummeler (Bloomburrow Commander /
/// Modern Horizons reprint frame). Legendary Creature — Elf Warrior 3/3,
/// mana cost {1}{G}{G}. Oracle text (verified against Scryfall):
///   "Tap another untapped creature you control: Tyvar gains indestructible
///    until end of turn. Tap it.
///    {3}{G}{G}: Creatures you control get +X/+X until end of turn, where X
///    is the greatest power among creatures you control."
///
/// The base shape (name, Legendary supertype, Creature, Elf/Warrior subtypes,
/// {1}{G}{G}, 3/3) is materialised from the embedded JSON definition
/// (<c>tyvar-the-pummeler.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The two printed activated
/// abilities are layered on here — the JSON <c>AbilityDefinition</c> schema
/// doesn't express a tap-another-creature cost nor a dynamic team pump, so
/// they live in the factory (same posture as
/// <see cref="AdantoVanguardFactory"/> / <see cref="HoldoutSettlementFactory"/>).
///
/// ## Implemented (v1)
/// - <b>"Tap another untapped creature you control: Tyvar gains
///   indestructible until end of turn." (CR 602 activated ability; the tap
///   on a non-source object is a tap-as-cost, CR 118.12)</b>: an
///   <see cref="ActivatedAbility"/> whose single cost is a
///   <see cref="TapAnotherUntappedCreatureCost"/> (CR 119.4 gates activation
///   when no other untapped, non-summoning-sick creature is available;
///   CR 302.6). Note the oracle reorders "Tap it." after the effect text,
///   but the tap is a COST (the colon precedes the effect) — paying the cost
///   taps the chosen creature. Resolution registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> granting "Indestructible"
///   to Tyvar (CR 702.12 / 613.1f Layer 6, expiring at cleanup CR 514.2) —
///   same grant primitive as <see cref="AdantoVanguardFactory"/> /
///   <see cref="SelflessSpiritFactory"/>. No mana component; repeatable while
///   another untapped creature can be tapped (CR 602.2a).
/// - <b>"{3}{G}{G}: Creatures you control get +X/+X until end of turn, where
///   X is the greatest power among creatures you control." (CR 602 activated
///   ability)</b>: an <see cref="ActivatedAbility"/> with a single
///   <see cref="ManaCostCost"/> of {3}{G}{G}. Resolution snapshots the
///   creatures the controller controls, computes X = the greatest power
///   among them (CR 608.2 — X is locked in as the ability resolves; a
///   negative/zero greatest power yields a no-op pump), and registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> of +X/+X (CR 613.1c Layer 7c,
///   expiring at cleanup CR 514.2) on each — the same dynamic team-pump shape
///   as <see cref="FinaleOfDevastationFactory"/>'s anthem rider.
///
/// ## Deferred (v1 gaps)
/// - <b>No-service shape path</b>: the parameterless
///   <see cref="Create(Player)"/> overload attaches both activated abilities
///   structurally. The indestructible grant and the team pump register
///   against each affected creature's <see cref="Permanent.ActiveEffects"/>
///   layer service (set by the live game); where a creature has no service
///   wired the grant/pump silently no-op rather than NRE'ing. Functional
///   behaviour requires creatures whose <see cref="Permanent.ActiveEffects"/>
///   is a live <see cref="ContinuousEffectsService"/>.
/// - <b>Agent prompt for which creature to tap</b>: the cost falls back to
///   the first eligible (untapped, no summoning sickness) creature via
///   <see cref="TapAnotherUntappedCreatureCost"/>'s deterministic pick;
///   agents/tests set <see cref="TapAnotherUntappedCreatureCost.Target"/> to
///   override — the same gap as the rest of the additional-cost family.
/// </summary>
[CardName("Tyvar, the Pummeler")]
public static class TyvarThePummelerFactory
{
    public const string CardName = "Tyvar, the Pummeler";
    public const string Slug = "tyvar-the-pummeler";

    /// <summary>Mana cost of the team-pump activated ability.</summary>
    public const string PumpAbilityCost = "{3}{G}{G}";

    /// <summary>Keyword granted by the tap-a-creature ability.</summary>
    private const string Indestructible = "Indestructible";

    /// <summary>
    /// Construct Tyvar, the Pummeler owned and controlled by
    /// <paramref name="owner"/> with both printed activated abilities attached.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary,
        // Creature, Elf/Warrior subtypes, {1}{G}{G}, 3/3). The JSON carries
        // no abilities — both printed activated abilities are layered below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        card.AddAbility(BuildIndestructibleAbility(card, owner));
        card.AddAbility(BuildTeamPumpAbility(card, owner));

        return card;
    }

    /// <summary>
    /// "Tap another untapped creature you control: Tyvar gains indestructible
    /// until end of turn." Cost = <see cref="TapAnotherUntappedCreatureCost"/>
    /// (CR 118.12 tap-as-cost on a non-source object); resolution grants
    /// Tyvar "Indestructible" until end of turn (CR 702.12 / 514.2).
    /// </summary>
    public static ActivatedAbility BuildIndestructibleAbility(Creature card, Player owner)
    {
        var grantEffect = new Effect(
            $"{CardName}: gains indestructible until end of turn (CR 702.12)",
            () =>
            {
                // Register against Tyvar's own layer service (set by the live
                // game). Shape-only path with no service no-ops cleanly.
                card.ActiveEffects?.Register(
                    new GrantKeywordUntilEndOfTurnEffect(card, Indestructible));
            });

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new TapAnotherUntappedCreatureCost(card) },
            effects: new IEffect[] { grantEffect });
    }

    /// <summary>
    /// "{3}{G}{G}: Creatures you control get +X/+X until end of turn, where X
    /// is the greatest power among creatures you control." Cost =
    /// <see cref="ManaCostCost"/>({3}{G}{G}); resolution computes X and pumps
    /// each creature the controller controls by +X/+X (CR 613.1c Layer 7c,
    /// CR 514.2).
    /// </summary>
    public static ActivatedAbility BuildTeamPumpAbility(Creature card, Player owner)
    {
        var pumpEffect = new Effect(
            $"{CardName}: creatures you control get +X/+X until end of turn (CR 608.2)",
            () => ApplyTeamPump(card.Controller ?? owner));

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(PumpAbilityCost) },
            effects: new IEffect[] { pumpEffect });
    }

    /// <summary>
    /// CR 608.2 — at resolution, X is the greatest power among creatures
    /// <paramref name="controller"/> controls; pump every such creature by
    /// +X/+X until end of turn. A non-positive greatest power makes the pump
    /// a no-op (a +0/+0 — or negative — buff still expires at cleanup but
    /// changes nothing meaningful). Creatures without a wired
    /// <see cref="ContinuousEffectsService"/> no-op cleanly.
    /// </summary>
    public static void ApplyTeamPump(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // Snapshot to a list so same-step registrations don't disturb the
        // enumeration (mirrors FinaleOfDevastationFactory.ApplyAnthemIfBig).
        var creatures = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();

        if (creatures.Count == 0) return;

        // X = greatest power among creatures you control (CR 608.2). Power is
        // read live so it already reflects any active layer effects.
        int x = creatures.Max(c => c.Power);

        if (x <= 0) return;

        foreach (var creature in creatures)
        {
            // CR 613.1c Layer 7c — +X/+X pump until end of turn. Shape-only
            // safety: a creature with no layer service silently no-ops.
            creature.ActiveEffects?.Register(
                new PumpUntilEndOfTurnEffect(creature, x, x));
        }
    }
}
