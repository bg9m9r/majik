using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BarbarianRingFactory"/>.
///
/// Barbarian Ring — Land (Odyssey).
/// Oracle text:
///   "{T}: Add {R}. Barbarian Ring deals 1 damage to you.
///    Threshold — {R}, {T}, Sacrifice Barbarian Ring: It deals 2 damage to
///    any target. Activate only if there are seven or more cards in your
///    graveyard."
///
/// Covers:
/// - Identity (Land, non-Basic, non-Legendary, name, owner/controller).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
/// - Mana ability: exactly one ManaAbility producing {R}; activation deals 1
///   damage to the controller (life −1); land is tapped after activation.
/// - Threshold gate: sac ability's resolve-time guard skips when graveyard
///   has fewer than 7 cards.
/// - Threshold gate: sac ability resolves when graveyard has ≥7 cards — deals
///   2 damage to the chosen target AND moves the land to the graveyard.
/// - Target receives 2 damage only when threshold is met.
/// </summary>
public class BarbarianRingFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BarbarianRing_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var ring = BarbarianRingFactory.Create(alice);

        ring.Should().BeOfType<Land>();
        ring.HasType(CardType.Land).Should().BeTrue();
        ring.Name.Should().Be("Barbarian Ring");
    }

    [Fact]
    public void BarbarianRing_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var ring = BarbarianRingFactory.Create(alice);

        ring.Owner.Should().BeSameAs(alice);
        ring.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void BarbarianRing_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var ring = BarbarianRingFactory.Create(alice);

        ring.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        ring.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void BarbarianRing_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Barbarian Ring", alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Barbarian Ring");
    }

    // -----------------------------------------------------------------------
    // Mana ability — {T}: Add {R}. Barbarian Ring deals 1 damage to you.
    // -----------------------------------------------------------------------

    [Fact]
    public void BarbarianRing_HasExactlyOneManaAbility_ProducingRed()
    {
        var alice = new Player("Alice", 20);
        var ring = BarbarianRingFactory.Create(alice);

        var manaAbilities = ring.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().HaveCount(1,
            "Barbarian Ring has exactly one mana ability: {T}: Add {R}");
        manaAbilities[0].ManaGenerated.Red.Should().Be(1,
            "the mana ability produces {R}");
    }

    [Fact]
    public void BarbarianRing_ManaAbility_Activation_DealsOneDamageToController()
    {
        var alice = new Player("Alice", 20);
        var ring = BarbarianRingFactory.Create(alice);
        var mana = ring.Abilities.OfType<ManaAbility>().Single();

        mana.Activate();

        alice.LifeTotal.Should().Be(19,
            "tapping Barbarian Ring for {R} deals 1 damage to you (CR 120.3)");
        ring.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void BarbarianRing_ManaAbility_CannotActivateWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var ring = BarbarianRingFactory.Create(alice);
        var mana = ring.Abilities.OfType<ManaAbility>().Single();

        mana.Activate(); // first tap

        mana.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped land");
    }

    // -----------------------------------------------------------------------
    // Threshold sac ability — resolve-time guard
    // -----------------------------------------------------------------------

    [Fact]
    public void BarbarianRing_HasExactlyOneActivatedAbility()
    {
        var alice = new Player("Alice", 20);
        var ring = BarbarianRingFactory.Create(alice);

        ring.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the Threshold sac ability is the only non-mana activated ability");
    }

    [Fact]
    public void BarbarianRing_SacAbility_WithLessThanSevenCardsInGraveyard_IsNoOp()
    {
        // Fewer than 7 cards in graveyard — threshold NOT met.
        // The resolve-time guard should short-circuit: target takes no damage,
        // land stays on battlefield.
        var alice = new Player("Alice", 20);
        var ring = BarbarianRingFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ring);
        ring.SetZone(ZoneType.Battlefield);

        var bob = new Player("Bob", 20);
        SeedGraveyard(alice, 6); // exactly one short of threshold

        var sac = ring.Abilities.OfType<ActivatedAbility>().Single();
        sac.SetChosenTargets(new[] { new object[] { bob } });

        sac.Resolve();

        bob.LifeTotal.Should().Be(20,
            "threshold NOT met (<7 graveyard cards) — sac ability is a no-op, target takes no damage");
        ring.Zone.Should().Be(ZoneType.Battlefield,
            "ring should still be on the battlefield when threshold is not met");
    }

    [Fact]
    public void BarbarianRing_SacAbility_WithSevenCardsInGraveyard_DealsTwoDamageToTarget()
    {
        // Exactly 7 cards — threshold met.
        var alice = new Player("Alice", 20);
        var ring = BarbarianRingFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ring);
        ring.SetZone(ZoneType.Battlefield);

        var bob = new Player("Bob", 20);
        SeedGraveyard(alice, 7); // exactly at threshold

        var sac = ring.Abilities.OfType<ActivatedAbility>().Single();
        sac.SetChosenTargets(new[] { new object[] { bob } });

        sac.Resolve();

        bob.LifeTotal.Should().Be(18,
            "threshold met (≥7 graveyard cards) — deals 2 damage to the target");
    }

    [Fact]
    public void BarbarianRing_SacAbility_WithSevenCardsInGraveyard_SacrificesTheLand()
    {
        var alice = new Player("Alice", 20);
        var ring = BarbarianRingFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ring);
        ring.SetZone(ZoneType.Battlefield);

        var bob = new Player("Bob", 20);
        SeedGraveyard(alice, 7);

        var sac = ring.Abilities.OfType<ActivatedAbility>().Single();
        sac.SetChosenTargets(new[] { new object[] { bob } });

        sac.Resolve();

        ring.Zone.Should().Be(ZoneType.Graveyard,
            "Barbarian Ring is sacrificed as part of the activation cost — moves to graveyard");
        alice.Zones.Battlefield.GetCards().Should().NotContain(ring);
        alice.Zones.Graveyard.GetCards().Should().Contain(ring);
    }

    [Fact]
    public void BarbarianRing_SacAbility_WithMoreThanSevenCardsInGraveyard_DealsDamage()
    {
        // 9 cards — well above threshold.
        var alice = new Player("Alice", 20);
        var ring = BarbarianRingFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ring);
        ring.SetZone(ZoneType.Battlefield);

        var bob = new Player("Bob", 20);
        SeedGraveyard(alice, 9);

        var sac = ring.Abilities.OfType<ActivatedAbility>().Single();
        sac.SetChosenTargets(new[] { new object[] { bob } });

        sac.Resolve();

        bob.LifeTotal.Should().Be(18,
            "threshold met (9 ≥ 7) — deals 2 damage to the target");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Seed <paramref name="count"/> stub instant cards into
    /// <paramref name="player"/>'s graveyard for threshold testing.
    /// </summary>
    private static void SeedGraveyard(Player player, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var stub = new Instant($"Stub{i}", "R");
            stub.SetOwner(player);
            player.Zones.Graveyard.AddCard(stub);
        }
    }
}
