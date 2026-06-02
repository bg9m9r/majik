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
/// Tests for Engulf the Shore (Eldritch Moon, {3}{U}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-01):
///   "Return to their owners' hands all creatures with toughness less than
///    or equal to the number of Islands you control."
///
/// A mass return-to-hand bounce gated by a dynamic threshold = the caster's
/// Island count. The bounce analogue of a board sweep:
///   - Like <see cref="EchoingTruthFactory"/> it returns permanents to their
///     owners' hands (CR 701.10), each to ITS OWN owner's hand.
///   - Like <see cref="BoilFactory"/> / <see cref="PyroclasmFactory"/> it
///     sweeps every battlefield (controller-agnostic on the creatures
///     bounced), exposing a positional <c>BuildResolveEffect(caster,
///     allPlayers)</c> so tests / bot probes can fire it without the full
///     cast flow.
///
/// "Islands you control" (CR 109.5 — "you" = the spell's controller) counts
/// the CASTER's Islands only; the toughness threshold is computed once at
/// resolution.
///
/// Covers:
///   - Card identity (Instant, {3}{U}, owner / controller).
///   - NamedCardFactory dispatch.
///   - Resolve: bounces creatures whose toughness <= caster's Island count,
///     on BOTH players' battlefields, each to its own owner's hand.
///   - Resolve: leaves creatures with toughness > the threshold on the
///     battlefield.
///   - Resolve: threshold uses the CASTER's Islands, not the opponent's.
///   - Resolve: zero Islands → only 0-toughness creatures could match (none
///     here) → clean no-op on a normal board.
///   - Resolve: noncreature permanents (artifacts, lands) are untouched.
/// </summary>
public class EngulfTheShoreTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EngulfTheShore_IsInstant_AtCost3U()
    {
        var card = EngulfTheShoreFactory.Create(_alice);

        card.Name.Should().Be("Engulf the Shore");
        card.ManaCost.Should().Be("{3}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EngulfTheShore()
    {
        var card = NamedCardFactory.Create("Engulf the Shore", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Engulf the Shore");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{U}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — toughness threshold = caster's Island count
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_BouncesCreatures_AtOrBelowCastersIslandCount_AcrossBothPlayers()
    {
        // Alice (caster) controls 3 Islands → threshold = 3.
        AddIslands(_alice, 3);

        var aliceBear = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2); // T2 <= 3
        var aliceWall = NewCreature(_alice, "Wall of Omens", "{1}{W}", 0, 4);  // T4 > 3
        var bobExactly = NewCreature(_bob, "Hill Giant", "{3}{R}", 3, 3);      // T3 == 3
        var bobBig = NewCreature(_bob, "Craw Wurm", "{4}{G}{G}", 6, 4);        // T4 > 3

        Resolve(caster: _alice);

        aliceBear.Zone.Should().Be(ZoneType.Hand,
            "toughness 2 <= 3 Islands — bounced to its owner's hand (CR 701.10)");
        bobExactly.Zone.Should().Be(ZoneType.Hand,
            "toughness 3 <= 3 Islands — the boundary is inclusive (less than OR equal to)");

        aliceWall.Zone.Should().Be(ZoneType.Battlefield,
            "toughness 4 > 3 Islands — stays put");
        bobBig.Zone.Should().Be(ZoneType.Battlefield,
            "toughness 4 > 3 Islands — stays put");

        _alice.Zones.Hand.GetCards().Should().Contain(aliceBear);
        _bob.Zones.Hand.GetCards().Should().Contain(bobExactly,
            "each bounced creature returns to ITS OWN owner's hand (owners' hands, plural)");
    }

    [Fact]
    public void Resolve_ThresholdCounts_CastersIslandsOnly_NotOpponents()
    {
        // Bob controls 5 Islands, Alice (caster) controls 0. Threshold = 0.
        AddIslands(_bob, 5);

        var aliceBear = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobBear = NewCreature(_bob, "Runeclaw Bear", "{1}{G}", 2, 2);

        Resolve(caster: _alice);

        aliceBear.Zone.Should().Be(ZoneType.Battlefield,
            "Alice controls 0 Islands → threshold 0 → a 2-toughness creature stays");
        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "'Islands you control' counts the CASTER's Islands, never the opponent's");
    }

    [Fact]
    public void Resolve_NoncreaturePermanents_AreUntouched()
    {
        AddIslands(_alice, 4);

        var bear = NewCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var land = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        Resolve(caster: _alice);

        bear.Zone.Should().Be(ZoneType.Hand, "creatures within the threshold are bounced");
        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "only creatures are bounced — artifacts stay");
        land.Zone.Should().Be(ZoneType.Battlefield,
            "only creatures are bounced — lands stay (the caster's own Islands included)");
    }

    [Fact]
    public void Resolve_ZeroIslands_LeavesNormalCreaturesAlone()
    {
        // No Islands → threshold 0; no creature here has toughness <= 0.
        var bear = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobBear = NewCreature(_bob, "Runeclaw Bear", "{1}{G}", 2, 2);

        Resolve(caster: _alice);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        bobBear.Zone.Should().Be(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(Player caster)
    {
        var effects = EngulfTheShoreFactory.BuildResolveEffect(
            caster: caster,
            allPlayers: new[] { _alice, _bob });
        foreach (var fx in effects) fx.Execute();
    }

    private static void AddIslands(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
            island.SetOwner(owner);
            island.SetController(owner);
            owner.Zones.Battlefield.AddCard(island);
            island.SetZone(ZoneType.Battlefield);
        }
    }

    private static Creature NewCreature(
        Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
