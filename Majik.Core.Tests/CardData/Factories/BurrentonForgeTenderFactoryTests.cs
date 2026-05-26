using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BurrentonForgeTenderFactory"/>.
///
/// Covers:
/// - Identity (Kithkin Wizard 1/1 at {W}, owner / controller wired).
/// - NamedCardFactory dispatch.
/// - Protection from red is attached as <see cref="ProtectionAbility"/>.
/// - One activated ability (sac-self + prevent red).
/// - Sacrifice cost moves Burrenton to its owner's graveyard.
/// - Resolution registers a
///   <see cref="PreventAllDamageFromColoredSourcesToCreatureShield"/> on
///   the supplied <see cref="ReplacementBus"/>.
/// - Shield prevents red-source damage to the chosen target end-to-end.
/// - No bus + no target = clean no-op (shape test).
/// </summary>
public class BurrentonForgeTenderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Burrenton_Identity()
    {
        var c = BurrentonForgeTenderFactory.Create(_alice);

        c.Name.Should().Be("Burrenton Forge-Tender");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Kithkin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Burrenton_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Burrenton Forge-Tender", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Burrenton Forge-Tender");
        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(1,
            "protection from red rider");
        c.Abilities.OfType<BurrentonForgeTenderAbility>().Should().HaveCount(1,
            "sac-self prevent-red activated ability");
    }

    [Fact]
    public void Burrenton_HasProtectionFromRed()
    {
        var c = BurrentonForgeTenderFactory.Create(_alice);

        var protection = c.Abilities.OfType<ProtectionAbility>().Single();
        protection.Quality.Should().Be("red");
    }

    [Fact]
    public void Burrenton_Activate_NullTarget_NoOpResolution()
    {
        var bus = new ReplacementBus();
        var burrenton = BurrentonForgeTenderFactory.Create(_alice, bus);
        burrenton.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(burrenton);

        var ability = burrenton.Abilities.OfType<BurrentonForgeTenderAbility>().Single();
        // Don't set PreventionTarget — resolution should no-op cleanly
        // but still record the no-target resolution payload.
        foreach (var e in ability.Effects) e.Execute();

        ability.LastResolution.Should().NotBeNull();
        ability.LastResolution!.Target.Should().BeNull();
        ability.LastResolution.Registered.Should().BeFalse();
    }

    [Fact]
    public void Burrenton_Activate_RegistersShieldOnBusForTarget()
    {
        var bus = new ReplacementBus();
        var burrenton = BurrentonForgeTenderFactory.Create(_alice, bus);
        burrenton.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(burrenton);

        // Target: Alice's creature we want to protect.
        var protectee = new Creature("Mox-Tapper", "{1}{W}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        protectee.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(protectee);

        var ability = burrenton.Abilities.OfType<BurrentonForgeTenderAbility>().Single();
        ability.PreventionTarget = protectee;

        foreach (var e in ability.Effects) e.Execute();

        ability.LastResolution.Should().NotBeNull();
        ability.LastResolution!.Target.Should().BeSameAs(protectee);
        ability.LastResolution.Registered.Should().BeTrue();

        // The shield is now live on the bus — red-source damage to the
        // protectee is cancelled.
        var redSrc = new Creature("Pyroclasm-stand-in", "{R}", 2, 2);
        bus.Apply(new DamageIntent(redSrc, 3, TargetCreature: protectee))
            .Should().BeNull("shield prevents red-source damage to protectee EOT");
    }

    [Fact]
    public void Burrenton_SacrificeCost_SendsBurrentonToOwnerGraveyard()
    {
        var bus = new ReplacementBus();
        var burrenton = BurrentonForgeTenderFactory.Create(_alice, bus);
        burrenton.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(burrenton);

        var ability = burrenton.Abilities.OfType<BurrentonForgeTenderAbility>().Single();
        ability.SacrificeChoice.CanPay(_alice).Should().BeTrue(
            "Burrenton is on Alice's battlefield + controlled by her");
        ability.SacrificeChoice.Pay(_alice);

        burrenton.Zone.Should().Be(ZoneType.Graveyard,
            "Sacrifice routes Burrenton to its owner's graveyard (CR 701.16a)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(burrenton);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(burrenton);
    }

    [Fact]
    public void Burrenton_SacrificeCost_FailsWhenNotOnBattlefield()
    {
        var burrenton = BurrentonForgeTenderFactory.Create(_alice);
        // Burrenton in hand — sacrifice cost cannot be paid.
        burrenton.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(burrenton);

        var ability = burrenton.Abilities.OfType<BurrentonForgeTenderAbility>().Single();
        ability.SacrificeChoice.CanPay(_alice).Should().BeFalse(
            "sacrifice requires the permanent on its controller's battlefield");
    }

    [Fact]
    public void Burrenton_DoesNotBlockNonRedDamage()
    {
        var bus = new ReplacementBus();
        var burrenton = BurrentonForgeTenderFactory.Create(_alice, bus);
        burrenton.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(burrenton);

        var protectee = new Creature("Mox-Tapper", "{1}{W}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };

        var ability = burrenton.Abilities.OfType<BurrentonForgeTenderAbility>().Single();
        ability.PreventionTarget = protectee;
        foreach (var e in ability.Effects) e.Execute();

        // Black source: shield should NOT engage.
        var blackSrc = new Creature("Doom-Blade-stand-in", "{1}{B}", 1, 1);
        var passed = bus.Apply(new DamageIntent(blackSrc, 2, TargetCreature: protectee));
        passed.Should().NotBeNull();
        passed!.Amount.Should().Be(2,
            "non-red source is not gated by Burrenton's prevention");
    }
}
