using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ManOWarFactory"/>.
///
/// Man-o'-War — Creature — Jellyfish {2}{U} 2/2.
/// Oracle text: "When this creature enters, return target creature
///               to its owner's hand."
///
/// Man-o'-War is the simpler, unconditional sibling of Aether Adept /
/// Reflector Mage: any creature, no replay restriction. Mirrors
/// <see cref="AetherAdeptFactoryTests"/>.
///
/// Covers:
/// - Identity (name, type, P/T 2/2, Jellyfish subtype, mana cost {2}{U},
///   mana value 3, owner/controller, Blue colour).
/// - Exactly one ETB triggered ability attached.
/// - ETB trigger has one TargetRequest (1..1, BotIntent.Bounce, description
///   contains "creature").
/// - ETB resolution: target creature on opponent's battlefield is bounced to
///   its owner's hand.
/// - ETB resolution: target creature owned/controlled by the same player
///   (self-bounce) is also legal — "target creature" with no ownership restriction.
/// - ETB resolution: no target chosen → no-op, no exception.
/// - ETB resolution: target already off battlefield (CR 608.2b) → no-op.
/// </summary>
[Trait("Color", "U")]
public class ManOWarFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ManOWar_Identity()
    {
        var c = ManOWarFactory.Create(_alice);

        c.Name.Should().Be("Man-o'-War");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Jellyfish).Should().BeTrue("Man-o'-War is a Jellyfish");
        c.ManaCost.Should().Be("{2}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ManOWar_ManaValue_IsThree()
    {
        var c = ManOWarFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(3,
            "mana value 3: two generic + one Blue pip");
    }

    [Fact]
    public void ManOWar_Colors_ContainsBlueOnly()
    {
        var c = ManOWarFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "Man-o'-War costs {2}{U}");
        colors.Should().HaveCount(1, "Man-o'-War is exactly Blue");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ManOWar_HasExactlyOneTriggeredAbility()
    {
        var c = ManOWarFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB bounce trigger on Man-o'-War");
    }

    [Fact]
    public void ManOWar_EtbTrigger_HasOneTargetRequest()
    {
        var c = ManOWarFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().HaveCount(1,
            "exactly one 'target creature' request");

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature",
            "request describes a creature target");
        req.Intent.Should().Be(BotIntent.Bounce,
            "bot uses Bounce intent to rank the target");

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB trigger functions only from the battlefield");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — bounce opponent creature
    // -----------------------------------------------------------------------

    [Fact]
    public void ManOWar_EtbEffect_BouncesOpponentCreatureToHand()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var target = new Creature("Grizzly Bears", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var manOWar = ManOWarFactory.Create(alice);
        var etb = manOWar.Abilities.OfType<TriggeredAbility>().Single();

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        etb.Resolve();

        target.Zone.Should().Be(ZoneType.Hand,
            "Man-o'-War ETB bounces the chosen creature to its owner's hand");
        bob.Zones.Hand.GetCards().Should().Contain(target,
            "the bounced creature ends up in Bob's hand");
        bob.Zones.Battlefield.GetCards().Should().NotContain(target,
            "the creature has left Bob's battlefield");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — any creature is legal (no opponent restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void ManOWar_EtbEffect_CanBounceOwnCreature()
    {
        var alice = new Player("Alice", 20);

        var allyCreature = new Creature("Merfolk Looter", "{1}{U}", 1, 1);
        allyCreature.SetOwner(alice);
        allyCreature.SetController(alice);
        alice.Zones.Battlefield.AddCard(allyCreature);
        allyCreature.SetZone(ZoneType.Battlefield);

        var manOWar = ManOWarFactory.Create(alice);
        var etb = manOWar.Abilities.OfType<TriggeredAbility>().Single();

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { allyCreature },
        });
        etb.Resolve();

        allyCreature.Zone.Should().Be(ZoneType.Hand,
            "Man-o'-War targets ANY creature — self-bounce is legal (no opponent restriction)");
        alice.Zones.Hand.GetCards().Should().Contain(allyCreature);
        alice.Zones.Battlefield.GetCards().Should().NotContain(allyCreature);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — guard cases
    // -----------------------------------------------------------------------

    [Fact]
    public void ManOWar_EtbEffect_NoTarget_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var manOWar = ManOWarFactory.Create(alice);
        var etb = manOWar.Abilities.OfType<TriggeredAbility>().Single();
        // ChosenTargets left empty — no target declared.

        var act = () => etb.Resolve();

        act.Should().NotThrow("ETB with no chosen target is a no-op");
        bob.Zones.Hand.GetCards().Should().BeEmpty(
            "no creature was bounced when there was no target");
    }

    [Fact]
    public void ManOWar_EtbEffect_TargetAlreadyLeft_IsNoOp()
    {
        // CR 608.2b — if the chosen target is no longer on the battlefield at
        // resolution, the ability does nothing.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var target = new Creature("Grizzly Bears", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        bob.Zones.Graveyard.AddCard(target);
        target.SetZone(ZoneType.Graveyard); // already dead at resolution time

        var manOWar = ManOWarFactory.Create(alice);
        var etb = manOWar.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => etb.Resolve();

        act.Should().NotThrow(
            "CR 608.2b: illegal target at resolution is a no-op, not an exception");
        bob.Zones.Hand.GetCards().Should().BeEmpty(
            "the already-dead creature is not bounced to hand");
        bob.Zones.Graveyard.GetCards().Should().Contain(target,
            "the creature stays in the graveyard (it was already there)");
    }
}
