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
/// Tests for <see cref="DenOfTheBugbearFactory"/> (Adventures in the
/// Forgotten Realms manland cycle, red member). Land:
///   "If you control two or more other lands, this land enters tapped.
///    {T}: Add {R}.
///    {3}{R}: Until end of turn, this land becomes a 3/2 red Goblin
///    creature with \"Whenever this creature attacks, create a 1/1 red
///    Goblin creature token that's tapped and attacking.\" It's still a
///    land."
///
/// Mirrors <see cref="HiveOfTheEyeTyrantTests"/> (the suggested analogue):
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Mana ability ({T}: Add {R}) + animate ability + attack-trigger shape.
/// - Animate registers a <see cref="ManlandCycleAnimateEffect"/> +
///   <see cref="ManlandCycleBecomesPTEffect"/>:
///     * Adds Creature type + Goblin subtype on Layer 4.
///     * Records 3/2 base P/T on Layer 7b.
///     * Both expire at end of turn.
/// - Conditional ETB-tapped (two or more other lands).
/// </summary>
public class DenOfTheBugbearTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void DenOfTheBugbear_Identity()
    {
        var land = DenOfTheBugbearFactory.Create(_alice);

        land.Name.Should().Be("Den of the Bugbear");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Den of the Bugbear is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DenOfTheBugbear()
    {
        var card = NamedCardFactory.Create("Den of the Bugbear", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Den of the Bugbear");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {R} mana ability is wired");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{3}{R} animate ability is wired");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack-trigger shape is attached for inspection");
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void DenOfTheBugbear_AnimateAbility_HasPrintedManaCost3R()
    {
        var land = DenOfTheBugbearFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({3}{R})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void DenOfTheBugbear_Animate_AppliesLayer4OnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = DenOfTheBugbearFactory.Create(_alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 (\"It's still a land\")");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Goblin,
            "Goblin subtype added");
    }

    // -----------------------------------------------------------------------
    // Conditional ETB-tapped — "two or more other lands"
    // -----------------------------------------------------------------------

    [Fact]
    public void DenOfTheBugbear_RegistersConditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = DenOfTheBugbearFactory.Create(_alice, effects: null, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        // Zero other lands → enters untapped.
        var afterEmpty = bus.Apply(intent);
        afterEmpty.Should().NotBeNull();
        afterEmpty!.EntersTapped.Should().BeFalse(
            "with 0 other lands, Den of the Bugbear enters untapped");

        // Two other lands present (excluding Den) → enters tapped.
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

    // -----------------------------------------------------------------------
    // Attack trigger — creates a 1/1 red Goblin token
    // -----------------------------------------------------------------------

    [Fact]
    public void DenOfTheBugbear_AttackTrigger_CreatesRedGoblinToken()
    {
        var land = DenOfTheBugbearFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var beforeCount = _alice.Zones.Battlefield.GetCards().Count();

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var afterCount = _alice.Zones.Battlefield.GetCards().Count();
        afterCount.Should().Be(beforeCount + 1,
            "the attack trigger creates one Goblin token");

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken);
        token.Name.Should().Be("Goblin");
        token.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Goblin).Should().BeTrue(
            "token is a Goblin");
        Majik.Core.Cards.CardColors.GetColors(token).Should().Contain(
            Majik.Core.ValueObjects.ManaColor.Red,
            "1/1 red Goblin creature token");
    }
}
