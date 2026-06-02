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
/// Tests for <see cref="FurycalmSnarlFactory"/> — the Strixhaven Snarl-cycle
/// reveal-a-Mountain-or-Plains dual land.
///
/// Oracle (verified against Scryfall):
/// "As this land enters, you may reveal a Mountain or Plains card from your
///  hand. If you don't, this land enters tapped.
///  {T}: Add {R} or {W}."
///
/// Covers:
/// - Identity (Land type, printed name, no printed subtype, non-Basic,
///   non-Legendary, owner/controller wiring).
/// - Two painless mana abilities producing {R} and {W} (CR 605.1a).
/// - No activated / triggered abilities beyond mana.
/// - ETB reveal-or-tapped replacement via
///   <see cref="ConditionalEntersTappedReplacement"/> (CR 614.1c):
///     - reveal path (Mountain OR Plains present, agent reveals) ⇒ untapped.
///     - decline path ⇒ tapped.
///     - no-match path ⇒ tapped, agent never prompted.
///     - no-agent ⇒ tapped (default decline posture).
/// - Args validation: null owner.
/// - Dispatch routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class FurycalmSnarlFactoryTests : IDisposable
{
    public FurycalmSnarlFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static Card MountainCard() =>
        new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });

    private static Card PlainsCard() =>
        new Land("Plains", subtypes: new[] { CardSubtype.Plains });

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FurycalmSnarl_IsLand_WithCorrectIdentity()
    {
        var alice = new Player("Alice", 20);

        var land = FurycalmSnarlFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Furycalm Snarl");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("it is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void FurycalmSnarl_HasTwoManaAbilities_ProducingRW()
    {
        var alice = new Player("Alice", 20);

        var land = FurycalmSnarlFactory.Create(alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(2, "Furycalm Snarl taps for {R} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Red == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void FurycalmSnarl_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = FurycalmSnarlFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Furycalm Snarl has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the reveal-or-tapped clause is a replacement (CR 614.1c), not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB reveal-a-Mountain-or-Plains replacement (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void FurycalmSnarl_EntersUntapped_WhenAgentRevealsMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(MountainCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = FurycalmSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Mountain lets the land enter untapped");
    }

    [Fact]
    public void FurycalmSnarl_EntersUntapped_WhenAgentRevealsPlains()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(PlainsCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = FurycalmSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Plains lets the land enter untapped");
    }

    [Fact]
    public void FurycalmSnarl_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(MountainCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = FurycalmSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "declining to reveal makes the land enter tapped");
    }

    [Fact]
    public void FurycalmSnarl_EntersTapped_WhenNoMountainOrPlainsInHand()
    {
        // Nothing to reveal ⇒ enters tapped. The agent is never prompted
        // (an empty ScriptedAgent queue would throw if it were).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(
            new Land("Forest", subtypes: new[] { CardSubtype.Forest }));
        var agent = new ScriptedAgent(); // no QueueYesNo — must not be asked
        AgentRegistry.Set(alice, agent);

        var land = FurycalmSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "with no Mountain or Plains card to reveal the land enters tapped");
    }

    [Fact]
    public void FurycalmSnarl_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(MountainCard());
        // intentionally no AgentRegistry.Set

        var land = FurycalmSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no registered agent ⇒ default decline ⇒ enters tapped");
    }

    [Fact]
    public void FurycalmSnarl_ShapeOnlyPath_NoReplacementBus_StillHasMana()
    {
        // Single-arg path: no ReplacementBus, so the reveal-or-tapped
        // replacement is omitted (shape-only posture, matches the reveal /
        // shock / check land cycles).
        var alice = new Player("Alice", 20);

        var land = FurycalmSnarlFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Args validation + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FurycalmSnarl_Create_ThrowsOnNullOwner()
    {
        var act = () => FurycalmSnarlFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FurycalmSnarl_DispatchByName_ResolvesFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Furycalm Snarl", alice);

        card.Should().BeOfType<Land>(
            "the [CardName] dispatch table resolves Furycalm Snarl to its factory, not a vanilla shell");
        card.Name.Should().Be("Furycalm Snarl");
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
