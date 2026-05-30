using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

/// <summary>
/// CR 105.3 / 702.16e — integration coverage that the Layer-5 colour-changing
/// pass flows through the combat protection checks via
/// <see cref="Permanent.GetEffectiveColors"/>. Proves the new effective-colour
/// rewire changes behaviour when an effect is active AND is identical to the
/// printed colour when none is (no regression to existing protection cards).
/// </summary>
public class Layer5ColorCombatTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GreenBlockerMadeAllColors_CannotBlockProtectionFromRed()
    {
        var svc = new ContinuousEffectsService();

        var whiteKnight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        whiteKnight.AddAbility(new ProtectionAbility("red"));

        // Printed green — would normally be a legal blocker (no red).
        var greenBlocker = new Creature("Green Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        greenBlocker.ActiveEffects = svc;

        // CR 105.2c / 613.1e — "is all colors" → now also red → can't block.
        svc.Register(SetColorsEffect.AllColors(
            source: greenBlocker, scope: p => ReferenceEquals(p, greenBlocker)));

        var v = new CombatValidator();
        var attacker = new Attacker(whiteKnight, _bob);

        v.CanBlock(greenBlocker, attacker, _bob).Should().BeFalse();
    }

    [Fact]
    public void GreenBlocker_NoColorEffect_StillBlocksProtectionFromRed()
    {
        // No Layer-5 effect: effective colour == printed green → legal block.
        var svc = new ContinuousEffectsService();

        var whiteKnight = new Creature("White Knight", "WW", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        whiteKnight.AddAbility(new ProtectionAbility("red"));

        var greenBlocker = new Creature("Green Bear", "1G", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        greenBlocker.ActiveEffects = svc;

        var v = new CombatValidator();
        var attacker = new Attacker(whiteKnight, _bob);

        v.CanBlock(greenBlocker, attacker, _bob).Should().BeTrue();
    }
}
