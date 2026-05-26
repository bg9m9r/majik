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
/// Tests for Path of Peril (Adventures in the Forgotten Realms, {2}{B},
/// Sorcery).
///
/// Oracle: "Cleave {1}{W}{B}{B}. Destroy all [nonlegendary] creatures
/// with mana value 2 or less."
///
/// v1 ships the always-cleaved body — Cleave (CR 702.156) is not yet
/// implemented; see <see cref="PathOfPerilFactory"/> doc for the
/// deferral. Tests pin the cleaved semantics:
///
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Destroys creatures with mv ≤ 2 on every supplied player's
///     battlefield (CR 109.5 symmetric sweep).
///   - Creatures with mv ≥ 3 survive.
///   - Legendary creatures with mv ≤ 2 are also destroyed (cleaved body).
///   - Non-creature permanents (lands, artifacts) survive regardless of
///     mana value.
///   - Empty battlefield is a clean no-op.
/// </summary>
public class PathOfPerilTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PathOfPeril_IsSorcery_At2B()
    {
        var p = PathOfPerilFactory.Create(_alice);

        p.Name.Should().Be("Path of Peril");
        p.ManaCost.Should().Be("{2}{B}");
        p.HasType(CardType.Sorcery).Should().BeTrue();
        p.Owner.Should().BeSameAs(_alice);
        p.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PathOfPeril()
    {
        var card = NamedCardFactory.Create("Path of Peril", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Path of Peril");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CleaveCost_IsRecorded_ForFutureCastPipeline()
    {
        // Pins the constant — when Cleave (CR 702.156) is wired the
        // cast pipeline will bill this string as the alt cost and drop
        // the "nonlegendary" qualifier from the resolve body.
        PathOfPerilFactory.CleavePrintedCost.Should().Be("{1}{W}{B}{B}");
    }

    // -----------------------------------------------------------------------
    // Resolve — mv ≤ 2 sweep, symmetric across battlefields
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysCreaturesWithManaValue2OrLess_OnBothBattlefields()
    {
        // Mix: mv0 (token-shaped, empty cost), mv1, mv2, mv3, mv4.
        var mv0 = SeedCreature(_alice, "Alice-Token", "");
        var mv1 = SeedCreature(_alice, "Alice-Cur", "{B}");
        var mv2 = SeedCreature(_bob, "Bob-Bear", "{1}{G}");
        var mv3 = SeedCreature(_bob, "Bob-Knight", "{1}{W}{W}");
        var mv4 = SeedCreature(_alice, "Alice-Wolf", "{2}{G}{G}");

        var effects = PathOfPerilFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        // mv ≤ 2 destroyed.
        mv0.Zone.Should().Be(ZoneType.Graveyard);
        mv1.Zone.Should().Be(ZoneType.Graveyard);
        mv2.Zone.Should().Be(ZoneType.Graveyard);

        // mv ≥ 3 survives.
        mv3.Zone.Should().Be(ZoneType.Battlefield);
        mv4.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_CleavedBody_DestroysLegendaryCreaturesWithManaValue2OrLess()
    {
        // v1 ships the cleaved body — legendary is NOT filtered out.
        // Once Cleave (CR 702.156) is wired, this test should branch on
        // a cleaved flag; for now it pins the v1 contract.
        var legendaryMv2 = new Creature("Thalia, Guardian of Thraben", "{1}{W}", 2, 1,
            supertypes: new[] { CardSupertype.Legendary });
        legendaryMv2.SetOwner(_alice);
        legendaryMv2.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(legendaryMv2);
        legendaryMv2.SetZone(ZoneType.Battlefield);

        var effects = PathOfPerilFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        legendaryMv2.Zone.Should().Be(ZoneType.Graveyard);
        legendaryMv2.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Resolve_LeavesNonCreaturePermanentsAlone()
    {
        // Land, artifact — none destroyed regardless of mv.
        var creature = SeedCreature(_alice, "Alice-Cur", "{B}"); // mv1
        var land = new Land("Plains");
        land.SetOwner(_alice); land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Bone Saw", ""); // mv0
        artifact.SetOwner(_alice); artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact); artifact.SetZone(ZoneType.Battlefield);

        var effects = PathOfPerilFactory.BuildResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        creature.Zone.Should().Be(ZoneType.Graveyard);
        land.Zone.Should().Be(ZoneType.Battlefield);
        artifact.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Resolve_EmptyBattlefields_IsCleanNoOp()
    {
        var effects = PathOfPerilFactory.BuildResolveEffect(new[] { _alice, _bob });
        var act = () => { foreach (var e in effects) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedCreature(Player owner, string name, string manaCost)
    {
        var c = new Creature(name, manaCost, power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
