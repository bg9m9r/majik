using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KnightOfTheReliquaryFactory"/>.
///
/// Card: Knight of the Reliquary — Creature — Human Knight {1}{G}{W}, 2/2.
///   "Knight of the Reliquary gets +1/+1 for each land card in your
///    graveyard.
///    {T}, Sacrifice a Forest or Plains: Search your library for a land
///    card, put that card onto the battlefield, then shuffle."
///
/// Covers:
///   - Identity (name, types, subtypes, P/T, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Layer 7c self-pump:
///     - 0 lands in graveyard → 2/2.
///     - 3 lands in graveyard → 5/5.
///     - 7 lands in graveyard → 9/9.
///     - Non-land cards in graveyard don't count (Instant, Sorcery, etc.).
///     - Only the controller's graveyard counts (opponent's doesn't).
///     - Pump only active while on the battlefield.
///   - Tutor activated ability:
///     - Tap + sacrifice Forest cost on Knight's printed ability.
///     - Resolution: sacrifices a Forest, tutors any land, shuffles.
///     - Sacrifices a Plains when no Forest is available.
///     - No-op when no Forest or Plains is available.
///     - Pure helper <see cref="KnightOfTheReliquaryFactory.CountLandsInGraveyard"/>.
/// </summary>
public class KnightOfTheReliquaryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly ContinuousEffectsService _effects = new();
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public KnightOfTheReliquaryFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Knight_Identity()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice);

        knight.Name.Should().Be("Knight of the Reliquary");
        knight.ManaCost.Should().Be("{1}{G}{W}");
        knight.HasType(CardType.Creature).Should().BeTrue();
        knight.HasSubtype(CardSubtype.Human).Should().BeTrue();
        knight.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        knight.BasePower.Should().Be(2);
        knight.BaseToughness.Should().Be(2);
        knight.Owner.Should().BeSameAs(_alice);
        knight.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Knight_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Knight of the Reliquary", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Knight of the Reliquary");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{G}{W}");
    }

    // -----------------------------------------------------------------------
    // CountLandsInGraveyard pure helper
    // -----------------------------------------------------------------------

    [Fact]
    public void CountLandsInGraveyard_CountsOnlyLandCards()
    {
        _alice.Zones.Graveyard.AddCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        _alice.Zones.Graveyard.AddCard(new Land("Plains", subtypes: new[] { CardSubtype.Plains }));
        _alice.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}"));
        _alice.Zones.Graveyard.AddCard(new Land("Wasteland"));

        KnightOfTheReliquaryFactory.CountLandsInGraveyard(_alice).Should().Be(3);
    }

    [Fact]
    public void CountLandsInGraveyard_EmptyGraveyard_ReturnsZero()
    {
        KnightOfTheReliquaryFactory.CountLandsInGraveyard(_alice).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Layer 7c self-pump
    // -----------------------------------------------------------------------

    [Fact]
    public void Pump_ZeroLandsInGraveyard_IsBaseStatLine()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(knight, ZoneType.Library, ZoneType.Battlefield, _alice);

        var chars = _effects.Compute(knight);

        chars.Power.Should().Be(2, "0 lands in graveyard → no pump, base 2/2");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void Pump_ThreeLandsInGraveyard_IsFiveFive()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(knight, ZoneType.Library, ZoneType.Battlefield, _alice);

        _alice.Zones.Graveyard.AddCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        _alice.Zones.Graveyard.AddCard(new Land("Mountain", subtypes: new[] { CardSubtype.Mountain }));
        _alice.Zones.Graveyard.AddCard(new Land("Island", subtypes: new[] { CardSubtype.Island }));

        var chars = _effects.Compute(knight);

        chars.Power.Should().Be(5);
        chars.Toughness.Should().Be(5);
    }

    [Fact]
    public void Pump_SevenLandsInGraveyard_IsNineNine()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(knight, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (int i = 0; i < 7; i++)
        {
            _alice.Zones.Graveyard.AddCard(new Land($"Forest #{i}", subtypes: new[] { CardSubtype.Forest }));
        }

        var chars = _effects.Compute(knight);

        chars.Power.Should().Be(9);
        chars.Toughness.Should().Be(9);
    }

    [Fact]
    public void Pump_NonLandCardsInGraveyard_DontCount()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(knight, ZoneType.Library, ZoneType.Battlefield, _alice);

        _alice.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}"));
        _alice.Zones.Graveyard.AddCard(new Sorcery("Wrath of God", "{2}{W}{W}"));
        _alice.Zones.Graveyard.AddCard(new Creature("Tarmogoyf", "{1}{G}", 0, 1));

        var chars = _effects.Compute(knight);

        chars.Power.Should().Be(2, "no lands in graveyard → base 2/2");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void Pump_OpponentsGraveyard_DoesNotCount()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(knight, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Bob has lands in HIS graveyard; Alice's Knight only counts Alice's.
        _bob.Zones.Graveyard.AddCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        _bob.Zones.Graveyard.AddCard(new Land("Island", subtypes: new[] { CardSubtype.Island }));

        var chars = _effects.Compute(knight);

        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void Pump_NotActive_WhileOffBattlefield()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        // Knight stays in library (not on battlefield) — the effect's
        // IsActive gate plus the ETB/LTB lifecycle keep it dormant.
        _alice.Zones.Graveyard.AddCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));

        var pump = new KnightOfTheReliquaryFactory.LandsInGraveyardPumpEffect(knight);
        pump.IsActive().Should().BeFalse("Knight is not on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Tutor activated ability — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void TutorAbility_IsAttached_WithTapCost()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice);

        var activated = knight.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1, "Knight has one printed activated ability");

        var ability = activated[0];
        ability.Costs.Should().NotBeEmpty();
        // Tap cost is one of the AdditionalCost entries.
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
    }

    // -----------------------------------------------------------------------
    // Tutor activated ability — resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void TutorAbility_Resolution_SacsForest_TutorsLand_ShufflesLibrary()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(knight, ZoneType.Library, ZoneType.Battlefield, _alice);

        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);
        forest.SetZone(ZoneType.Battlefield);

        var sacredFoundry = new Land("Sacred Foundry",
            subtypes: new[] { CardSubtype.Mountain, CardSubtype.Plains });
        sacredFoundry.SetOwner(_alice);
        _alice.Zones.Library.AddCard(sacredFoundry);
        sacredFoundry.SetZone(ZoneType.Library);

        // Activate the ability — direct effect-firing (skip the cost
        // machinery; the closure does the sac payment itself).
        var ability = knight.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects)
        {
            e.Execute();
        }

        // Forest sacrificed: now in graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(forest);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(forest);

        // Sacred Foundry tutored onto the battlefield.
        _alice.Zones.Battlefield.GetCards().Should().Contain(sacredFoundry);
        _alice.Zones.Library.GetCards().Should().NotContain(sacredFoundry);
    }

    [Fact]
    public void TutorAbility_Resolution_SacsPlains_WhenNoForestAvailable()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(knight, ZoneType.Library, ZoneType.Battlefield, _alice);

        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);
        plains.SetZone(ZoneType.Battlefield);

        var anyLand = new Land("Wasteland");
        anyLand.SetOwner(_alice);
        _alice.Zones.Library.AddCard(anyLand);
        anyLand.SetZone(ZoneType.Library);

        var ability = knight.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(plains);
        _alice.Zones.Battlefield.GetCards().Should().Contain(anyLand);
    }

    [Fact]
    public void TutorAbility_Resolution_NoOpWhenNoSacrificeAvailable()
    {
        var knight = KnightOfTheReliquaryFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(knight, ZoneType.Library, ZoneType.Battlefield, _alice);

        // No Forest or Plains on the battlefield — closure no-ops.
        var land = new Land("Wasteland");
        land.SetOwner(_alice);
        _alice.Zones.Library.AddCard(land);
        land.SetZone(ZoneType.Library);

        var ability = knight.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().Contain(land,
            "no sacrifice → no tutor");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(land);
    }
}
