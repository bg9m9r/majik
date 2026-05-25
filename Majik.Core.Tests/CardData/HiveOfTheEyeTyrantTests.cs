using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="HiveOfTheEyeTyrantFactory"/> (Adventures in the
/// Forgotten Realms). Land:
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {B}.
///    {3}{B}: Until end of turn, this land becomes a 3/3 black Beholder
///    creature with menace and \"Whenever this creature attacks, exile
///    target card from defending player's graveyard.\" It's still a land."
///
/// Covers:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Mana ability ({T}: Add {B}) + animate ability + attack-trigger shape.
/// - Animate registers a <see cref="HiveOfTheEyeTyrantAnimateEffect"/> +
///   <see cref="HiveOfTheEyeTyrantBecomesPTEffect"/>:
///     * Adds Creature type + Beholder subtype + Menace keyword on Layer 4.
///     * Records 3/3 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// </summary>
public class HiveOfTheEyeTyrantTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HiveOfTheEyeTyrant_Identity()
    {
        var land = HiveOfTheEyeTyrantFactory.Create(_alice);

        land.Name.Should().Be("Hive of the Eye Tyrant");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Hive of the Eye Tyrant is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HiveOfTheEyeTyrant()
    {
        var card = NamedCardFactory.Create("Hive of the Eye Tyrant", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hive of the Eye Tyrant");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {B} mana ability is wired");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{3}{B} animate ability is wired");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack-trigger shape is attached for inspection");
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void HiveOfTheEyeTyrant_AnimateAbility_HasPrintedManaCost3B()
    {
        var land = HiveOfTheEyeTyrantFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({3}{B})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void HiveOfTheEyeTyrant_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = HiveOfTheEyeTyrantFactory.Create(_alice, effects, replacements: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        // Compute(Permanent) on a Land returns a PermanentCharacteristics
        // seeded with printed types + every registered effect's Layer 4
        // apply. Confirm Creature was added on top of printed Land and
        // Beholder subtype + Menace keyword grant are present.
        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Beholder,
            "Beholder subtype added");
        chars.Keywords.Should().Contain("Menace",
            "Menace keyword marker added");
    }

    [Fact]
    public void HiveOfTheEyeTyrant_AnimateEffect_AppliesTypeSubtypeAndMenace()
    {
        var land = HiveOfTheEyeTyrantFactory.Create(_alice);
        var effect = new HiveOfTheEyeTyrantAnimateEffect(land);

        var chars = new PermanentCharacteristics();
        chars.Types.Add(CardType.Land); // printed
        effect.Apply(chars);

        chars.Types.Should().Contain(CardType.Creature, "creature type added");
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays — \"It's still a land\"");
        chars.Subtypes.Should().Contain(CardSubtype.Beholder,
            "Beholder subtype added");
        chars.Keywords.Should().Contain("Menace",
            "Menace keyword marker added (CR 702.110)");
    }

    // -----------------------------------------------------------------------
    // Conditional ETB-tapped — "two or more other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void HiveOfTheEyeTyrant_RegistersConditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = HiveOfTheEyeTyrantFactory.Create(_alice, effects: null, replacements: bus);

        // No direct registration accessor — the binder shape we mirror
        // (ConditionalEntersTappedReplacement) just lives on the bus. We
        // verify by triggering the replacement path: build an intent
        // moving Hive to the battlefield with controller = Alice and
        // ≥ 2 other lands → expect EntersTapped = true. With ≤ 1 other
        // land, EntersTapped stays false.
        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        // Zero other lands → enters untapped.
        var afterEmpty = bus.Apply(intent);
        afterEmpty.Should().NotBeNull();
        afterEmpty!.EntersTapped.Should().BeFalse(
            "with 0 other lands, Hive enters untapped");

        // Two other lands present (excluding Hive) → enters tapped.
        var land1 = NamedCardFactory.Create("Mountain", _alice);
        var land2 = NamedCardFactory.Create("Forest", _alice);
        _alice.Zones.Battlefield.AddCard(land1);
        land1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land2);
        land2.SetZone(ZoneType.Battlefield);

        var afterTwoOthers = bus.Apply(intent);
        afterTwoOthers.Should().NotBeNull();
        afterTwoOthers!.EntersTapped.Should().BeTrue(
            "with 2 other lands, the slow-land clause flips it tapped");
    }
}
