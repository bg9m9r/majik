using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Wildfire Wickerfolk (Modern Horizons 3, {R}{G}, Artifact Creature
/// — Scarecrow 3/2).
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (name / {R}{G} / 3/2 / Artifact + Creature / Scarecrow).
///   - Haste — printed, always on.
///   - Delirium inactive (3 types): printed 3/2, no trample.
///   - Delirium active (4+ types): +1/+1 → 4/3 AND has trample.
///   - Delirium dynamic: gaining a 4th distinct graveyard type lights up the
///     +1/+1 and the trample grant.
///
/// Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests — not re-asserted here.
/// </summary>
[Trait("Color", "M")]
public class WildfireWickerfolkFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void SeedGraveyard(Player owner, params CardType[][] typeBundles)
    {
        var i = 0;
        foreach (var types in typeBundles)
        {
            var card = new Card($"Seed{i++}", "0", types);
            card.SetOwner(owner);
            owner.Zones.Graveyard.AddCard(card);
        }
    }

    private Creature CreateAndMoveToBattlefield(EventBus bus, ContinuousEffectsService effects)
    {
        var ww = WildfireWickerfolkFactory.Create(_alice, bus, effects);
        ww.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(ww, ZoneType.Hand, ZoneType.Battlefield));
        return ww;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WildfireWickerfolk_Identity_ArtifactScarecrow_3_2_AtCostRG()
    {
        var ww = WildfireWickerfolkFactory.Create(_alice);

        ww.Name.Should().Be("Wildfire Wickerfolk");
        ww.ManaCost.Should().Be("{R}{G}");
        ww.HasType(CardType.Creature).Should().BeTrue();
        ww.HasType(CardType.Artifact).Should().BeTrue();
        ww.HasSubtype(CardSubtype.Scarecrow).Should().BeTrue();
        ww.BasePower.Should().Be(3);
        ww.BaseToughness.Should().Be(2);
        ww.Owner.Should().BeSameAs(_alice);
        ww.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Haste — printed, always on (CR 702.10)
    // -----------------------------------------------------------------------

    [Fact]
    public void WildfireWickerfolk_HasHaste()
    {
        var ww = WildfireWickerfolkFactory.Create(_alice);
        CombatAbilities.HasHaste(ww).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Delirium — conditional +1/+1 and trample (CR 702.105)
    // -----------------------------------------------------------------------

    [Fact]
    public void DeliriumInactive_ThreeTypes_Is_3_2_NoTrample()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var ww = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        WildfireWickerfolkFactory.IsDeliriumActive(_alice).Should().BeFalse();

        ww.Power.Should().Be(3);
        ww.Toughness.Should().Be(2);
        CombatAbilities.HasTrample(ww).Should().BeFalse();
    }

    [Fact]
    public void DeliriumActive_FourTypes_Is_4_3_AndHasTrample()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var ww = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        WildfireWickerfolkFactory.IsDeliriumActive(_alice).Should().BeTrue();

        ww.Power.Should().Be(4);
        ww.Toughness.Should().Be(3);
        CombatAbilities.HasTrample(ww).Should().BeTrue();
        // Haste is unaffected by delirium.
        CombatAbilities.HasHaste(ww).Should().BeTrue();
    }

    [Fact]
    public void DeliriumDynamic_GainingFourthType_LightsUpPumpAndTrample()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var ww = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        ww.Power.Should().Be(3, "3 types is below the delirium threshold");
        CombatAbilities.HasTrample(ww).Should().BeFalse();

        // Drop a fourth distinct type into the graveyard. The graveyard add
        // bypasses the event bus, so invalidate the layer-system cache via
        // Clear() (same pattern as GrimFlayerTests).
        var enchant = new Card("Holy Aura", "1W", new[] { CardType.Enchantment });
        enchant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(enchant);
        effects.Clear();

        ww.Power.Should().Be(4);
        ww.Toughness.Should().Be(3);
        CombatAbilities.HasTrample(ww).Should().BeTrue();
    }
}
