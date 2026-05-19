using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class KeywordExpansionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------- Hexproof / Shroud ----------

    [Fact]
    public void Hexproof_OpponentCannotTarget()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bear.AddAbility(new KeywordAbility("Hexproof"));

        TargetLegality.CanBeTargetedBy(bear, _bob).Should().BeFalse();
        TargetLegality.CanBeTargetedBy(bear, _alice).Should().BeTrue();
    }

    [Fact]
    public void Shroud_NoOneCanTarget()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bear.AddAbility(new KeywordAbility("Shroud"));

        TargetLegality.CanBeTargetedBy(bear, _bob).Should().BeFalse();
        TargetLegality.CanBeTargetedBy(bear, _alice).Should().BeFalse();
    }

    // ---------- Flash ----------

    [Fact]
    public void Flash_AllowsInstantSpeedCast()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.AddAbility(new KeywordAbility("Flash"));
        TimingRules.CanCastAtInstantSpeed(bear).Should().BeTrue();
    }

    [Fact]
    public void NoFlash_VanillaCreature_OnlyAtSorcerySpeed()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        TimingRules.CanCastAtInstantSpeed(bear).Should().BeFalse();
    }

    [Fact]
    public void Instant_AlwaysAtInstantSpeed()
    {
        var bolt = new Instant("Bolt", "R");
        TimingRules.CanCastAtInstantSpeed(bolt).Should().BeTrue();
    }

    // ---------- Protection ----------

    [Fact]
    public void ProtectionFromRed_BlocksRedTargeting()
    {
        var knight = new Creature("White Knight", "WW", 2, 2);
        knight.AddAbility(new ProtectionAbility("red"));
        Protection.HasProtectionFromColor(knight, ManaColor.Red).Should().BeTrue();
        Protection.HasProtectionFromColor(knight, ManaColor.Blue).Should().BeFalse();
    }

    [Fact]
    public void ProtectionFromCreatures_BlocksCreatureTypes()
    {
        var pegasus = new Creature("Pegasus", "1W", 2, 1);
        pegasus.AddAbility(new ProtectionAbility("creatures"));
        Protection.HasProtectionFromCardType(pegasus, CardType.Creature).Should().BeTrue();
    }
}
