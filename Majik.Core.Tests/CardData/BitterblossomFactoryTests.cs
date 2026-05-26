using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Bitterblossom (Morningtide, {1}{B}, Tribal Enchantment — Faerie).
///
/// Coverage:
/// - Identity (name / types / mana cost / Faerie subtype + Tribal type).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Upkeep trigger shape — single TriggeredAbility filtered to controller's
///   own Upkeep step.
/// - Trigger resolution: controller loses 1 life and gains a 1/1 black
///   Faerie Rogue token with Flying.
/// - Opponent upkeep does NOT fire the trigger (controller-scoped).
/// </summary>
public class BitterblossomFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Bitterblossom_Identity()
    {
        var c = BitterblossomFactory.Create(_alice);

        c.Name.Should().Be("Bitterblossom");
        c.ManaCost.Should().Be("{1}{B}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasType(CardType.Tribal).Should().BeTrue(
            "Bitterblossom is printed as 'Tribal Enchantment — Faerie' (Morningtide)");
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // Exactly one triggered ability (the upkeep token + life loss).
        c.Abilities.OfType<TriggeredAbility>().Should().ContainSingle();
    }

    [Fact]
    public void Bitterblossom_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Bitterblossom", _alice);

        c.Should().BeOfType<Enchantment>();
        c.Name.Should().Be("Bitterblossom");
        c.HasType(CardType.Tribal).Should().BeTrue();
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Upkeep trigger resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Bitterblossom_OnUpkeep_LosesLifeAndCreatesFaerieToken()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var card = BitterblossomFactory.Create(_alice, bus, triggers: null, zones);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var preLife = _alice.LifeTotal;

        // Fire the trigger body directly (shape path — no TriggerManager).
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        // 1 life lost.
        _alice.LifeTotal.Should().Be(preLife - 1,
            "Bitterblossom's upkeep trigger loses 1 life (CR 119.6)");

        // 1 Faerie Rogue token, 1/1, Flying, black.
        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => !ReferenceEquals(c, card))
            .ToList();
        tokens.Should().ContainSingle("Bitterblossom creates one token per upkeep");

        var token = tokens.Single();
        token.Name.Should().Be("Faerie Rogue");
        token.BasePower.Should().Be(BitterblossomFactory.TokenPower);
        token.BaseToughness.Should().Be(BitterblossomFactory.TokenToughness);
        token.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        token.HasSubtype(CardSubtype.Rogue).Should().BeTrue();
        token.IsToken.Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "the token has flying");

        // Colour stamped via TokenSpec.Colors — black (CR 105 / 111.4).
        token.TokenColorsOverride.Should().NotBeNull();
        token.TokenColorsOverride!.Should().Contain(ManaColor.Black);
    }

    // -----------------------------------------------------------------------
    // Trigger condition — controller-scoped
    // -----------------------------------------------------------------------

    [Fact]
    public void Bitterblossom_OnlyFiresOnControllersUpkeep()
    {
        var card = BitterblossomFactory.Create(_alice);
        // Trigger only checks ActiveZones — put Bitterblossom on the
        // battlefield so the zone gate passes and we evaluate the
        // step-condition.
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var aliceUpkeep = new StepStartedEvent(PhaseStateType.Upkeep, _alice);
        var bobUpkeep = new StepStartedEvent(PhaseStateType.Upkeep, _bob);
        var aliceDraw = new StepStartedEvent(PhaseStateType.Draw, _alice);

        trigger.IsTriggered(aliceUpkeep).Should().BeTrue(
            "fires on the controller's own Upkeep");
        trigger.IsTriggered(bobUpkeep).Should().BeFalse(
            "does NOT fire on the opponent's upkeep — 'your upkeep'");
        trigger.IsTriggered(aliceDraw).Should().BeFalse(
            "only Upkeep step triggers — not Draw step");
    }

    [Fact]
    public void Bitterblossom_TriggerActiveOnBattlefield()
    {
        var card = BitterblossomFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().BeEquivalentTo(new[] { ZoneType.Battlefield },
            "the trigger only functions while Bitterblossom is on the battlefield (CR 603.6)");
    }
}
