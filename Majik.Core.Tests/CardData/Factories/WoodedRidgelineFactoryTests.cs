using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WoodedRidgelineFactory"/> — Wooded Ridgeline, a
/// Bloomburrow common dual (Mountain Forest). Oracle text:
///   "({T}: Add {R} or {G}.)
///    This land enters tapped."
///
/// Covers:
/// - Identity (Land + both printed subtypes Mountain/Forest, non-Basic).
/// - Two mana abilities producing {R} and {G} respectively (CR 605.1).
/// - No activated / triggered abilities beyond mana.
/// - Unconditional ETB-tapped via <see cref="EntersTappedReplacement"/>
///   (CR 614.1c): always enters tapped regardless of board state.
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Single-arg path registers no replacement.
/// </summary>
[Trait("Color", "C")]
public class WoodedRidgelineFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------
    [Fact]
    public void WoodedRidgeline_IsNotBasic()
    {
        var land = WoodedRidgelineFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Wooded Ridgeline is a nonbasic dual land");
    }

    [Fact]
    public void WoodedRidgeline_HasTwoManaAbilities_ProducingRG()
    {
        var land = (Land)NamedCardFactory.Create("Wooded Ridgeline", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Wooded Ridgeline taps for {R} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void WoodedRidgeline_HasNoActivatedOrTriggeredAbilities()
    {
        var land = WoodedRidgelineFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "plain tapped duals have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Wooded Ridgeline has no triggered abilities (no lifegain rider)");
    }

    // -----------------------------------------------------------------------
    // Unconditional ETB-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void WoodedRidgeline_EntersTapped_WithEmptyBoard()
    {
        var bus = new ReplacementBus();
        var land = WoodedRidgelineFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "Wooded Ridgeline always enters tapped");
    }

    [Fact]
    public void WoodedRidgeline_EntersTapped_RegardlessOfBasics()
    {
        // Unlike a battle land, the tapped clause is unconditional: even with
        // a board full of basics, Wooded Ridgeline still enters tapped.
        var bus = new ReplacementBus();
        SeedBasic("Mountain", _alice);
        SeedBasic("Forest", _alice);
        SeedBasic("Plains", _alice);
        var land = WoodedRidgelineFactory.Create(_alice, replacements: bus);

        var after = ApplyEtb(bus, land, _alice);

        after.EntersTapped.Should().BeTrue(
            "the enters-tapped clause is unconditional");
    }

    // -----------------------------------------------------------------------
    // Shape-only single-arg path
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void WoodedRidgeline_Create_ThrowsOnNullOwner()
    {
        var act = () => WoodedRidgelineFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void SeedBasic(string name, Player owner)
    {
        var basic = (Land)NamedCardFactory.Create(name, owner);
        owner.Zones.Battlefield.AddCard(basic);
        basic.SetZone(ZoneType.Battlefield);
    }

    private static ZoneMoveIntent ApplyEtb(ReplacementBus bus, Land land, Player controller)
    {
        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        return after!;
    }
}
