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
/// Tests for Indomitable Creativity (Amonkhet, {X}{R}{R}, Sorcery).
///
/// Covers:
/// - Identity (name, type, cost, colour).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - <see cref="IndomitableCreativityFactory.IsNonlandPermanentCard"/> —
///   permanent type roster (CR 110.4) minus lands.
/// - <see cref="IndomitableCreativityFactory.RevealUntilPermanent"/> peels
///   until a hit, hit enters battlefield, others shuffle back.
/// - <see cref="IndomitableCreativityFactory.Resolve"/> destroys the
///   chosen targets + runs one reveal per destroyed permanent.
/// - <see cref="SpellDefinition"/> shape: HasVariableX = true, one
///   target request.
/// </summary>
public class IndomitableCreativityFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Identity_NameTypeCost_Sorcery_Red()
    {
        var card = IndomitableCreativityFactory.Create(_alice);

        card.Name.Should().Be("Indomitable Creativity");
        card.ManaCost.Should().Be("{X}{R}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Should().BeOfType<Sorcery>();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_IndomitableCreativity()
    {
        var card = NamedCardFactory.Create("Indomitable Creativity", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Indomitable Creativity");
    }

    [Fact]
    public void SpellDefinition_HasVariableX_AndOneTargetRequest()
    {
        var def = IndomitableCreativityFactory.BuildSpellDefinition(new[] { _alice, _bob });

        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(0);
        def.TargetRequests[0].MaxTargets.Should().Be(int.MaxValue);
    }

    [Fact]
    public void IsNonlandPermanentCard_Creature_True()
    {
        var c = new Creature("Grizzly Bear", "{1}{G}", 2, 2);
        IndomitableCreativityFactory.IsNonlandPermanentCard(c).Should().BeTrue();
    }

    [Fact]
    public void IsNonlandPermanentCard_Artifact_True()
    {
        var a = new Artifact("Mox", "{0}");
        IndomitableCreativityFactory.IsNonlandPermanentCard(a).Should().BeTrue();
    }

    [Fact]
    public void IsNonlandPermanentCard_Enchantment_True()
    {
        var e = new Enchantment("Wrath Stop", "{2}{W}");
        IndomitableCreativityFactory.IsNonlandPermanentCard(e).Should().BeTrue();
    }

    [Fact]
    public void IsNonlandPermanentCard_Land_False()
    {
        var l = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        IndomitableCreativityFactory.IsNonlandPermanentCard(l).Should().BeFalse();
    }

    [Fact]
    public void IsNonlandPermanentCard_Instant_False()
    {
        var i = new Instant("Bolt", "{R}");
        IndomitableCreativityFactory.IsNonlandPermanentCard(i).Should().BeFalse();
    }

    [Fact]
    public void RevealUntilPermanent_HitsFirstCreature_LandsRestack_HitEntersBattlefield()
    {
        // Library top-to-bottom: Mountain, Mountain, Grizzly Bear, Bolt.
        var m1 = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
        var m2 = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
        var bear = new Creature("Grizzly Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        var bolt = new Instant("Bolt", "{R}");
        bolt.SetOwner(_alice);

        foreach (var c in new ICard[] { m1, m2, bear, bolt })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var ev = IndomitableCreativityFactory.RevealUntilPermanent(_alice);

        ev.Hit.Should().BeSameAs(bear);
        ev.Peeled.Should().HaveCount(2); // 2 mountains (the bear is the hit and is removed from Peeled).
        ev.Peeled.Should().Contain(new ICard[] { m1, m2 });

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(bear);

        // Bolt and mountains stay in the library (shuffled).
        var libCount = _alice.Zones.Library.GetCards().Count();
        libCount.Should().Be(3); // bolt + 2 mountains.
    }

    [Fact]
    public void RevealUntilPermanent_EmptyLibrary_NoHit_NoCrash()
    {
        var ev = IndomitableCreativityFactory.RevealUntilPermanent(_alice);
        ev.Hit.Should().BeNull();
        ev.Peeled.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_DestroysChosenTargets_AndRevealsPerDestroyed()
    {
        // Alice controls a Forest token she'll target.
        // Bob controls a Mox she'll target.
        // Each owner's library has [Mountain, Grizzly Bear] so the reveal hits the bear.
        var aliceCreature = new Creature("Hill Giant", "{3}{R}", 3, 3);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var bobArtifact = new Artifact("Mox Sapphire", "{0}");
        bobArtifact.SetOwner(_bob);
        bobArtifact.SetController(_bob);
        bobArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobArtifact);

        var aliceMountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice };
        var aliceBear = new Creature("Grizzly Bear", "{1}{G}", 2, 2);
        aliceBear.SetOwner(_alice);
        foreach (var c in new ICard[] { aliceMountain, aliceBear })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var bobIsland = new Land("Island", subtypes: new[] { CardSubtype.Island })
            { Owner = _bob };
        var bobBear = new Creature("Runeclaw Bear", "{1}{G}", 2, 2);
        bobBear.SetOwner(_bob);
        foreach (var c in new ICard[] { bobIsland, bobBear })
        {
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var result = IndomitableCreativityFactory.Resolve(new object[] { aliceCreature, bobArtifact });

        result.Destroyed.Should().HaveCount(2);
        result.Reveals.Should().HaveCount(2);

        // Both bears (hits) entered their respective owners' battlefields.
        aliceBear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(aliceBear);
        bobBear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);

        // The destroyed permanents are in their owners' graveyards.
        aliceCreature.Zone.Should().Be(ZoneType.Graveyard);
        bobArtifact.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Resolve_NonArtifactNonCreatureTarget_Skipped()
    {
        // A Land target — illegal at resolve (CR 608.2b).
        var land = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);

        var result = IndomitableCreativityFactory.Resolve(new object[] { land });

        result.Destroyed.Should().BeEmpty();
        result.Reveals.Should().BeEmpty();
        land.Zone.Should().Be(ZoneType.Battlefield); // Survived.
    }

    [Fact]
    public void Resolve_NoTargets_CleanNoOp()
    {
        var result = IndomitableCreativityFactory.Resolve(Array.Empty<object>());
        result.Destroyed.Should().BeEmpty();
        result.Reveals.Should().BeEmpty();
    }
}
