using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Doomskar (Kaldheim, {3}{W}{W}, Sorcery).
///
/// Oracle: "Foretell {2}{W}. Destroy all creatures."
///
/// v1 ships without the Foretell alt cost (CR 702.143) — see
/// <see cref="DoomskarFactory"/> doc for the deferral. The factory only
/// exposes the printed mana-cost cast path; the resolve body is the
/// same shape as the foretold cast would produce.
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Sweep destroys every creature on every supplied player's
///     battlefield (CR 109.5 symmetric, CR 701.7 destroy).
///   - Non-creature permanents survive.
///   - Empty battlefield is a clean no-op.
///   - Foretell printed cost constant matches the oracle text.
/// </summary>
public class DoomskarTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Doomskar_IsSorcery_At3WW()
    {
        var d = DoomskarFactory.Create(_alice);

        d.Name.Should().Be("Doomskar");
        d.ManaCost.Should().Be("{3}{W}{W}");
        d.HasType(CardType.Sorcery).Should().BeTrue();
        d.Owner.Should().BeSameAs(_alice);
        d.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Doomskar()
    {
        var card = NamedCardFactory.Create("Doomskar", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Doomskar");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{W}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ForetellCost_IsRecorded_ForFutureCastPipeline()
    {
        // Pins the constant — when Foretell (CR 702.143) is wired the
        // cast pipeline will bill this string instead of PrintedManaCost
        // for the foretold cast path.
        DoomskarFactory.ForetellPrintedCost.Should().Be("{2}{W}");
    }

    // -----------------------------------------------------------------------
    // Resolve — destroy all creatures, symmetric
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysCreaturesOnBothBattlefields_ToOwnerGraveyards()
    {
        var aliceCreatures = new[]
        {
            SeedCreature(_alice, "Alice-Bear"),
            SeedCreature(_alice, "Alice-Wolf"),
        };
        var bobCreatures = new[]
        {
            SeedCreature(_bob, "Bob-Bear"),
            SeedCreature(_bob, "Bob-Wolf"),
        };

        var effects = DoomskarFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();

        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(aliceCreatures);
        _bob.Zones.Graveyard.GetCards().Should().BeEquivalentTo(bobCreatures);
    }

    [Fact]
    public void Resolve_LeavesNonCreaturePermanentsAlone()
    {
        var creature = SeedCreature(_alice, "Alice-Bear");
        var land = new Land("Plains");
        land.SetOwner(_alice); land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);
        var enchantment = new Enchantment("Glorious Anthem", "{1}{W}{W}");
        enchantment.SetOwner(_alice); enchantment.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(enchantment); enchantment.SetZone(ZoneType.Battlefield);

        var effects = DoomskarFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        creature.Zone.Should().Be(ZoneType.Graveyard);
        land.Zone.Should().Be(ZoneType.Battlefield);
        enchantment.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_EmptyBattlefields_IsCleanNoOp()
    {
        var effects = DoomskarFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
