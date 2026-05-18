using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

/// <summary>
/// CR 608.2b — on resolution, illegal targets are removed. If no legal
/// targets remain, the spell is countered. This file demonstrates the
/// recheck pattern using <see cref="TargetLegality.IsLegal"/> directly;
/// SpellCastFlow integration that calls these hooks lives in Phase 15.
/// </summary>
public class ResolutionRecheckTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TargetLeftBattlefield_NoLongerLegal_AtResolution()
    {
        var spec = new TargetSpec("creature").Creatures();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield,
        };

        TargetLegality.IsLegal(spec, bear, _alice).Should().BeTrue();

        // Simulate Bear dying / leaving battlefield before spell resolves.
        bear.Zone = ZoneType.Graveyard;

        TargetLegality.IsLegal(spec, bear, _alice).Should().BeFalse();
    }

    [Fact]
    public void TargetGainsHexproof_BetweenCastAndResolution_BecomesIllegal()
    {
        var spec = new TargetSpec("creature").Creatures();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield,
        };

        TargetLegality.IsLegal(spec, bear, _alice).Should().BeTrue();

        // Defender gives hexproof via instant before resolution.
        bear.AddAbility(new KeywordAbility("Hexproof", bear, _bob));

        TargetLegality.IsLegal(spec, bear, _alice).Should().BeFalse();
    }

    [Fact]
    public void AllTargetsIllegal_SpellShouldBeCountered_Pattern()
    {
        var spec = new TargetSpec("creature").Creatures();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield,
        };
        var chosenTargets = new object[] { bear };

        // At cast time — legal
        chosenTargets.All(t => TargetLegality.IsLegal(spec, t, _alice)).Should().BeTrue();

        // Bear dies before resolution
        bear.Zone = ZoneType.Graveyard;

        // At resolution — all illegal → spell would be countered (CR 608.2b)
        chosenTargets.All(t => TargetLegality.IsLegal(spec, t, _alice)).Should().BeFalse();
    }
}
