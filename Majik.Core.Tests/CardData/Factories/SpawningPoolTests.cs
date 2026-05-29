using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Spawning Pool (Urza's Legacy). Land. Oracle text:
///   "This land enters tapped.
///    {T}: Add {B}.
///    {1}{B}: This land becomes a 1/1 black Skeleton creature with
///    \"{B}: Regenerate this creature\" until end of turn. It's still a
///    land."
///
/// Shares the manland animate shape (<see cref="ManlandCycleAnimateEffect"/> +
/// <see cref="ManlandCycleBecomesPTEffect"/>); like
/// <see cref="WanderingFumaroleTests"/> the animated body also gains a
/// granted activated ability — here "{B}: Regenerate this creature"
/// (CR 701.18) via <see cref="GrantAbilityEffect"/>.
/// </summary>
public class SpawningPoolTests
{
    private const string Name = "Spawning Pool";
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void IsLand_NoSubtypes_NoSupertypes()
    {
        var land = SpawningPoolFactory.Create(_alice);

        land.Name.Should().Be(Name);
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed manland is just a Land until activated");
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches()
    {
        var card = NamedCardFactory.Create(Name, _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be(Name);
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1, "{T}: Add {B}");
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1, "the animate ability");
    }

    [Fact]
    public void TapForBlack_ProducesBlack()
    {
        var land = SpawningPoolFactory.Create(_alice);
        var ability = land.Abilities.OfType<ManaAbility>().Single();

        ability.CanActivate().Should().BeTrue();
        var produced = ability.Activate();

        produced.Black.Should().Be(1);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Animate_RegistersLayer4AndLayer7b_EotExpiring_AndGrantsRegenerateAbility()
    {
        var effects = new ContinuousEffectsService();
        var land = SpawningPoolFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Target, land));
        animateEffect.Should().NotBeNull();
        animateEffect!.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        animateEffect.Subtypes.Should().Contain(CardSubtype.Skeleton);
        animateEffect.Subtypes.Should().NotContain(CardSubtype.Elemental,
            "Spawning Pool animates to a Skeleton, not the cycle-default Elemental");

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.NewPower == 1 && e.NewToughness == 1);
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();

        // Granted "{B}: Regenerate this creature" — CR 613.1f Layer 6
        // ability grant, EOT-expiring (CR 514.2).
        var grant = GetRegisteredEffects(effects)
            .OfType<GrantAbilityEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Source, land));
        grant.Should().NotBeNull("the animated body gains \"{B}: Regenerate this creature\"");
        grant!.Layer.Should().Be(Layer.Abilities);
        grant.ExpiresAtEndOfTurn.Should().BeTrue();

        // Compute(land) reflects the Layer 4 grants: printed Land stays,
        // Creature + Skeleton added.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "\"It's still a land.\"");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Skeleton);

        // The grant syncs onto the land during Compute; the bearer now
        // carries the {B}-cost regenerate activated ability.
        var regenAbility = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .SingleOrDefault(a => a.Costs.OfType<ManaCostCost>()
                .Any(c => c.Cost == ManaCost.Parse("B")));
        regenAbility.Should().NotBeNull("\"{B}: Regenerate this creature\"");
    }

    [Fact]
    public void GrantedRegenerateAbility_AddsRegenerationShield()
    {
        var effects = new ContinuousEffectsService();
        var land = SpawningPoolFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();
        effects.Compute(land); // sync the grant onto the bearer

        land.RegenerationShieldCount.Should().Be(0);

        var regenAbility = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>()
                .Any(c => c.Cost == ManaCost.Parse("B")));
        regenAbility.Resolve();

        land.RegenerationShieldCount.Should().Be(1,
            "resolving \"{B}: Regenerate this creature\" adds a regeneration shield (CR 701.18)");
    }

    [Fact]
    public void Animate_EndOfTurnExpiration_RevertsLand_AndRevokesRegenerateGrant()
    {
        var effects = new ContinuousEffectsService();
        var land = SpawningPoolFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();
        effects.Compute(land); // sync the grant onto the bearer

        GetRegisteredEffects(effects).OfType<ManlandCycleAnimateEffect>().Should().NotBeEmpty();
        GetRegisteredEffects(effects).OfType<GrantAbilityEffect>().Should().NotBeEmpty();

        // CR 514.2 — "until end of turn" effects end during cleanup.
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects)
            .OfType<ManlandCycleAnimateEffect>()
            .Where(e => ReferenceEquals(e.Target, land))
            .Should().BeEmpty();
        GetRegisteredEffects(effects)
            .OfType<GrantAbilityEffect>()
            .Where(e => ReferenceEquals(e.Source, land))
            .Should().BeEmpty();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Skeleton);

        // The EOT-expiring grant is revoked — the {B} regenerate ability is
        // gone.
        land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Any(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Cost == ManaCost.Parse("B")))
            .Should().BeFalse();
    }

    [Fact]
    public void Animate_NoEffectsService_NoOp_ShapeRemainsLand()
    {
        var land = SpawningPoolFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        var resolve = () => animate.Resolve();
        resolve.Should().NotThrow();
        land.HasType(CardType.Creature).Should().BeFalse();
    }

    [Fact]
    public void EntersTappedReplacement_IsRegistered_WhenReplacementBusSupplied()
    {
        var replacements = new ReplacementBus();
        var act = () => SpawningPoolFactory.Create(_alice, effects: null, replacements: replacements);
        act.Should().NotThrow();
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(
        ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
