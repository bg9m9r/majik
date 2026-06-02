using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="GameTrailFactory"/> — the Shadows over Innistrad
/// reveal-a-Mountain-or-Forest dual land.
///
/// Oracle (verified against Scryfall):
/// "As this land enters, you may reveal a Mountain or Forest card from your hand.
///  If you don't, this land enters tapped.
///  {T}: Add {R} or {G}."
///
/// Covers:
/// - Identity (Land type, printed name, no printed subtype, non-Basic,
///   non-Legendary, owner/controller wiring).
/// - Two painless mana abilities producing {R} and {G} (CR 605.1a).
/// - No activated / triggered abilities beyond mana.
/// - ETB reveal-or-tapped replacement via
///   <see cref="ConditionalEntersTappedReplacement"/> (CR 614.1c):
///     - reveal path (Mountain OR Forest present, agent reveals) ⇒ untapped.
///     - decline path ⇒ tapped.
///     - no-match path ⇒ tapped, agent never prompted.
///     - no-agent ⇒ tapped (default decline posture).
/// - Args validation: null owner.
/// - Dispatch routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class GameTrailFactoryTests : IDisposable
{
    public GameTrailFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static Card MountainCard() =>
        new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });

    private static Card ForestCard() =>
        new Land("Forest", subtypes: new[] { CardSubtype.Forest });

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GameTrail_IsLand_WithCorrectIdentity()
    {
        var alice = new Player("Alice", 20);

        var land = GameTrailFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Game Trail");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("it is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void GameTrail_HasTwoManaAbilities_ProducingRG()
    {
        var alice = new Player("Alice", 20);

        var land = GameTrailFactory.Create(alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(2, "Game Trail taps for {R} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void GameTrail_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = GameTrailFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Game Trail has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the reveal-or-tapped clause is a replacement (CR 614.1c), not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB reveal-a-Mountain-or-Forest replacement (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void GameTrail_EntersUntapped_WhenAgentRevealsMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(MountainCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = GameTrailFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Mountain lets the land enter untapped");
    }

    [Fact]
    public void GameTrail_EntersUntapped_WhenAgentRevealsForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(ForestCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = GameTrailFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Forest lets the land enter untapped");
    }

    [Fact]
    public void GameTrail_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(MountainCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = GameTrailFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "declining to reveal makes the land enter tapped");
    }

    [Fact]
    public void GameTrail_EntersTapped_WhenNoMountainOrForestInHand()
    {
        // Nothing to reveal ⇒ enters tapped. The agent is never prompted
        // (an empty ScriptedAgent queue would throw if it were).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(
            new Land("Island", subtypes: new[] { CardSubtype.Island }));
        var agent = new ScriptedAgent(); // no QueueYesNo — must not be asked
        AgentRegistry.Set(alice, agent);

        var land = GameTrailFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "with no Mountain or Forest card to reveal the land enters tapped");
    }

    [Fact]
    public void GameTrail_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(MountainCard());
        // intentionally no AgentRegistry.Set

        var land = GameTrailFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no registered agent ⇒ default decline ⇒ enters tapped");
    }

    [Fact]
    public void GameTrail_ShapeOnlyPath_NoReplacementBus_StillHasMana()
    {
        // Single-arg path: no ReplacementBus, so the reveal-or-tapped
        // replacement is omitted (shape-only posture, matches the reveal /
        // shock / check land cycles).
        var alice = new Player("Alice", 20);

        var land = GameTrailFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Args validation + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GameTrail_Create_ThrowsOnNullOwner()
    {
        var act = () => GameTrailFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GameTrail_DispatchByName_ResolvesFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Game Trail", alice);

        card.Should().BeOfType<Land>(
            "the [CardName] dispatch table resolves Game Trail to its factory, not a vanilla shell");
        card.Name.Should().Be("Game Trail");
        ((Land)card).Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ZoneMoveIntent MoveIntent(Land land, Player controller) =>
        new(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: controller);
}
