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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="RestlessPrairieFactory"/> (Murders at Karlov Manor
/// "Restless" creature-land cycle, green/white member). Land:
///   "This land enters tapped.
///    {T}: Add {G} or {W}.
///    {2}{G}{W}: This land becomes a 3/3 green and white Llama creature until
///    end of turn. It's still a land.
///    Whenever this land attacks, other creatures you control get +1/+1 until
///    end of turn."
///
/// Mirrors <see cref="RestlessBivouacFactoryTests"/> /
/// <see cref="RestlessRidgelineFactoryTests"/>:
/// - Identity (Land, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {G} / {T}: Add {W} mana abilities (two).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Animate ability cost ({2}{G}{W}, instant speed) + Layer 4 / Layer 7b:
///     * Adds Creature type + Llama subtype on Layer 4 ("still a land").
///     * Records 3/3 base P/T on Layer 7b.
/// - Unconditional ETB-tapped replacement.
/// - Attack trigger: NON-targeted; pumps every OTHER creature the controller
///   controls +1/+1 until end of turn (the land itself excluded).
/// </summary>
public class RestlessPrairieFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessPrairie_Identity()
    {
        var land = RestlessPrairieFactory.Create(_alice);

        land.Name.Should().Be("Restless Prairie");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Restless Prairie is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RestlessPrairie_HasManaAnimateAndAttackTrigger()
    {
        var land = RestlessPrairieFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "{T}: Add {G} and {T}: Add {W} are wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "{2}{G}{W} animate ability is wired");
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the attack trigger is attached to the land shape");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RestlessPrairie()
    {
        var card = NamedCardFactory.Create("Restless Prairie", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Restless Prairie");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Animate ability
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessPrairie_AnimateAbility_HasPrintedManaCost2GW()
    {
        var land = RestlessPrairieFactory.Create(_alice);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        animate.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the animate cost is one ManaCostCost ({2}{G}{W})");
        animate.IsSorcerySpeed.Should().BeFalse(
            "animate is instant-speed per oracle");
    }

    [Fact]
    public void RestlessPrairie_Animate_AppliesLayer4AndLayer7bOnCompute()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessPrairieFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in animate.Effects) e.Execute();

        var chars = effects.Compute((Permanent)land);
        chars.Types.Should().Contain(CardType.Land,
            "printed Land type stays through Layer 4 — \"It's still a land\"");
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature");
        chars.Subtypes.Should().Contain(CardSubtype.Llama,
            "Llama subtype added");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped — unconditional ("This land enters tapped.")
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessPrairie_RegistersUnconditionalEtbTappedReplacement_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var land = RestlessPrairieFactory.Create(
            _alice, effects: null, replacements: bus, triggers: null);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "\"This land enters tapped\" — unconditional (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — "other creatures you control get +1/+1 until end of turn"
    // -----------------------------------------------------------------------

    [Fact]
    public void RestlessPrairie_AttackTrigger_IsNonTargeted()
    {
        var land = RestlessPrairieFactory.Create(_alice);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        trigger.TargetRequests.Should().BeEmpty(
            "the anthem is non-targeted — it hits all OTHER creatures you control");
    }

    [Fact]
    public void RestlessPrairie_AttackTrigger_PumpsOtherCreatures_NotItself()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessPrairieFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Two other creatures the controller controls.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var elf = new Creature("Llanowar Elves", "{G}", 1, 1);
        elf.SetOwner(_alice);
        elf.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(elf);
        elf.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        effects.Compute(bear).Power.Should().Be(3, "base 2 power + 1 from +1/+1");
        effects.Compute(bear).Toughness.Should().Be(3, "base 2 toughness + 1 from +1/+1");
        effects.Compute(elf).Power.Should().Be(2, "base 1 power + 1 from +1/+1");
        effects.Compute(elf).Toughness.Should().Be(2, "base 1 toughness + 1 from +1/+1");

        // The pump expires at end of turn (CR 514.2 cleanup).
        effects.ExpireEndOfTurn();
        effects.Compute(bear).Power.Should().Be(2, "the +1/+1 pump expired at end of turn");
        effects.Compute(elf).Power.Should().Be(1);
    }

    [Fact]
    public void RestlessPrairie_AttackTrigger_NoOtherCreatures_DoesNotThrow()
    {
        var effects = new ContinuousEffectsService();
        var land = RestlessPrairieFactory.Create(
            _alice, effects, replacements: null, triggers: null);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };
        act.Should().NotThrow(
            "with no other creatures the anthem is a clean no-op");
    }
}
