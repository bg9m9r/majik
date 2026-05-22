using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.Effects;

public class PreventAllCombatDamageToPlayersShieldTests
{
    [Fact]
    public void BlocksCombatDamageToAnyPlayer()
    {
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllCombatDamageToPlayersShield());

        var attacker = new Creature("a", "", 4, 4);
        var defender = new Player("D", 20);
        bus.Apply(new DamageIntent(attacker, 4, TargetPlayer: defender))
            .Should().BeNull();
    }

    [Fact]
    public void DoesNotBlockCombatDamageToCreatures()
    {
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllCombatDamageToPlayersShield());

        var attacker = new Creature("a", "", 4, 4);
        var blocker = new Creature("b", "", 2, 2);
        var result = bus.Apply(new DamageIntent(attacker, 4, TargetCreature: blocker));
        result.Should().NotBeNull("creature-on-creature combat damage still resolves");
        result!.Amount.Should().Be(4);
    }

    [Fact]
    public void DoesNotBlockNonCombatDamageToPlayers()
    {
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllCombatDamageToPlayersShield());

        // Source is an Instant (Lightning Bolt-style), not a Creature.
        var bolt = new Instant("Bolt", "{R}");
        var victim = new Player("V", 20);
        var result = bus.Apply(new DamageIntent(bolt, 3, TargetPlayer: victim));
        result.Should().NotBeNull("non-combat damage still resolves");
        result!.Amount.Should().Be(3);
    }

    [Fact]
    public void DoesNotBlockCombatDamageToPlaneswalkers()
    {
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllCombatDamageToPlayersShield());

        var attacker = new Creature("a", "", 4, 4);
        var pw = new Planeswalker("Pw", "", 4);
        var result = bus.Apply(new DamageIntent(attacker, 4, TargetPlaneswalker: pw));
        result.Should().NotBeNull("planeswalker damage is not player-bound");
        result!.Amount.Should().Be(4);
    }

    [Fact]
    public void DropsAtEndOfTurn()
    {
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllCombatDamageToPlayersShield());
        bus.ExpireEndOfTurn();

        var attacker = new Creature("a", "", 4, 4);
        var defender = new Player("D", 20);
        var result = bus.Apply(new DamageIntent(attacker, 4, TargetPlayer: defender));
        result.Should().NotBeNull();
    }
}
