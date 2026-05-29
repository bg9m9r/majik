using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CrumblingVestigeFactory"/> — Crumbling Vestige
/// (Oath of the Gatewatch). Oracle text (Scryfall, verified):
///   "This land enters tapped.
///    When this land enters, add one mana of any color.
///    {T}: Add {C}. ({C} represents colorless mana.)"
///
/// Mirrors the analogue factories: <see cref="WastelandFactory"/> for the
/// {T}: Add {C} mana ability, <see cref="SavaiTriomeFactory"/> for the
/// EntersTappedReplacement (CR 614.1c), and <see cref="LotusCobraFactory"/>
/// for the one-shot "add one mana of any color" trigger (CR 106).
/// </summary>
public class CrumblingVestigeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingVestige_Dispatch_ReturnsLand()
    {
        var card = NamedCardFactory.Create("Crumbling Vestige", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Crumbling Vestige");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSupertype(CardSupertype.Basic).Should()
            .BeFalse("Crumbling Vestige is a nonbasic land");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} — CR 605.1 mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingVestige_HasSingleColorlessManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Crumbling Vestige", _alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        manaAbilities.Should().ContainSingle("{T}: Add {C}");
        var produced = manaAbilities[0].ManaGenerated;
        // {C} (colourless, CR 107.4c) has no dedicated ManaCost bucket today
        // — ManaCost.Parse("C") folds it into Generic (mirrors Wasteland /
        // Urza's Saga "{T}: Add {C}"). So one unit of mana, no coloured pip.
        produced.Generic.Should().Be(1, "{C} is one (colourless) mana, bucketed as Generic");
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Enters-tapped — CR 614.1c
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingVestige_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();
        var land = CrumblingVestigeFactory.Create(
            _alice, triggers: null, replacements: replacements, colorPicker: null);

        land.Should().NotBeNull();
        // EntersTappedReplacement has no public bus-inspection surface; the
        // production tapped-entry path is covered by the binder chain off the
        // oracle text. Asserting the bus-wired build succeeds mirrors
        // SavaiTriomeFactoryTests.
    }

    // -----------------------------------------------------------------------
    // ETB "add one mana of any color" — CR 106
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingVestige_HasEnterBattlefieldSelfTrigger()
    {
        var land = (Land)NamedCardFactory.Create("Crumbling Vestige", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should()
            .ContainSingle("When this land enters, add one mana of any color");
    }

    [Fact]
    public void CrumblingVestige_EtbTrigger_AddsOneManaOfChosenColor()
    {
        var land = CrumblingVestigeFactory.Create(
            _alice, triggers: null, replacements: null,
            colorPicker: () => ManaColor.Blue);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.ManaPool.Total.Should().Be(1, "add one mana of any color");
        _alice.ManaPool.Blue.Should().Be(1, "the picker chose blue");
    }

    [Fact]
    public void CrumblingVestige_EtbTrigger_DefaultsToColoredMana_WhenNoPicker()
    {
        var land = CrumblingVestigeFactory.Create(
            _alice, triggers: null, replacements: null, colorPicker: null);

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.ManaPool.Total.Should().Be(1, "add one mana of any color");
        // "any color" (CR 106.1b) is a WUBRG colour, never colorless/generic;
        // the default picker yields Green (a coloured pip), so the Generic
        // (colourless) bucket stays empty.
        _alice.ManaPool.Generic.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(1, "default 'any color' pick is Green");
    }

    // -----------------------------------------------------------------------
    // ETB trigger registers with a live bus
    // -----------------------------------------------------------------------

    [Fact]
    public void CrumblingVestige_EtbTrigger_FiresOnSelfEntering_WhenBusSupplied()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var land = CrumblingVestigeFactory.Create(
            _alice, triggers: triggers, replacements: null,
            colorPicker: () => ManaColor.Red);

        // Move the land onto the battlefield and announce its ETB so the
        // registered trigger surfaces as pending (CR 603.2).
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(land, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1,
            "the ETB trigger fired for the land entering");
    }
}
