using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 114 / CR 613.7c — the emblem-sourced static anthem
/// (<see cref="EmblemAnthemEffect"/>): "[Subtype]s you control get +P/+T".
/// Unlike a battlefield lord, the source is an emblem in the command zone, so
/// the anthem is always active (no zone gate) and scoped to a player.
/// </summary>
[Trait("Color", "M")]
public class EmblemAnthemEffectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Creature MakeNinja(Player controller, ContinuousEffectsService svc)
    {
        var ninja = new Creature("Ninja Bear", "{1}{U}", 2, 2,
            subtypes: new[] { CardSubtype.Ninja });
        ninja.SetOwner(controller); ninja.SetController(controller);
        ninja.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(ninja);
        ninja.ActiveEffects = svc;
        return ninja;
    }

    [Fact]
    public void NinjaAnthem_BoostsControllersNinjas_ByOnePlusOne()
    {
        var svc = new ContinuousEffectsService();
        var ninja = MakeNinja(_alice, svc);

        ninja.GetPower().Should().Be(2);
        ninja.GetToughness().Should().Be(2);

        svc.Register(new EmblemAnthemEffect(_alice, CardSubtype.Ninja, 1, 1));

        ninja.GetPower().Should().Be(3, "Ninjas you control get +1/+1");
        ninja.GetToughness().Should().Be(3);
    }

    [Fact]
    public void NinjaAnthem_IsAlwaysActive_NoZoneGate()
    {
        // CR 114 — an emblem lives in the command zone for the rest of the
        // game, so its anthem never deactivates.
        var anthem = new EmblemAnthemEffect(_alice, CardSubtype.Ninja);
        anthem.IsActive().Should().BeTrue();
        anthem.ExpiresAtEndOfTurn.Should().BeFalse();
    }

    [Fact]
    public void NinjaAnthem_DoesNotBoostOpponentsNinjas()
    {
        var svc = new ContinuousEffectsService();
        var aliceNinja = MakeNinja(_alice, svc);
        var bobNinja = MakeNinja(_bob, svc);

        svc.Register(new EmblemAnthemEffect(_alice, CardSubtype.Ninja, 1, 1));

        aliceNinja.GetPower().Should().Be(3, "Alice's emblem boosts Alice's Ninja");
        bobNinja.GetPower().Should().Be(2, "but not Bob's Ninja (CR 109.5 — \"you control\")");
    }

    [Fact]
    public void NinjaAnthem_DoesNotBoostNonNinjaCreatures()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Plain Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice); bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.ActiveEffects = svc;

        svc.Register(new EmblemAnthemEffect(_alice, CardSubtype.Ninja, 1, 1));

        bear.GetPower().Should().Be(2, "a non-Ninja is unaffected by a Ninja anthem");
    }

    [Fact]
    public void NinjaAnthem_StacksWithMultipleEmblems()
    {
        var svc = new ContinuousEffectsService();
        var ninja = MakeNinja(_alice, svc);

        // Two +1 activations ⇒ two emblems ⇒ two anthems ⇒ +2/+2.
        svc.Register(new EmblemAnthemEffect(_alice, CardSubtype.Ninja, 1, 1));
        svc.Register(new EmblemAnthemEffect(_alice, CardSubtype.Ninja, 1, 1));

        ninja.GetPower().Should().Be(4, "two Ninja anthems stack additively (CR 613.7c)");
        ninja.GetToughness().Should().Be(4);
    }
}
