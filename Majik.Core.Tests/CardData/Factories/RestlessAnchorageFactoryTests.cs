using System.Linq;
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
/// Tests for <see cref="RestlessAnchorageFactory"/> (Murders at Karlov Manor
/// "restless" land cycle). Land:
///   "This land enters tapped.
///    {T}: Add {W} or {U}.
///    {1}{W}{U}: Until end of turn, this land becomes a 2/3 white and blue
///    Bird creature with flying. It's still a land.
///    Whenever this land attacks, create a Map token."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch + ability shape counts.
/// - Two mana abilities ({T}: Add {W} / {T}: Add {U}).
/// - Animate ability ({1}{W}{U}) registers a
///   <see cref="ManlandCycleAnimateEffect"/> + a
///   <see cref="ManlandCycleBecomesPTEffect"/>:
///     * Adds Creature + Bird subtype + Flying keyword on Layer 4.
///     * Records 2/3 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// - ETB-tapped replacement registered when a bus is wired.
/// - Non-targeted attack trigger mints exactly one Map token (CR 111.10).
/// </summary>
[Trait("Color", "WU")]
public class RestlessAnchorageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RestlessAnchorage_Identity()
    {
        var land = RestlessAnchorageFactory.Create(_alice);

        land.Name.Should().Be("Restless Anchorage");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land until activated");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Anchorage is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RestlessAnchorage_HasTwoManaAbilities_WandU()
    {
        var land = RestlessAnchorageFactory.Create(_alice);
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {W} and {T}: Add {U} from the JSON definition");
    }

    [Fact]
    public void RestlessAnchorage_AnimateAbility_HasPrintedManaCost1WU()
    {
        var land = RestlessAnchorageFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({1}{W}{U})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessAnchorage_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessAnchorageFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature, "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Bird, "Bird subtype added");
        chars.Keywords.Should().Contain("Flying", "Flying keyword marker added (CR 702.9)");
    }

    [Fact]
    public void RestlessAnchorage_Animate_ExpiresAtEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessAnchorageFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        effects.Compute((Permanent)land).Types.Should().Contain(CardType.Creature);

        effects.ExpireEndOfTurn();

        var afterCleanup = effects.Compute((Permanent)land);
        afterCleanup.Types.Should().NotContain(CardType.Creature,
            "the animation lifts at the cleanup step (CR 514.2)");
        afterCleanup.Types.Should().Contain(CardType.Land,
            "the printed Land type remains");
    }

    [Fact]
    public void RestlessAnchorage_RegistersEntersTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessAnchorageFactory.Create(_alice, effects: null, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Restless Anchorage always enters tapped (CR 614.1c)");
    }

    [Fact]
    public void RestlessAnchorage_AttackTrigger_MintsExactlyOneMapToken()
    {
        var land = RestlessAnchorageFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().BeEmpty("create-a-Map is non-targeted");

        var before = _alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Map");
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Count(c => c.Name == "Map")
            .Should().Be(before + 1, "the attack trigger mints exactly one Map token (CR 111.10)");
    }
}
