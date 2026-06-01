using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
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

    // -----------------------------------------------------------------------
    // CR 105.3 / 613.1e — the SOURCE's colour can be changed by a Layer-5
    // colour-changing effect (Painter's Servant on a spell, an animated land
    // "is all colours"). Protection-from-colour targeting must read the
    // EFFECTIVE colour of the source, not its printed colour.
    // -----------------------------------------------------------------------

    [Fact]
    public void ColourChangedSource_MadeRed_CannotTarget_ProtectionFromRed()
    {
        var svc = new ContinuousEffectsService();

        var knight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        knight.AddAbility(new ProtectionAbility("red"));

        // A colourless source spell-object (no coloured pips) that a Layer-5
        // effect has turned red — printed colour would be legal, effective
        // colour is not.
        var source = new Creature("Painted Bolt", "1", 0, 0)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        svc.Register(new SetColorsEffect(
            source: source,
            scope: p => ReferenceEquals(p, source),
            colors: new[] { ManaColor.Red }));

        TargetLegality.CanBeTargetedBy(knight, source, _bob).Should().BeFalse(
            "the source's EFFECTIVE colour (Layer-5 red) triggers protection from red");
    }

    [Fact]
    public void ColourChangedSource_MadeBlue_StillTargets_ProtectionFromRed()
    {
        var svc = new ContinuousEffectsService();

        var knight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        knight.AddAbility(new ProtectionAbility("red"));

        // Printed red, but a Layer-5 SET effect makes it ONLY blue → protection
        // from red no longer applies (the printed-colour read would wrongly
        // reject it).
        var source = new Creature("Recoloured Bolt", "R", 0, 0)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        svc.Register(new SetColorsEffect(
            source: source,
            scope: p => ReferenceEquals(p, source),
            colors: new[] { ManaColor.Blue }));

        TargetLegality.CanBeTargetedBy(knight, source, _bob).Should().BeTrue(
            "the source's effective colour is blue, so protection from red does not apply");
    }
}
