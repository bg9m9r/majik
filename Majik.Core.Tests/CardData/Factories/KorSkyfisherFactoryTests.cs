using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KorSkyfisherFactory"/>.
///
/// Kor Skyfisher — Creature — Kor Soldier {1}{W} 2/3.
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, return a permanent you control to its
///    owner's hand."
///
/// Covers:
/// - Identity (name, type, P/T 2/3, Kor + Soldier subtypes, mana cost {1}{W},
///   mana value 2, owner/controller, White colour).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flying keyword marker (CR 702.9) — CombatAbilities.HasFlying.
/// - Exactly one ETB triggered ability attached.
/// - ETB trigger has one TargetRequest (1..1, BotIntent.Bounce, description
///   names "you control").
/// - ETB resolution: a permanent the controller owns + controls is bounced to
///   its owner's hand (self-bounce — "a permanent you control").
/// - ETB resolution: bouncing Kor Skyfisher itself is legal.
/// - ETB resolution: no target chosen → no-op, no exception.
/// - ETB resolution: target already off battlefield (CR 608.2b) → no-op.
/// </summary>
public class KorSkyfisherFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSkyfisher_Identity()
    {
        var c = KorSkyfisherFactory.Create(_alice);

        c.Name.Should().Be("Kor Skyfisher");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(3);
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue("Kor Skyfisher is a Kor");
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue("Kor Skyfisher is a Soldier");
        c.ManaCost.Should().Be("{1}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KorSkyfisher_ManaValue_IsTwo()
    {
        var c = KorSkyfisherFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(2,
            "mana value 2: one generic + one White pip");
    }

    [Fact]
    public void KorSkyfisher_Colors_ContainsWhiteOnly()
    {
        var c = KorSkyfisherFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White, "Kor Skyfisher costs {1}{W}");
        colors.Should().HaveCount(1, "Kor Skyfisher is exactly White");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSkyfisher_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Kor Skyfisher", _alice);

        c.Should().BeOfType<Creature>("Kor Skyfisher is a Creature");
        c.Name.Should().Be("Kor Skyfisher");
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{W}");
    }

    // -----------------------------------------------------------------------
    // Flying (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSkyfisher_HasFlying()
    {
        var c = KorSkyfisherFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue(
            "Kor Skyfisher has the Flying keyword (CR 702.9)");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSkyfisher_HasExactlyOneTriggeredAbility()
    {
        var c = KorSkyfisherFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB self-bounce trigger on Kor Skyfisher");
    }

    [Fact]
    public void KorSkyfisher_EtbTrigger_HasOneTargetRequest()
    {
        var c = KorSkyfisherFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.TargetRequests.Should().HaveCount(1,
            "exactly one 'a permanent you control' request");

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("you control",
            "request describes a permanent the controller owns");
        req.Intent.Should().Be(BotIntent.Bounce,
            "bot uses Bounce intent to rank the target");

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB trigger functions only from the battlefield");
    }

    // -----------------------------------------------------------------------
    // ETB resolution — self-bounce a permanent you control
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSkyfisher_EtbEffect_ReturnsOwnPermanentToHand()
    {
        var alice = new Player("Alice", 20);

        // A permanent Alice owns and controls — a legal "permanent you control".
        var land = new Land("Plains");
        land.SetOwner(alice);
        land.SetController(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var skyfisher = KorSkyfisherFactory.Create(alice);
        var etb = skyfisher.Abilities.OfType<TriggeredAbility>().Single();

        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { land },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        land.Zone.Should().Be(ZoneType.Hand,
            "Kor Skyfisher ETB returns a permanent you control to its owner's hand");
        alice.Zones.Hand.GetCards().Should().Contain(land,
            "the bounced permanent ends up in Alice's hand");
        alice.Zones.Battlefield.GetCards().Should().NotContain(land,
            "the permanent has left Alice's battlefield");
    }

    [Fact]
    public void KorSkyfisher_EtbEffect_CanReturnItself()
    {
        // "a permanent you control" — Kor Skyfisher itself is a legal choice
        // (and is typically the only target if it's your sole permanent).
        var alice = new Player("Alice", 20);

        var skyfisher = KorSkyfisherFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(skyfisher);
        skyfisher.SetZone(ZoneType.Battlefield);

        var etb = skyfisher.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { skyfisher },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        skyfisher.Zone.Should().Be(ZoneType.Hand,
            "Kor Skyfisher can bounce itself");
        alice.Zones.Hand.GetCards().Should().Contain(skyfisher);
        alice.Zones.Battlefield.GetCards().Should().NotContain(skyfisher);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — guard cases
    // -----------------------------------------------------------------------

    [Fact]
    public void KorSkyfisher_EtbEffect_NoTarget_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var skyfisher = KorSkyfisherFactory.Create(alice);
        var etb = skyfisher.Abilities.OfType<TriggeredAbility>().Single();
        // ChosenTargets left empty — no target declared.

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("ETB with no chosen target is a no-op");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no permanent was bounced when there was no target");
    }

    [Fact]
    public void KorSkyfisher_EtbEffect_TargetAlreadyLeft_IsNoOp()
    {
        // CR 608.2b — if the chosen target is no longer on the battlefield at
        // resolution, the ability does nothing.
        var alice = new Player("Alice", 20);

        var land = new Land("Plains");
        land.SetOwner(alice);
        land.SetController(alice);
        alice.Zones.Graveyard.AddCard(land);
        land.SetZone(ZoneType.Graveyard); // already gone at resolution time

        var skyfisher = KorSkyfisherFactory.Create(alice);
        var etb = skyfisher.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { land },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "CR 608.2b: illegal target at resolution is a no-op, not an exception");
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the already-gone permanent is not bounced to hand");
        alice.Zones.Graveyard.GetCards().Should().Contain(land,
            "the permanent stays in the graveyard (it was already there)");
    }
}
