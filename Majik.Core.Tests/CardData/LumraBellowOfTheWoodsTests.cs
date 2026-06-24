using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Lumra, Bellow of the Woods — Legendary Creature —
/// Elemental Bear {4}{G}{G}, "Vigilance, reach. Lumra's power and toughness are
/// each equal to the number of lands you control. When Lumra enters, mill four
/// cards. Then return all land cards from your graveyard to the battlefield
/// tapped."
///
/// CR 604.3 / 613.2 — Layer 7a characteristic-defining P/T = lands you control.
/// CR 603.6a / 701.13 — ETB mill-then-return-lands trigger.
/// </summary>
[Trait("Color", "G")]
public class LumraBellowOfTheWoodsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public LumraBellowOfTheWoodsTests()
    {
        _zones = new ZoneService(_bus);
        ZoneServiceRegistry.Set(_alice, _zones);
    }

    private Func<IEnumerable<ICard>> AliceBattlefield =>
        () => _alice.Zones.Battlefield.GetCards();

    private Creature WireLumra()
    {
        var lumra = LumraBellowOfTheWoodsFactory.Create(
            _alice, _effects, _bus, AliceBattlefield, triggers: null);
        lumra.ActiveEffects = _effects;
        return lumra;
    }

    private static Land MakeLand(string name)
    {
        var land = new Land(name);
        return land;
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Lumra_IsLegendaryElementalBear_AtCost4GG_WithStarStar()
    {
        var lumra = LumraBellowOfTheWoodsFactory.Create(_alice);

        lumra.Name.Should().Be("Lumra, Bellow of the Woods");
        lumra.HasType(CardType.Creature).Should().BeTrue();
        lumra.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        lumra.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        lumra.HasSubtype(CardSubtype.Bear).Should().BeTrue();
        lumra.ManaCost.Should().Be("{4}{G}{G}");
        // Printed P/T is */* — seeded 0/0 (CR 208.2c).
        lumra.BasePower.Should().Be(0);
        lumra.BaseToughness.Should().Be(0);
    }

    [Fact]
    public void Lumra_HasVigilanceAndReachKeywords()
    {
        var lumra = LumraBellowOfTheWoodsFactory.Create(_alice);

        var keywords = lumra.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Vigilance");
        keywords.Should().Contain("Reach");
    }

    // -----------------------------------------------------------------------
    // Layer 7a — CDA P/T tracks lands you control
    // -----------------------------------------------------------------------

    [Fact]
    public void Lumra_NoLands_Is_0_0()
    {
        var lumra = WireLumra();
        _zones.MoveCard(lumra, ZoneType.Library, ZoneType.Battlefield, _alice);

        lumra.Power.Should().Be(0);
        lumra.Toughness.Should().Be(0);
    }

    [Fact]
    public void Lumra_PowerAndToughness_EqualLandsYouControl()
    {
        var lumra = WireLumra();
        _zones.MoveCard(lumra, ZoneType.Library, ZoneType.Battlefield, _alice);

        foreach (var name in new[] { "Forest", "Mountain", "Island" })
        {
            var land = MakeLand(name);
            land.SetOwner(_alice);
            land.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);
        }

        // A non-land creature on the battlefield does NOT count.
        var bear = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        bear.SetOwner(_alice);
        _alice.Zones.Battlefield.AddCard(bear);

        lumra.Power.Should().Be(3);
        lumra.Toughness.Should().Be(3);
    }

    [Fact]
    public void Lumra_PureHelper_CountsLands()
    {
        var forest = new Card("Forest", "", new[] { CardType.Land });
        var bolt = new Card("Lightning Bolt", "R", new[] { CardType.Instant });
        var island = new Card("Island", "", new[] { CardType.Land });

        LumraBellowOfTheWoodsFactory.CountLands(new ICard[] { forest, bolt, island })
            .Should().Be(2);
        LumraBellowOfTheWoodsFactory.CountLands(Array.Empty<ICard>())
            .Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — mill four, then return all land cards from graveyard
    // to the battlefield tapped (CR 603.6a / 701.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void Lumra_HasExactlyOneEnterTrigger()
    {
        var lumra = LumraBellowOfTheWoodsFactory.Create(_alice);

        lumra.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Lumra prints one ETB trigger: mill four, then return lands");
    }

    [Fact]
    public void Lumra_Etb_MillsFour()
    {
        var lumra = WireLumra();

        // Seven non-land cards on top of the library.
        for (var i = 0; i < 7; i++)
        {
            var c = new Card($"Spell {i}", "1G", new[] { CardType.Instant });
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
        }

        LumraBellowOfTheWoodsFactory.ResolveEtb(lumra, _alice);

        // Mill four — four spells now in the graveyard, three left in library.
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(4);
        _alice.Zones.Library.GetCards().Should().HaveCount(3);
    }

    [Fact]
    public void Lumra_Etb_ReturnsMilledLandsToBattlefieldTapped()
    {
        var lumra = WireLumra();

        // Top of library: two lands among non-land cards, so the mill puts the
        // lands into the graveyard, then the trigger returns them.
        var forest = MakeLand("Forest");
        var island = MakeLand("Island");
        forest.SetOwner(_alice);
        island.SetOwner(_alice);
        var filler1 = new Card("Spell A", "1G", new[] { CardType.Instant });
        var filler2 = new Card("Spell B", "1G", new[] { CardType.Instant });
        filler1.SetOwner(_alice);
        filler2.SetOwner(_alice);

        // Library order (top first): Forest, Spell A, Island, Spell B — all four
        // are milled.
        _alice.Zones.Library.AddCard(forest);
        _alice.Zones.Library.AddCard(filler1);
        _alice.Zones.Library.AddCard(island);
        _alice.Zones.Library.AddCard(filler2);

        LumraBellowOfTheWoodsFactory.ResolveEtb(lumra, _alice);

        // Both lands are back on the battlefield, tapped; the two spells stay
        // in the graveyard.
        var battlefield = _alice.Zones.Battlefield.GetCards().ToList();
        battlefield.Should().Contain(forest);
        battlefield.Should().Contain(island);
        forest.IsTapped.Should().BeTrue("returned lands enter tapped");
        island.IsTapped.Should().BeTrue("returned lands enter tapped");

        var graveyard = _alice.Zones.Graveyard.GetCards().ToList();
        graveyard.Should().Contain(filler1);
        graveyard.Should().Contain(filler2);
        graveyard.Should().NotContain(forest);
        graveyard.Should().NotContain(island);
    }

    [Fact]
    public void Lumra_Etb_ReturnsPreExistingGraveyardLands()
    {
        var lumra = WireLumra();

        // A land already in the graveyard (not milled this turn) is also
        // returned — "all land cards from your graveyard".
        var swamp = MakeLand("Swamp");
        swamp.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(swamp);
        swamp.SetZone(ZoneType.Graveyard);

        // A non-land card in the graveyard is left behind.
        var corpse = new Card("Grizzly Bears", "1G", new[] { CardType.Creature });
        corpse.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(corpse);
        corpse.SetZone(ZoneType.Graveyard);

        LumraBellowOfTheWoodsFactory.ResolveEtb(lumra, _alice);

        _alice.Zones.Battlefield.GetCards().Should().Contain(swamp);
        swamp.IsTapped.Should().BeTrue();
        _alice.Zones.Graveyard.GetCards().Should().Contain(corpse);
    }
}
