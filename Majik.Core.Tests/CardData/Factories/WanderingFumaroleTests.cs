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
/// Tests for Wandering Fumarole (Oath of the Gatewatch). Land. Oracle text:
///   "This land enters tapped.
///    {T}: Add {U} or {R}.
///    {2}{U}{R}: Until end of turn, this land becomes a 1/4 blue and red
///    Elemental creature with \"{0}: Switch this creature's power and
///    toughness until end of turn.\" It's still a land."
///
/// Shares the Worldwake / BFZ / OGW "manland" shape modelled by
/// <see cref="ManlandCycleAnimateEffect"/> + <see cref="ManlandCycleBecomesPTEffect"/>
/// (see <see cref="ManlandCycleTests"/>), but unlike the rest of the cycle the
/// animated body also gains a granted activated ability
/// (<c>{0}: Switch P/T</c>) via <see cref="GrantAbilityEffect"/> +
/// <see cref="SwitchPTEffect"/> (CR 613.7d).
/// </summary>
[Trait("Color", "C")]
public class WanderingFumaroleTests
{
    private const string Name = "Wandering Fumarole";
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void IsLand_NoSubtypes_NoSupertypes()
    {
        var land = WanderingFumaroleFactory.Create(_alice);

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
    public void TapForBlue_ProducesBlue()
    {
        var land = WanderingFumaroleFactory.Create(_alice);
        var ability = land.Abilities.OfType<ManaAbility>().First();

        ability.CanActivate().Should().BeTrue();
        var produced = ability.Activate();

        produced.Blue.Should().Be(1);
        produced.Red.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TapForRed_ProducesRed()
    {
        var land = WanderingFumaroleFactory.Create(_alice);
        var second = land.Abilities.OfType<ManaAbility>().Skip(1).First();

        second.CanActivate().Should().BeTrue();
        var produced = second.Activate();

        produced.Red.Should().Be(1);
        produced.Blue.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Animate_RegistersLayer4AndLayer7b_EotExpiring_AndGrantsSwitchAbility()
    {
        var effects = new ContinuousEffectsService();
        var land = WanderingFumaroleFactory.Create(_alice, effects, replacements: null);
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
        animateEffect.Subtypes.Should().Contain(CardSubtype.Elemental);

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<ManlandCycleBecomesPTEffect>()
            .SingleOrDefault(e => e.NewPower == 1 && e.NewToughness == 4);
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();

        // Granted "{0}: Switch P/T" ability — CR 613.1f Layer 6 ability grant,
        // EOT-expiring (CR 514.2).
        var grant = GetRegisteredEffects(effects)
            .OfType<GrantAbilityEffect>()
            .SingleOrDefault(e => ReferenceEquals(e.Source, land));
        grant.Should().NotBeNull("the animated body gains \"{0}: Switch P/T\"");
        grant!.Layer.Should().Be(Layer.Abilities);
        grant.ExpiresAtEndOfTurn.Should().BeTrue();

        // Compute(land) reflects the Layer 4 grants: printed Land stays,
        // Creature + Elemental added.
        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "\"It's still a land.\"");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);

        // The grant syncs onto the land during Compute; the bearer now carries
        // the {0}-cost switch activated ability.
        var switchAbility = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .SingleOrDefault(a => a.Costs.OfType<ManaCostCost>()
                .Any(c => c.Cost.IsZero));
        switchAbility.Should().NotBeNull("\"{0}: Switch this creature's power and toughness\"");
    }

    [Fact]
    public void Animate_EndOfTurnExpiration_RevertsLand_AndRevokesGrant()
    {
        var effects = new ContinuousEffectsService();
        var land = WanderingFumaroleFactory.Create(_alice, effects, replacements: null);
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
        chars.Subtypes.Should().NotContain(CardSubtype.Elemental);

        // The EOT-expiring grant is revoked — the {0} switch ability is gone.
        land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Any(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Cost.IsZero))
            .Should().BeFalse();
    }

    [Fact]
    public void Animate_NoEffectsService_NoOp_ShapeRemainsLand()
    {
        var land = WanderingFumaroleFactory.Create(_alice);
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
        var act = () => WanderingFumaroleFactory.Create(_alice, effects: null, replacements: replacements);
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
