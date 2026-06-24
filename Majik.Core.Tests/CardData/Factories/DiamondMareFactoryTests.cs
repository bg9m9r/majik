using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Diamond Mare (Core Set 2019, {2}, Artifact Creature — Horse 1/3).
///
///   "As this creature enters, choose a color.
///    Whenever you cast a spell of the chosen color, you gain 1 life."
///
/// Covers the card's UNIQUE behaviour (the colourless contract-test +
/// dispatch/well-formedness checks are owned by CardFactoryContractTests):
///   - Identity (Artifact Creature — Horse, {2}, 1/3, colourless).
///   - The choose-a-color holder is stashed so the ETB overlay can prompt.
///   - Casting a spell of the chosen colour → gain 1 life.
///   - Casting a spell of a DIFFERENT colour → no life gain.
///   - An OPPONENT casting a spell of the chosen colour → no life gain.
///   - The chosen colour is read LIVE (the agent's ETB pick wins over the seed).
/// </summary>
[Trait("Color", "C")]
public class DiamondMareFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewColoredSpell(Player controller, string manaCost)
    {
        var instant = new Instant("Test Bolt", manaCost) { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    [Fact]
    public void DiamondMare_Identity_ArtifactCreatureHorse1_3()
    {
        var mare = DiamondMareFactory.Create(_alice);

        mare.Name.Should().Be("Diamond Mare");
        mare.HasType(CardType.Artifact).Should().BeTrue();
        mare.HasType(CardType.Creature).Should().BeTrue();
        mare.HasSubtype(CardSubtype.Horse).Should().BeTrue();
        mare.ManaCost.Should().Be("{2}");
        mare.BasePower.Should().Be(1);
        mare.BaseToughness.Should().Be(3);
        // {2} cost, no color indicator → colourless (CR 105.2c).
        CardColors.GetColors(mare).Should().BeEmpty();
        mare.Owner.Should().BeSameAs(_alice);
        mare.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DiamondMare_StashesColorChoiceHolder_ForEtbOverlay()
    {
        var mare = DiamondMareFactory.Create(_alice);

        // CR 614.12 — the choose-a-color holder must be discoverable by
        // ChooseColorPermanentBinder so the routed build can prompt the agent.
        ColorChoiceRegistry.Get(mare).Should().NotBeNull();
    }

    [Fact]
    public void CastChosenColorSpell_GainsOneLife()
    {
        var (stack, triggers) = BuildEngine(out var bus);

        var mare = DiamondMareFactory.Create(_alice, ManaColor.Red, triggers);
        _alice.Zones.Battlefield.AddCard(mare);
        mare.SetZone(ZoneType.Battlefield);

        var startLife = _alice.LifeTotal;

        // Alice casts a red spell — matches the chosen colour.
        bus.Publish(new SpellCastEvent(NewColoredSpell(_alice, "R")));

        triggers.PendingCount.Should().Be(1,
            "casting a spell of the chosen color fires Diamond Mare (CR 603.1)");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(startLife + 1, "Diamond Mare gains 1 life");
    }

    [Fact]
    public void CastWrongColorSpell_DoesNotGainLife()
    {
        var (stack, triggers) = BuildEngine(out var bus);

        var mare = DiamondMareFactory.Create(_alice, ManaColor.Red, triggers);
        _alice.Zones.Battlefield.AddCard(mare);
        mare.SetZone(ZoneType.Battlefield);

        var startLife = _alice.LifeTotal;

        // Alice casts a blue spell — chosen colour is red.
        bus.Publish(new SpellCastEvent(NewColoredSpell(_alice, "U")));

        triggers.PendingCount.Should().Be(0,
            "a spell of a different colour does not match the chosen colour");
        _alice.LifeTotal.Should().Be(startLife);
    }

    [Fact]
    public void OpponentCastsChosenColorSpell_DoesNotGainLife()
    {
        var (stack, triggers) = BuildEngine(out var bus);

        var mare = DiamondMareFactory.Create(_alice, ManaColor.Red, triggers);
        _alice.Zones.Battlefield.AddCard(mare);
        mare.SetZone(ZoneType.Battlefield);

        var startLife = _alice.LifeTotal;

        // Bob casts a red spell — chosen colour matches but "you cast" doesn't.
        bus.Publish(new SpellCastEvent(NewColoredSpell(_bob, "R")));

        triggers.PendingCount.Should().Be(0,
            "'whenever YOU cast' restricts the trigger to Diamond Mare's controller");
        _alice.LifeTotal.Should().Be(startLife);
    }

    /// <summary>
    /// CR 614.12 — the chosen colour is read LIVE. Re-stamping the holder (the
    /// agent's "as this enters" pick) keys the trigger off the new colour, not
    /// the seed supplied at construction.
    /// </summary>
    [Fact]
    public void ChosenColorIsReadLive_AfterEtbStamp()
    {
        var (stack, triggers) = BuildEngine(out var bus);

        // Seed Red, then the ETB overlay stamps Green (the agent's real pick).
        var mare = DiamondMareFactory.Create(_alice, ManaColor.Red, triggers);
        ColorChoiceRegistry.Get(mare)!.Choose(ManaColor.Green);
        _alice.Zones.Battlefield.AddCard(mare);
        mare.SetZone(ZoneType.Battlefield);

        var startLife = _alice.LifeTotal;

        // A red spell no longer matches (chosen colour is now Green).
        bus.Publish(new SpellCastEvent(NewColoredSpell(_alice, "R")));
        triggers.PendingCount.Should().Be(0, "the live chosen colour is Green, not the Red seed");

        // A green spell matches.
        bus.Publish(new SpellCastEvent(NewColoredSpell(_alice, "G")));
        triggers.PendingCount.Should().Be(1, "a green spell matches the live chosen colour");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(startLife + 1);
    }

    private static (Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine(out EventBus bus)
    {
        bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (stack, triggers);
    }
}
