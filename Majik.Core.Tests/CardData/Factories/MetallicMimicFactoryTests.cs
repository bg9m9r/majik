using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Metallic Mimic (Aether Revolt — Artifact Creature —
/// Shapeshifter {2} 2/1).
///
/// Oracle (verified against Scryfall):
///   "As this creature enters, choose a creature type.
///    This creature is the chosen type in addition to its other types.
///    Each other creature you control of the chosen type enters with an
///    additional +1/+1 counter on it."
///
/// Coverage:
///   * Identity: Artifact Creature — Shapeshifter, {2}, 2/1.
///   * NamedCardFactory dispatch.
///   * Unwired single-arg path: no chosen type, no effects.
///   * As-enters type choice stored + exposed via GetChosenType.
///   * "This creature is the chosen type in addition to its other types" —
///     the chosen subtype is granted (additive; Shapeshifter preserved).
///   * Another creature of the chosen type you control enters with a +1/+1
///     counter.
///   * A creature NOT of the chosen type is unaffected.
///   * An opponent's creature of the chosen type is unaffected
///     ("creature YOU control", CR 109.5).
///   * Metallic Mimic itself does not get a counter ("each OTHER creature").
///   * Metallic Mimic leaving the battlefield lifts the replacement.
/// </summary>
public class MetallicMimicFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public MetallicMimicFactoryTests()
    {
        _zones = new ZoneService(_bus, _replacements);
    }

    private static Func<Player, CardSubtype> Choose(CardSubtype t) => _ => t;

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MetallicMimic_IsArtifactCreatureShapeshifter_2_1_AtCost2()
    {
        var c = MetallicMimicFactory.Create(_alice);

        c.Name.Should().Be("Metallic Mimic");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MetallicMimic_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Metallic Mimic", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Metallic Mimic");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    [Fact]
    public void MetallicMimic_SingleArgPath_NoChoice_NoEffects()
    {
        var c = MetallicMimicFactory.Create(_alice);

        MetallicMimicFactory.GetChosenType(c).Should().BeNull(
            "the single-arg path resolves no creature-type choice");
    }

    // -----------------------------------------------------------------------
    // As-enters choice + "is the chosen type" (CR 614.12 / CR 613.1d)
    // -----------------------------------------------------------------------

    [Fact]
    public void MetallicMimic_StoresChosenType()
    {
        var c = MetallicMimicFactory.Create(
            _alice, _effects, _replacements, _bus, Choose(CardSubtype.Goblin));

        MetallicMimicFactory.GetChosenType(c).Should().Be(CardSubtype.Goblin);
    }

    [Fact]
    public void MetallicMimic_IsChosenType_InAdditionToShapeshifter()
    {
        var c = MetallicMimicFactory.Create(
            _alice, _effects, _replacements, _bus, Choose(CardSubtype.Goblin));
        c.ActiveEffects = _effects;
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var chars = _effects.Compute((Permanent)c);
        chars.Subtypes.Should().Contain(CardSubtype.Goblin,
            "CR 613.1d — Metallic Mimic becomes the chosen type");
        chars.Subtypes.Should().Contain(CardSubtype.Shapeshifter,
            "the chosen type is gained IN ADDITION to its other types (CR 205.3)");
    }

    // -----------------------------------------------------------------------
    // "Each other creature you control of the chosen type enters with a
    // +1/+1 counter" (CR 614.1d)
    // -----------------------------------------------------------------------

    private Creature MimicOnBattlefield(CardSubtype chosen)
    {
        var mimic = MetallicMimicFactory.Create(
            _alice, _effects, _replacements, _bus, Choose(chosen));
        mimic.ActiveEffects = _effects;
        _alice.Zones.Library.AddCard(mimic);
        mimic.SetZone(ZoneType.Library);
        _zones.MoveCard(mimic, ZoneType.Library, ZoneType.Battlefield, _alice);
        return mimic;
    }

    [Fact]
    public void OtherCreatureOfChosenType_EntersWithCounter()
    {
        MimicOnBattlefield(CardSubtype.Goblin);

        var goblin = new Creature("Goblin Guide", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblin.SetOwner(_alice);
        goblin.SetController(_alice);
        _alice.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        _zones.MoveCard(goblin, ZoneType.Hand, ZoneType.Battlefield, _alice);

        goblin.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 614.1d — another Goblin you control enters with an additional +1/+1 counter");
    }

    [Fact]
    public void CreatureOfDifferentType_EntersWithoutCounter()
    {
        MimicOnBattlefield(CardSubtype.Goblin);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear });
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        _zones.MoveCard(bear, ZoneType.Hand, ZoneType.Battlefield, _alice);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "a Bear is not the chosen creature type (Goblin)");
    }

    [Fact]
    public void OpponentCreatureOfChosenType_EntersWithoutCounter()
    {
        MimicOnBattlefield(CardSubtype.Goblin);

        var oppGoblin = new Creature("Goblin Guide", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin });
        oppGoblin.SetOwner(_bob);
        oppGoblin.SetController(_bob);
        _bob.Zones.Hand.AddCard(oppGoblin);
        oppGoblin.SetZone(ZoneType.Hand);

        _zones.MoveCard(oppGoblin, ZoneType.Hand, ZoneType.Battlefield, _bob);

        oppGoblin.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "'creature YOU control' is controller-scoped (CR 109.5)");
    }

    [Fact]
    public void MetallicMimic_DoesNotGiveItselfACounter()
    {
        // Build a Goblin-named Mimic and route its OWN entry through
        // ZoneService while the replacement is live. "Each OTHER creature"
        // excludes the Mimic itself (CR 109.5).
        var mimic = MetallicMimicFactory.Create(
            _alice, _effects, _replacements, _bus, Choose(CardSubtype.Shapeshifter));
        mimic.ActiveEffects = _effects;
        _alice.Zones.Library.AddCard(mimic);
        mimic.SetZone(ZoneType.Library);

        _zones.MoveCard(mimic, ZoneType.Library, ZoneType.Battlefield, _alice);

        mimic.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "Metallic Mimic does not give ITSELF a counter ('each OTHER creature', CR 109.5)");
    }

    [Fact]
    public void MimicLeavesBattlefield_ReplacementLifts()
    {
        var mimic = MimicOnBattlefield(CardSubtype.Goblin);

        // Fires while the Mimic is out.
        var goblinBefore = new Creature("Goblin Guide", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinBefore.SetOwner(_alice);
        goblinBefore.SetController(_alice);
        _alice.Zones.Hand.AddCard(goblinBefore);
        goblinBefore.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblinBefore, ZoneType.Hand, ZoneType.Battlefield, _alice);
        goblinBefore.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // Mimic dies.
        _zones.MoveCard(mimic, ZoneType.Battlefield, ZoneType.Graveyard);

        // A fresh Goblin now enters without a counter.
        var goblinAfter = new Creature("Goblin Guide", "{R}", 2, 2,
            subtypes: new[] { CardSubtype.Goblin });
        goblinAfter.SetOwner(_alice);
        goblinAfter.SetController(_alice);
        _alice.Zones.Hand.AddCard(goblinAfter);
        goblinAfter.SetZone(ZoneType.Hand);
        _zones.MoveCard(goblinAfter, ZoneType.Hand, ZoneType.Battlefield, _alice);

        goblinAfter.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "the replacement must lift when Metallic Mimic leaves the battlefield");
    }
}
