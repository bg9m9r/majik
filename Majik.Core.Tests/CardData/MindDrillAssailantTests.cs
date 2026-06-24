using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Mind Drill Assailant (Modern Horizons 3, {2}{U/B}{U/B},
/// Creature — Rat Warlock 2/5).
///
/// Covers the card's UNIQUE behaviour:
///   - Card identity (cost / P-T / subtypes) — one *_Identity assert.
///   - Threshold (CR 702.85) inactive below 7 graveyard cards: printed 2/5.
///   - Threshold active at exactly 7 graveyard cards: +3/+0 → 5/5.
///   - Threshold dynamic: dropping the 7th card into the graveyard lights up
///     the static on the next P/T read; toughness is unaffected (+0).
///
/// (NamedCardFactory dispatch + well-formedness are asserted for every
/// implemented card by CardFactoryContractTests, so no dispatch test here.)
/// </summary>
[Trait("Color", "M")]
public class MindDrillAssailantTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void SeedGraveyard(Player owner, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var card = new Card($"Seed{i}", "0", new[] { CardType.Creature });
            card.SetOwner(owner);
            owner.Zones.Graveyard.AddCard(card);
        }
    }

    private Creature CreateAndMoveToBattlefield(EventBus bus, ContinuousEffectsService effects)
    {
        var mda = MindDrillAssailantFactory.Create(_alice, bus, effects);
        mda.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(mda, ZoneType.Hand, ZoneType.Battlefield));
        return mda;
    }

    [Fact]
    public void MindDrillAssailant_Identity_RatWarlock_2_5_AtCost2UBUB()
    {
        var mda = MindDrillAssailantFactory.Create(_alice);

        mda.ManaCost.Should().Be("{2}{U/B}{U/B}");
        mda.HasType(CardType.Creature).Should().BeTrue();
        mda.HasSubtype(CardSubtype.Rat).Should().BeTrue();
        mda.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        mda.BasePower.Should().Be(2);
        mda.BaseToughness.Should().Be(5);
    }

    [Fact]
    public void ThresholdInactive_SixCards_Is_2_5()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var mda = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice, 6);

        MindDrillAssailantFactory.IsThresholdActive(_alice).Should().BeFalse();
        mda.Power.Should().Be(2);
        mda.Toughness.Should().Be(5);
    }

    [Fact]
    public void ThresholdActive_SevenCards_Is_5_5()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var mda = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice, 7);

        MindDrillAssailantFactory.IsThresholdActive(_alice).Should().BeTrue();
        mda.Power.Should().Be(5, "+3 power from threshold");
        mda.Toughness.Should().Be(5, "+0 toughness — threshold pumps power only");
    }

    [Fact]
    public void ThresholdDynamic_GainingSeventhCard_LightsUpStatic()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var mda = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice, 6);
        mda.Power.Should().Be(2, "6 cards is below threshold");

        // Drop the seventh card into the graveyard — threshold lights up on the
        // next P/T read. The graveyard add bypasses the event bus, so
        // invalidate the layer-system cache explicitly via Clear().
        var seventh = new Card("Seventh", "0", new[] { CardType.Instant });
        seventh.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(seventh);
        effects.Clear();

        mda.Power.Should().Be(5);
        mda.Toughness.Should().Be(5);
    }
}
