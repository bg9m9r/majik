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
/// Unit tests for <see cref="ElvishReclaimerFactory"/>.
///
/// Card: Elvish Reclaimer — Creature — Elf Warrior {G}, 1/2 (Modern Horizons).
///   "This creature gets +2/+2 as long as there are three or more land cards
///    in your graveyard.
///    {2}, {T}, Sacrifice a land: Search your library for a land card, put it
///    onto the battlefield tapped, then shuffle."
///
/// Covers the card's UNIQUE behaviour vs. the Knight of the Reliquary analogue:
///   - Identity (mana cost, P/T, subtypes).
///   - Conditional +2/+2 Layer-7c pump keyed off the "three or more land cards"
///     graveyard threshold (CR 613.1g): below threshold → base 1/2; at/above
///     threshold → 3/4; non-lands and the opponent's graveyard don't count.
///   - Fetch ability shape: the printed extra {2} on top of {T}, Sacrifice.
///   - Resolution: sacrifices a land, tutors ANY land onto the battlefield
///     TAPPED, shuffles.
/// </summary>
[Trait("Color", "G")]
public class ElvishReclaimerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly ContinuousEffectsService _effects = new();
    private readonly EventBus _bus = new();
    private readonly ZoneService _zones;

    public ElvishReclaimerFactoryTests()
    {
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Reclaimer_Identity()
    {
        var card = ElvishReclaimerFactory.Create(_alice);

        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Conditional +2/+2 — "three or more land cards in your graveyard"
    // -----------------------------------------------------------------------

    [Fact]
    public void Pump_BelowThreshold_IsBaseStatLine()
    {
        var card = ElvishReclaimerFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Two lands < 3 → no pump.
        _alice.Zones.Graveyard.AddCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        _alice.Zones.Graveyard.AddCard(new Land("Island", subtypes: new[] { CardSubtype.Island }));

        var chars = _effects.Compute(card);

        chars.Power.Should().Be(1, "fewer than three lands in graveyard → base 1/2");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void Pump_AtThreshold_IsThreeFour()
    {
        var card = ElvishReclaimerFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        _alice.Zones.Graveyard.AddCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        _alice.Zones.Graveyard.AddCard(new Land("Mountain", subtypes: new[] { CardSubtype.Mountain }));
        _alice.Zones.Graveyard.AddCard(new Land("Island", subtypes: new[] { CardSubtype.Island }));

        var chars = _effects.Compute(card);

        chars.Power.Should().Be(3, "three lands → +2/+2 → 3/4");
        chars.Toughness.Should().Be(4);
    }

    [Fact]
    public void Pump_AboveThreshold_StillFlatPlusTwoPlusTwo()
    {
        var card = ElvishReclaimerFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        for (int i = 0; i < 6; i++)
        {
            _alice.Zones.Graveyard.AddCard(new Land($"Forest #{i}", subtypes: new[] { CardSubtype.Forest }));
        }

        var chars = _effects.Compute(card);

        // The pump is a FLAT +2/+2 (not per-land) — stays 3/4 above threshold.
        chars.Power.Should().Be(3);
        chars.Toughness.Should().Be(4);
    }

    [Fact]
    public void Pump_NonLandsAndOpponentGraveyard_DoNotCount()
    {
        var card = ElvishReclaimerFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Two real lands + non-lands in Alice's graveyard = still 2 lands < 3.
        _alice.Zones.Graveyard.AddCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        _alice.Zones.Graveyard.AddCard(new Land("Island", subtypes: new[] { CardSubtype.Island }));
        _alice.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}"));
        _alice.Zones.Graveyard.AddCard(new Sorcery("Wrath of God", "{2}{W}{W}"));
        // Bob's graveyard lands never count toward Alice's threshold.
        _bob.Zones.Graveyard.AddCard(new Land("Plains", subtypes: new[] { CardSubtype.Plains }));

        var chars = _effects.Compute(card);

        chars.Power.Should().Be(1, "only Alice's land CARDS count → 2 < 3 → no pump");
        chars.Toughness.Should().Be(2);
    }

    [Fact]
    public void CountLandsInGraveyard_CountsOnlyLandCards()
    {
        _alice.Zones.Graveyard.AddCard(new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        _alice.Zones.Graveyard.AddCard(new Instant("Lightning Bolt", "{R}"));
        _alice.Zones.Graveyard.AddCard(new Land("Wasteland"));

        ElvishReclaimerFactory.CountLandsInGraveyard(_alice).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Fetch ability shape — {2}, {T}, Sacrifice a land
    // -----------------------------------------------------------------------

    [Fact]
    public void FetchAbility_HasGenericTwo_AndTapCost()
    {
        var card = ElvishReclaimerFactory.Create(_alice);

        var fetch = card.Abilities.OfType<ActivatedAbility>().Single();

        // CR 117.5 — the printed cost carries an extra generic {2}.
        var manaCost = fetch.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(2, "fetch costs {2} in addition to {T}, Sacrifice a land");

        fetch.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
    }

    // -----------------------------------------------------------------------
    // Fetch ability resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void FetchResolution_SacsAnyLand_TutorsLandTapped_Shuffles()
    {
        var card = ElvishReclaimerFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Any land may be sacrificed — not gated to Forest/Plains.
        var sacForest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        sacForest.SetOwner(_alice);
        sacForest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sacForest);
        sacForest.SetZone(ZoneType.Battlefield);

        var fetched = new Land("Wasteland");
        fetched.SetOwner(_alice);
        _alice.Zones.Library.AddCard(fetched);
        fetched.SetZone(ZoneType.Library);

        var fetch = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in fetch.Effects) e.Execute();

        // Sacrificed land is in the graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(sacForest);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(sacForest);

        // Fetched land entered the battlefield TAPPED (the unique rider).
        _alice.Zones.Battlefield.GetCards().Should().Contain(fetched);
        _alice.Zones.Library.GetCards().Should().NotContain(fetched);
        fetched.IsTapped.Should().BeTrue("put onto the battlefield tapped");
    }

    [Fact]
    public void FetchResolution_NoOp_WhenNoLandToSacrifice()
    {
        var card = ElvishReclaimerFactory.Create(_alice, _effects, _bus, _zones);
        _zones.MoveCard(card, ZoneType.Library, ZoneType.Battlefield, _alice);

        // No land on the battlefield to sacrifice — closure no-ops.
        var inLibrary = new Land("Wasteland");
        inLibrary.SetOwner(_alice);
        _alice.Zones.Library.AddCard(inLibrary);
        inLibrary.SetZone(ZoneType.Library);

        var fetch = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in fetch.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Should().Contain(inLibrary,
            "no sacrifice → no tutor");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(inLibrary);
    }
}
