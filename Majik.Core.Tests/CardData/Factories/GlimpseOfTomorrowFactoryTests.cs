using FluentAssertions;
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
/// Tests for Glimpse of Tomorrow (Zendikar, {3}{R}, Sorcery).
///
/// Covers:
/// - Identity (name, type, cost, colour).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="GlimpseOfTomorrowFactory.Resolve"/>:
///   * counts permanents controlled before bulk-move.
///   * shuffles them into the library.
///   * reveals top of library until N nonland permanent cards are seen.
///   * those re-enter the battlefield; the others stay in the library.
/// </summary>
[Trait("Color", "R")]
public class GlimpseOfTomorrowFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeCost_Sorcery_Red()
    {
        var card = GlimpseOfTomorrowFactory.Create(_alice);

        card.Name.Should().Be("Glimpse of Tomorrow");
        card.ManaCost.Should().Be("{3}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Should().BeOfType<Sorcery>();
    }
    [Fact]
    public void SpellDefinition_NoTargets_NoX()
    {
        var def = GlimpseOfTomorrowFactory.BuildSpellDefinition(_alice);
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_BulkMovesPermanentsIntoLibrary_ThenRevealsN()
    {
        // Alice controls 2 permanents: a creature + an artifact.
        var bear = new Creature("Grizzly Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var mox = new Artifact("Mox", "{0}");
        mox.SetOwner(_alice);
        mox.SetController(_alice);
        mox.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mox);

        // Library already has 2 nonland permanents + 1 land + 1 instant.
        // After the shuffle in, all of {bear, mox, plus library cards} are
        // randomised. The reveal-until-N will yield 2 nonland permanent
        // hits (any of the 4 nonland permanents will do).
        var libCreature = new Creature("Hill Giant", "{3}{R}", 3, 3);
        libCreature.SetOwner(_alice);
        var libArtifact = new Artifact("Sol Ring", "{1}");
        libArtifact.SetOwner(_alice);
        var libLand = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice };
        var libInstant = new Instant("Bolt", "{R}");
        libInstant.SetOwner(_alice);
        foreach (var c in new ICard[] { libCreature, libArtifact, libLand, libInstant })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = GlimpseOfTomorrowFactory.Resolve(_alice);

        // Step 1 — both controller-side permanents got bulk-moved.
        result.ShuffledIn.Should().HaveCount(2);
        result.ShuffledIn.Should().Contain(new Permanent[] { bear, mox });

        // Step 2/3 — exactly 2 nonland permanent hits re-entered.
        result.RevealedHits.Should().HaveCount(2);
        result.RevealedHits.Should().AllSatisfy(c =>
        {
            c.Zone.Should().Be(ZoneType.Battlefield);
            IndomitableCreativityFactory.IsNonlandPermanentCard(c).Should().BeTrue();
        });

        // Battlefield count = 2 (the hits).
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(2);
    }

    [Fact]
    public void Resolve_NoPermanents_NoOp()
    {
        // No battlefield, no library — clean exit.
        var result = GlimpseOfTomorrowFactory.Resolve(_alice);
        result.ShuffledIn.Should().BeEmpty();
        result.RevealedHits.Should().BeEmpty();
        result.RevealedNonHits.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_LibraryEmptyAfterShuffleIn_StopsCleanly()
    {
        // Alice controls 3 permanents but starts with empty library.
        // After bulk-move, library has exactly 3 cards (all the permanents).
        // The reveal-until-N=3 may peel them all.
        var p1 = new Creature("A", "{R}", 1, 1); p1.SetOwner(_alice); p1.SetController(_alice);
        var p2 = new Creature("B", "{R}", 1, 1); p2.SetOwner(_alice); p2.SetController(_alice);
        var p3 = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
        foreach (var c in new Permanent[] { p1, p2, p3 })
        {
            c.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(c);
        }

        var result = GlimpseOfTomorrowFactory.Resolve(_alice);

        // 3 in. 2 nonland permanents come back. Land returns to library.
        result.ShuffledIn.Should().HaveCount(3);
        result.RevealedHits.Should().HaveCount(2);
        result.RevealedNonHits.Should().HaveCount(1);
        result.RevealedNonHits.Single().Should().BeOfType<Land>();
    }
}
