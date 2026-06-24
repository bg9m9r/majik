using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Brass's Bounty (Rivals of Ixalan, {6}{R}).
/// Sorcery — "For each land you control, create a Treasure token."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity: name, mana cost {6}{R}, Sorcery type.
///   - Resolve mints one Treasure per land the caster controls.
///   - Zero lands → zero Treasures (clean no-op, no throw).
///   - Only the caster's OWN lands count (an opponent's lands don't).
///   - Non-land permanents the caster controls don't count.
///
/// Dispatch + well-formedness is asserted automatically for every implemented
/// card by CardFactoryContractTests — no dispatch test needed here.
/// </summary>
[Trait("Color", "R")]
public class BrasssBountyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BrasssBounty_Identity()
    {
        var c = BrasssBountyFactory.Create(_alice);

        c.Name.Should().Be("Brass's Bounty");
        c.ManaCost.Should().Be("{6}{R}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().Be(_alice);
    }

    [Fact]
    public void Resolve_CreatesOneTreasurePerControlledLand()
    {
        SeedLands(_alice, 5);

        ResolveBounty(_alice);

        TreasureCount(_alice).Should().Be(5,
            "one Treasure is minted per land the caster controls");
    }

    [Fact]
    public void Resolve_NoLands_NoTreasures_NoThrow()
    {
        var act = () => ResolveBounty(_alice);

        act.Should().NotThrow();
        TreasureCount(_alice).Should().Be(0,
            "zero controlled lands → zero Treasures (clean no-op)");
    }

    [Fact]
    public void Resolve_CountsOnlyCastersOwnLands()
    {
        SeedLands(_alice, 3);
        SeedLands(_bob, 4); // opponent's lands must not contribute.

        ResolveBounty(_alice);

        TreasureCount(_alice).Should().Be(3,
            "only the caster's controlled lands count, not the opponent's");
        TreasureCount(_bob).Should().Be(0);
    }

    [Fact]
    public void Resolve_IgnoresNonLandPermanents()
    {
        SeedLands(_alice, 2);

        // A controlled non-land permanent (a creature) must not count.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        creature.SetController(_alice);
        creature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(creature);

        ResolveBounty(_alice);

        TreasureCount(_alice).Should().Be(2,
            "only lands count toward the Treasure count, not other permanents");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void ResolveBounty(Player caster)
    {
        foreach (var e in BrasssBountyFactory.BuildResolveEffect(caster))
        {
            e.Execute();
        }
    }

    private static void SeedLands(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var land = new Land($"Mountain{i}");
            land.SetOwner(p);
            land.SetController(p);
            land.SetZone(ZoneType.Battlefield);
            p.Zones.Battlefield.AddCard(land);
        }
    }

    private static int TreasureCount(Player p) =>
        p.Zones.Battlefield.GetCards()
            .Count(c => c.HasSubtype(CardSubtype.Treasure));
}
