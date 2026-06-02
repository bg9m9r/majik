using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AetherChannelerFactory"/>.
///
/// Covers:
/// - Identity ({2}{U} Creature — Human Wizard, 2/1, blue, mana value 3).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one battlefield-active ETB triggered ability.
/// - Mode 0 (token): a 1/1 white Bird with flying enters under the controller.
/// - Mode 1 (bounce): another nonland permanent returns to its owner's hand;
///   a land target is rejected ("nonland"); the source itself is rejected
///   ("another").
/// - Mode 2 (draw): controller draws the top card of their library.
/// </summary>
[Trait("Color", "U")]
public class AetherChannelerFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherChanneler_Identity()
    {
        var c = AetherChannelerFactory.Create(_alice);

        c.Name.Should().Be("Aether Channeler");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Aether Channeler is a Human");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Aether Channeler is a Wizard");
        c.ManaCost.Should().Be("{2}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AetherChanneler_IsBlue()
    {
        var c = AetherChannelerFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue,
            "Aether Channeler has a {U} pip in its mana cost");
        colors.Should().HaveCount(1, "only one color");
    }

    [Fact]
    public void AetherChanneler_ManaValue_IsThree()
    {
        var c = AetherChannelerFactory.Create(_alice);

        // {2}{U} = mana value 3 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(3, "CR 202.3 — {2}{U} has mana value 3");
    }

    [Fact]
    public void AetherChanneler_NamedFactory_Dispatches()
    {
        ImplementedCardNames.Contains("Aether Channeler").Should().BeTrue(
            "the [CardName] factory registers Aether Channeler as implemented");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherChanneler_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = AetherChannelerFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB modal trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — Create a 1/1 white Bird with flying
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherChanneler_Mode0_CreatesWhiteFlyingBirdToken()
    {
        var alice = new Player("Alice", 20);
        var channeler = AetherChannelerFactory.Create(alice, mode: 0);

        var etb = channeler.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var bird = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Bird");

        bird.Should().NotBeNull("mode 0 creates a Bird token");
        bird!.BasePower.Should().Be(1);
        bird.BaseToughness.Should().Be(1);
        bird.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        bird.IsToken.Should().BeTrue("CR 111 — a created token");
        bird.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "the Bird token has flying (CR 702.9)");
        CardColors.GetColors(bird).Should().Contain(ManaColor.White,
            "CR 111.4 — the Bird token is white");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — Return another target nonland permanent to its owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherChanneler_Mode1_BouncesNonlandPermanent_ToOwnersHand()
    {
        var channeler = AetherChannelerFactory.Create(_alice, mode: 1);

        // Bob's creature — a legal "another nonland permanent" target.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var etb = channeler.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        grizzly.Zone.Should().Be(ZoneType.Hand,
            "mode 1 returns the target nonland permanent to its owner's hand (CR 701.20)");
        _bob.Zones.Hand.GetCards().Should().Contain(grizzly,
            "the bounced permanent lands in its OWNER's hand");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(grizzly);
    }

    [Fact]
    public void AetherChanneler_Mode1_LandTarget_IsNotBounced()
    {
        // "nonland permanent" — a land target is illegal (CR 608.2b re-check).
        var channeler = AetherChannelerFactory.Create(_alice, mode: 1);

        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_bob);
        island.SetController(_bob);
        island.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(island);

        var etb = channeler.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { island },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        island.Zone.Should().Be(ZoneType.Battlefield,
            "a land is not a legal 'nonland permanent' target — no-op (CR 608.2b)");
        _bob.Zones.Hand.GetCards().Should().NotContain(island);
    }

    [Fact]
    public void AetherChanneler_Mode1_SelfTarget_IsNotBounced()
    {
        // "another" — Aether Channeler cannot bounce itself.
        var channeler = AetherChannelerFactory.Create(_alice, mode: 1);
        channeler.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(channeler);

        var etb = channeler.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { channeler },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        channeler.Zone.Should().Be(ZoneType.Battlefield,
            "'another' excludes the source itself — no-op (CR 115.5b)");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — Draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherChanneler_Mode2_DrawsTopCard()
    {
        var alice = new Player("Alice", 20);

        var topCard = new Creature("CardA", "{W}", 1, 1);
        topCard.SetOwner(alice);
        alice.Zones.Library.AddCard(topCard);

        var channeler = AetherChannelerFactory.Create(alice, mode: 2);

        var etb = channeler.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "mode 2 draws the top card of the library (CR 121.1)");
        alice.Zones.Library.GetCards().Should().NotContain(topCard);
    }

    // -----------------------------------------------------------------------
    // Wired path: bus event triggers ETB
    // -----------------------------------------------------------------------

    [Fact]
    public void AetherChanneler_WiredCreate_Mode2_EnteringBattlefield_DrawsACard()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var topCard = new Creature("CardA", "{W}", 1, 1);
        topCard.SetOwner(alice);
        alice.Zones.Library.AddCard(topCard);

        var channeler = AetherChannelerFactory.Create(alice, mode: 2, triggers: triggerManager);
        channeler.SetZone(ZoneType.Battlefield);

        var moveEvent = new CardMovedEvent(channeler, ZoneType.Hand, ZoneType.Battlefield);
        bus.Publish(moveEvent);

        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            item?.Resolve();
        }

        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "entering the battlefield via the bus with mode 2 draws a card end-to-end");
    }
}
