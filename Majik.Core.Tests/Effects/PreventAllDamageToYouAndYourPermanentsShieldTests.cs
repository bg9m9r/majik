using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Effects;

public class PreventAllDamageToYouAndYourPermanentsShieldTests
{
    [Fact]
    public void BlocksDamageToBeneficiaryPlayer()
    {
        var alice = new Player("A", 20);
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToYouAndYourPermanentsShield(alice));

        var src = new Creature("src", "", 3, 3);
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: alice))
            .Should().BeNull("shield prevents damage to caster");
    }

    [Fact]
    public void BlocksDamageToCreatureBeneficiaryControls()
    {
        var alice = new Player("A", 20);
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToYouAndYourPermanentsShield(alice));

        var myCreature = new Creature("mine", "", 2, 2) { Owner = alice, Controller = alice };
        var src = new Creature("src", "", 2, 2);
        bus.Apply(new DamageIntent(src, 2, TargetCreature: myCreature))
            .Should().BeNull("shield prevents damage to a creature you control");
    }

    [Fact]
    public void DoesNotBlockDamageToOpponentOrTheirPermanents()
    {
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToYouAndYourPermanentsShield(alice));

        var src = new Creature("src", "", 3, 3);
        var bobsCreature = new Creature("bob's", "", 2, 2) { Owner = bob, Controller = bob };

        // Damage to opponent — passes.
        var toBob = bus.Apply(new DamageIntent(src, 3, TargetPlayer: bob));
        toBob.Should().NotBeNull();
        toBob!.Amount.Should().Be(3);

        // Damage to opponent's creature — passes.
        var toBobsCreature = bus.Apply(new DamageIntent(src, 2, TargetCreature: bobsCreature));
        toBobsCreature.Should().NotBeNull();
        toBobsCreature!.Amount.Should().Be(2);
    }

    [Fact]
    public void BlocksDamageToBeneficiaryPlaneswalker()
    {
        var alice = new Player("A", 20);
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToYouAndYourPermanentsShield(alice));

        var pw = new Planeswalker("Pw", "", 4) { Owner = alice, Controller = alice };
        var src = new Creature("src", "", 2, 2);
        bus.Apply(new DamageIntent(src, 2, TargetPlaneswalker: pw))
            .Should().BeNull("shield prevents damage to a planeswalker you control");
    }

    [Fact]
    public void DropsAtEndOfTurn()
    {
        var alice = new Player("A", 20);
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllDamageToYouAndYourPermanentsShield(alice));
        bus.ExpireEndOfTurn();

        var src = new Creature("src", "", 3, 3);
        var result = bus.Apply(new DamageIntent(src, 3, TargetPlayer: alice));
        result.Should().NotBeNull();
        result!.Amount.Should().Be(3);
    }
}
