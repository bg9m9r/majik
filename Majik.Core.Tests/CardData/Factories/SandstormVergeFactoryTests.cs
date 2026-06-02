using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SandstormVergeFactory"/> (Edge of Eternities,
/// Land — Desert).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "{T}: Add {C}.
///    {3}, {T}: Target creature can't block this turn. Activate only as a
///    sorcery."
///
/// Base shape (Land — Desert + {T}: Add {C}) is materialised from the embedded
/// JSON definition (<c>sandstorm-verge.json</c>); the {3}, {T} can't-block
/// activated ability is layered on by the factory. Mirrors the Tectonic Edge
/// utility-land posture plus the Earthshaker Khenra CannotBlock
/// <see cref="CombatRestrictionEffect"/> resolution.
/// </summary>
[Trait("Color", "C")]
public class SandstormVergeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SandstormVerge_Identity_LandDesert()
    {
        var land = SandstormVergeFactory.Create(_alice);

        land.Name.Should().Be("Sandstorm Verge");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue("printed type is Land — Desert");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Sandstorm Verge is a nonbasic land");
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void SandstormVerge_TapForC_ProducesColorless()
    {
        var land = SandstormVergeFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        // {C} parses into the Generic slot today (no dedicated Colorless
        // property on ManaCost — mirrors Strip Mine / Wasteland).
        produced.Generic.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {3}, {T}: Target creature can't block this turn. Activate only as a sorcery.
    // -----------------------------------------------------------------------

    [Fact]
    public void SandstormVerge_HasManaAbility_AndSingleCantBlockActivatedAbility()
    {
        var land = SandstormVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1, "{T}: Add {C}");

        var activated = CantBlockOf(land);

        activated.TargetRequests.Should().HaveCount(1);
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);
        activated.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void SandstormVerge_CantBlockAbility_IsSorcerySpeed_With3Cost()
    {
        var land = SandstormVergeFactory.Create(_alice);
        var activated = CantBlockOf(land);

        activated.IsSorcerySpeed.Should().BeTrue(
            "the printed rider is 'Activate only as a sorcery' (CR 117.1a / 307.5)");
        activated.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the activation cost includes one ManaCostCost ({3})");
    }

    [Fact]
    public void SandstormVerge_CantBlock_RegistersRestrictionOnLegalTarget()
    {
        var land = SandstormVergeFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var activated = CantBlockOf(land);
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in activated.Effects) effect.Execute();

        service.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeTrue(
            "the resolved ability locks the chosen creature out of blocking this turn");
    }

    [Fact]
    public void SandstormVerge_CantBlock_NoRestriction_WhenTargetLeftBattlefield()
    {
        var land = SandstormVergeFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        // Target left the battlefield between choose and resolve — CR 608.2b.
        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Graveyard);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var activated = CantBlockOf(land);
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in activated.Effects) effect.Execute();

        service.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeFalse(
            "a target no longer on the battlefield fails the CR 608.2b recheck");
    }

    [Fact]
    public void SandstormVerge_CantBlock_NoActiveEffects_DoesNotThrow()
    {
        var land = SandstormVergeFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        // Target with no ContinuousEffectsService wired — shape-only.
        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        // target.ActiveEffects is null.

        var activated = CantBlockOf(land);
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var effect in activated.Effects) effect.Execute(); };

        act.Should().NotThrow("the effect body guards on a null ActiveEffects");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ActivatedAbility CantBlockOf(Land land) =>
        land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
}
