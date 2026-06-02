using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TeeteringPeaksFactory"/> (Zendikar, mono-red
/// "enters-tapped pump land").
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, target creature gets +2/+0 until end of turn.
///    {T}: Add {R}."
///
/// The enters-tapped shell mirrors the Refuge cycle
/// (<see cref="AkoumRefugeFactory"/>); the ETB targeted +2/+0 pump mirrors
/// the hand-rolled targeted ETB trigger of <see cref="AbolethSpawnFactory"/>
/// composed with the <see cref="PumpUntilEndOfTurnEffect"/> primitive used by
/// the pump-template family (e.g. <see cref="DistortionStrikeFactory"/>).
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - One single-colour mana ability — {R} (CR 605.1a).
/// - One battlefield-active ETB triggered ability with a 1..1
///   "target creature" request (CR 603.6a).
/// - ETB effect: the chosen creature gets +2/+0 until end of turn
///   (CR 613.1g layer 7c; CR 514.2 — expires in cleanup).
/// - CR 608.2b — an illegal target (off-battlefield) at resolution fizzles.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> / the
/// two-arg <see cref="ReplacementBus"/> overload — same posture as the
/// Refuge cycle (not asserted here).
/// </summary>
[Trait("Color", "R")]
public class TeeteringPeaksTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TeeteringPeaks_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Teetering Peaks", _alice);

        land.Name.Should().Be("Teetering Peaks");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should()
            .BeFalse("Teetering Peaks is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TeeteringPeaks_HasManaAbility_ForRed()
    {
        var land = (Land)NamedCardFactory.Create("Teetering Peaks", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m =>
                m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void TeeteringPeaks_EtbTrigger_IsBattlefieldActive_WithCreatureTarget()
    {
        var land = (Land)NamedCardFactory.Create("Teetering Peaks", _alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
        etb.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void TeeteringPeaks_Etb_PumpsTargetTwoPowerZeroToughness()
    {
        // CR 613.1g layer 7c — +2/+0; CR 514.2 — until end of turn.
        var land = (Land)NamedCardFactory.Create("Teetering Peaks", _alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.ActiveEffects = new ContinuousEffectsService();

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });
        foreach (var e in etb.Effects) e.Execute();

        bear.GetPower().Should().Be(4, "Teetering Peaks' ETB gives +2 power");
        bear.GetToughness().Should().Be(2, "the pump is +2/+0 — toughness unchanged");
    }

    [Fact]
    public void TeeteringPeaks_Etb_IllegalTargetFizzles()
    {
        // CR 608.2b — a target no longer on the battlefield at resolution
        // fizzles cleanly (no throw, no pump).
        var land = (Land)NamedCardFactory.Create("Teetering Peaks", _alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Graveyard); // not on the battlefield
        bear.ActiveEffects = new ContinuousEffectsService();

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };

        act.Should().NotThrow();
        bear.GetPower().Should().Be(2, "an illegal target receives no pump");
    }
}
