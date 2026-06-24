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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WildfireWickerfolkFactory"/> — Wildfire Wickerfolk
/// ({R}{G}, Artifact Creature — Scarecrow 3/2).
///
/// Oracle text (verified against Scryfall 2026-06-24):
///   "Haste
///    Delirium — This creature gets +1/+1 and has trample as long as there are
///    four or more card types among cards in your graveyard."
///
/// Covers:
/// - Identity (name, types incl. Artifact, Scarecrow subtype, cost, P/T,
///   owner/controller).
/// - Intrinsic Haste (CR 702.10).
/// - Delirium active (4+ types in graveyard): +1/+1 AND trample (CR 702.105 /
///   702.19).
/// - Delirium inactive (3 types): printed 3/2, no trample.
/// - Delirium dynamic: gaining a 4th type while on the battlefield lights up
///   the static.
///
/// NamedCardFactory dispatch + well-formedness are asserted globally by
/// CardFactoryContractTests, so no dispatch test here.
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
        var wf = WildfireWickerfolkFactory.Create(_alice, bus, effects);
        wf.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(wf, ZoneType.Hand, ZoneType.Battlefield));
        return wf;
    }

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Identity_ArtifactCreatureScarecrow_3_2_AtCostRG()
    {
        var wf = WildfireWickerfolkFactory.Create(_alice);

        wf.Name.Should().Be("Wildfire Wickerfolk");
        wf.ManaCost.Should().Be("{R}{G}");
        wf.HasType(CardType.Creature).Should().BeTrue();
        wf.HasType(CardType.Artifact).Should().BeTrue();
        wf.HasSubtype(CardSubtype.Scarecrow).Should().BeTrue();
        wf.BasePower.Should().Be(3);
        wf.BaseToughness.Should().Be(2);
        wf.Owner.Should().BeSameAs(_alice);
        wf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasHaste()
    {
        // CR 702.10 — Haste is intrinsic (printed keyword marker).
        var wf = WildfireWickerfolkFactory.Create(_alice);
        CombatAbilities.HasHaste(wf).Should().BeTrue();
    }

    [Fact]
    public void WithoutDelirium_DoesNotHaveTrample()
    {
        // The base shape has no trample — it's only granted by delirium.
        var wf = WildfireWickerfolkFactory.Create(_alice);
        CombatAbilities.HasTrample(wf).Should().BeFalse();
    }

    // ── Delirium — +1/+1 and trample ─────────────────────────────────────

    [Fact]
    public void DeliriumInactive_ThreeTypes_Is_3_2_NoTrample()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var wf = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        WildfireWickerfolkFactory.IsDeliriumActive(_alice).Should().BeFalse();

        wf.Power.Should().Be(3);
        wf.Toughness.Should().Be(2);
        CombatAbilities.HasTrample(wf).Should().BeFalse();
    }

    [Fact]
    public void DeliriumActive_FourTypes_Is_4_3_WithTrample()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();
        var wf = CreateAndMoveToBattlefield(bus, effects);

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery },
            new[] { CardType.Artifact });

        WildfireWickerfolkFactory.IsDeliriumActive(_alice).Should().BeTrue();

        wf.Power.Should().Be(4);
        wf.Toughness.Should().Be(3);
        CombatAbilities.HasTrample(wf).Should().BeTrue(
            "delirium grants trample (CR 702.105 / CR 702.19).");
        CombatAbilities.HasHaste(wf).Should().BeTrue(
            "Haste is intrinsic and unaffected by delirium.");
    }

    [Fact]
    public void DeliriumDynamic_GainingFourthType_LightsUpStatic()
    {
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();

        var wf = WildfireWickerfolkFactory.Create(_alice, bus, effects);
        wf.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(wf, ZoneType.Hand, ZoneType.Battlefield));

        SeedGraveyard(_alice,
            new[] { CardType.Creature },
            new[] { CardType.Instant },
            new[] { CardType.Sorcery });

        wf.Power.Should().Be(3, "3 types is below threshold");
        CombatAbilities.HasTrample(wf).Should().BeFalse();

        // Drop a fourth distinct type into the graveyard — delirium lights up
        // on the next read. The graveyard add bypasses the event bus, so
        // invalidate the layer-system cache explicitly via Clear().
        var enchant = new Card("Holy Aura", "1W", new[] { CardType.Enchantment });
        enchant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(enchant);
        effects.Clear();

        wf.Power.Should().Be(4);
        wf.Toughness.Should().Be(3);
        CombatAbilities.HasTrample(wf).Should().BeTrue();
    }
}
