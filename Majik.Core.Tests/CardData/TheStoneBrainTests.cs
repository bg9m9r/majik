using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="TheStoneBrainFactory"/> — Legendary Artifact {2}:
///   "{2}, {T}, Exile The Stone Brain: Choose a card name. Search target
///    opponent's graveyard, hand, and library for up to four cards with that
///    name and exile them. That player shuffles, then draws a card for each
///    card exiled from their hand this way. Activate only as a sorcery."
///
/// Covers:
/// - Card identity (Legendary Artifact, {2}, owner / controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: one sorcery-speed ActivatedAbility with {2}+{T} costs and
///   one "target player" TargetRequest.
/// - Resolve: name a card → exile matches across graveyard/hand/library,
///   capped at four; draw one per hand exile; shuffle.
/// - The Brain exiles itself as part of the cost.
/// - Empty / null name → no exiles, no draws, still shuffles.
/// </summary>
public class TheStoneBrainTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TheStoneBrain_IsLegendaryArtifact_WithGenericTwoCost()
    {
        var brain = TheStoneBrainFactory.Create(_alice);

        brain.HasType(CardType.Artifact).Should().BeTrue();
        brain.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        brain.Name.Should().Be("The Stone Brain");
        brain.ManaCost.Should().Be("{2}");
        brain.Owner.Should().BeSameAs(_alice);
        brain.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TheStoneBrain()
    {
        var card = NamedCardFactory.Create("The Stone Brain", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("The Stone Brain");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TheStoneBrain_HasOneSorcerySpeedActivatedAbility_WithManaTapCostsAndPlayerTarget()
    {
        var brain = TheStoneBrainFactory.Create(_alice);

        var ability = brain.Abilities.OfType<ActivatedAbility>().Single();

        ability.IsSorcerySpeed.Should().BeTrue("\"Activate only as a sorcery\"");

        ability.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Description.Contains("2"));
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the ability costs {T}");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Resolve: name a card → exile matches + draw per hand exile + shuffle
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ExilesMatchesFromAllThreeZones_AndDrawsPerHandExile()
    {
        // Bob: 1 named card in graveyard, 2 in hand, 1 in library, plus a
        // non-matching card in each zone. Naming "Bauble" exiles all four
        // matches (cap is four) and draws 2 (the two from hand).
        var gy = SeedCard(_bob, ZoneType.Graveyard, "Bauble");
        var hand1 = SeedCard(_bob, ZoneType.Hand, "Bauble");
        var hand2 = SeedCard(_bob, ZoneType.Hand, "Bauble");
        var lib = SeedCard(_bob, ZoneType.Library, "Bauble");

        var keepGy = SeedCard(_bob, ZoneType.Graveyard, "Counterspell");
        var keepHand = SeedCard(_bob, ZoneType.Hand, "Counterspell");
        // Library decoys so the draw has cards to pull (CR 614 draw).
        var libDecoy1 = SeedCard(_bob, ZoneType.Library, "Forest");
        var libDecoy2 = SeedCard(_bob, ZoneType.Library, "Forest");

        var brain = MakeOnBattlefield(_alice, nameSelector: _ => "Bauble", target: _bob);
        ResolveAbility(brain);

        // All four named matches are exiled.
        _bob.Zones.Exile.GetCards().Should().Contain(new[] { gy, hand1, hand2, lib });
        gy.Zone.Should().Be(ZoneType.Exile);
        hand1.Zone.Should().Be(ZoneType.Exile);
        hand2.Zone.Should().Be(ZoneType.Exile);
        lib.Zone.Should().Be(ZoneType.Exile);

        // Non-matching cards stay put.
        _bob.Zones.Graveyard.GetCards().Should().Contain(keepGy);
        _bob.Zones.Hand.GetCards().Should().Contain(keepHand);

        // Two cards exiled from hand → Bob draws two (the library decoys).
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { libDecoy1, libDecoy2 });
        libDecoy1.Zone.Should().Be(ZoneType.Hand);
        libDecoy2.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_CapsExileAtFourMatches()
    {
        // Six matching cards in Bob's library; only four may be exiled.
        var matches = Enumerable.Range(0, 6)
            .Select(_ => SeedCard(_bob, ZoneType.Library, "Bauble"))
            .ToList();

        var brain = MakeOnBattlefield(_alice, nameSelector: _ => "Bauble", target: _bob);
        ResolveAbility(brain);

        _bob.Zones.Exile.GetCards().Count(c => c.Name == "Bauble").Should().Be(4);
        _bob.Zones.Library.GetCards().Count(c => c.Name == "Bauble").Should().Be(2);
    }

    [Fact]
    public void Resolve_NoHandMatches_NoDraw()
    {
        // Two matches in graveyard only — nothing from hand, so no draw.
        SeedCard(_bob, ZoneType.Graveyard, "Bauble");
        SeedCard(_bob, ZoneType.Graveyard, "Bauble");
        var libDecoy = SeedCard(_bob, ZoneType.Library, "Forest");

        var brain = MakeOnBattlefield(_alice, nameSelector: _ => "Bauble", target: _bob);
        ResolveAbility(brain);

        _bob.Zones.Exile.GetCards().Count(c => c.Name == "Bauble").Should().Be(2);
        // No hand exile → no draw → the library decoy stays in the library.
        _bob.Zones.Library.GetCards().Should().Contain(libDecoy);
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NullName_NoExilesNoDraw()
    {
        var hand = SeedCard(_bob, ZoneType.Hand, "Bauble");
        var lib = SeedCard(_bob, ZoneType.Library, "Forest");

        var brain = MakeOnBattlefield(_alice, nameSelector: _ => null, target: _bob);
        ResolveAbility(brain);

        _bob.Zones.Exile.GetCards().Should().BeEmpty();
        _bob.Zones.Hand.GetCards().Should().Contain(hand);
        _bob.Zones.Library.GetCards().Should().Contain(lib);
    }

    // -----------------------------------------------------------------------
    // The Brain exiles itself as part of the cost.
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ExilesTheStoneBrainItself()
    {
        var brain = MakeOnBattlefield(_alice, nameSelector: _ => "Bauble", target: _bob);

        brain.Zone.Should().Be(ZoneType.Battlefield);
        ResolveAbility(brain);

        brain.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(brain);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(brain);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ICard SeedCard(Player p, ZoneType zone, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        switch (zone)
        {
            case ZoneType.Graveyard: p.Zones.Graveyard.AddCard(c); break;
            case ZoneType.Hand: p.Zones.Hand.AddCard(c); break;
            case ZoneType.Library: p.Zones.Library.AddCard(c); break;
            default: throw new ArgumentOutOfRangeException(nameof(zone));
        }
        c.SetZone(zone);
        return c;
    }

    private static Artifact MakeOnBattlefield(
        Player owner, Func<Player, string?> nameSelector, Player target)
    {
        var brain = TheStoneBrainFactory.Create(owner, nameSelector, target);
        owner.Zones.Battlefield.AddCard(brain);
        brain.SetZone(ZoneType.Battlefield);
        return brain;
    }

    private static void ResolveAbility(Artifact brain)
    {
        var ability = brain.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();
    }
}
