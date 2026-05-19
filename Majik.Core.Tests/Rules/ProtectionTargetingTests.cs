using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class ProtectionTargetingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RedBolt_CannotTarget_CreatureWithProtectionFromRed()
    {
        var bolt = new Instant("Lightning Bolt", "R");
        var knight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        knight.AddAbility(new ProtectionAbility("red"));

        TargetLegality.CanBeTargetedBy(knight, bolt, _bob).Should().BeFalse();
    }

    [Fact]
    public void BlueRemoval_CanTarget_CreatureWithProtectionFromRed()
    {
        var counter = new Instant("Removal", "1U");
        var knight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        knight.AddAbility(new ProtectionAbility("red"));

        TargetLegality.CanBeTargetedBy(knight, counter, _bob).Should().BeTrue();
    }

    [Fact]
    public void ColorlessSource_BypassesColorProtection()
    {
        var artifact = new Instant("Artifact Pulse", "2"); // no coloured pips
        var knight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        knight.AddAbility(new ProtectionAbility("red"));

        TargetLegality.CanBeTargetedBy(knight, artifact, _bob).Should().BeTrue();
    }
}
