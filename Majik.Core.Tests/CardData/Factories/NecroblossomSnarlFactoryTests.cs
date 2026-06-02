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
/// Tests for <see cref="NecroblossomSnarlFactory"/> — the Strixhaven Snarl-cycle
/// reveal-a-Swamp-or-Forest dual land.
///
/// Oracle (verified against Scryfall):
/// "As this land enters, you may reveal a Swamp or Forest card from your
///  hand. If you don't, this land enters tapped.
///  {T}: Add {B} or {G}."
///
/// Covers:
/// - Identity (Land type, printed name, no printed subtype, non-Basic,
///   non-Legendary, owner/controller wiring).
/// - Two painless mana abilities producing {B} and {G} (CR 605.1a).
/// - No activated / triggered abilities beyond mana.
/// - ETB reveal-or-tapped replacement via
///   <see cref="ConditionalEntersTappedReplacement"/> (CR 614.1c):
///     - reveal path (Swamp OR Forest present, agent reveals) ⇒ untapped.
///     - decline path ⇒ tapped.
///     - no-match path ⇒ tapped, agent never prompted.
///     - no-agent ⇒ tapped (default decline posture).
/// - Args validation: null owner.
/// - Dispatch routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class NecroblossomSnarlFactoryTests : IDisposable
{
    public NecroblossomSnarlFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static Card SwampCard() =>
        new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });

    private static Card ForestCard() =>
        new Land("Forest", subtypes: new[] { CardSubtype.Forest });

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void NecroblossomSnarl_IsLand_WithCorrectIdentity()
    {
        var alice = new Player("Alice", 20);

        var land = NecroblossomSnarlFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Necroblossom Snarl");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("it is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void NecroblossomSnarl_HasTwoManaAbilities_ProducingBG()
    {
        var alice = new Player("Alice", 20);

        var land = NecroblossomSnarlFactory.Create(alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(2, "Necroblossom Snarl taps for {B} or {G}");
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void NecroblossomSnarl_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = NecroblossomSnarlFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Necroblossom Snarl has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the reveal-or-tapped clause is a replacement (CR 614.1c), not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB reveal-a-Swamp-or-Forest replacement (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void NecroblossomSnarl_EntersUntapped_WhenAgentRevealsSwamp()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(SwampCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = NecroblossomSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Swamp lets the land enter untapped");
    }

    [Fact]
    public void NecroblossomSnarl_EntersUntapped_WhenAgentRevealsForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(ForestCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = NecroblossomSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Forest lets the land enter untapped");
    }

    [Fact]
    public void NecroblossomSnarl_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(SwampCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = NecroblossomSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "declining to reveal makes the land enter tapped");
    }

    [Fact]
    public void NecroblossomSnarl_EntersTapped_WhenNoSwampOrForestInHand()
    {
        // Nothing to reveal ⇒ enters tapped. The agent is never prompted
        // (an empty ScriptedAgent queue would throw if it were).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(
            new Land("Island", subtypes: new[] { CardSubtype.Island }));
        var agent = new ScriptedAgent(); // no QueueYesNo — must not be asked
        AgentRegistry.Set(alice, agent);

        var land = NecroblossomSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "with no Swamp or Forest card to reveal the land enters tapped");
    }

    [Fact]
    public void NecroblossomSnarl_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(SwampCard());
        // intentionally no AgentRegistry.Set

        var land = NecroblossomSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no registered agent ⇒ default decline ⇒ enters tapped");
    }

    [Fact]
    public void NecroblossomSnarl_ShapeOnlyPath_NoReplacementBus_StillHasMana()
    {
        // Single-arg path: no ReplacementBus, so the reveal-or-tapped
        // replacement is omitted (shape-only posture, matches the reveal /
        // shock / check land cycles).
        var alice = new Player("Alice", 20);

        var land = NecroblossomSnarlFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Args validation + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NecroblossomSnarl_Create_ThrowsOnNullOwner()
    {
        var act = () => NecroblossomSnarlFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void NecroblossomSnarl_DispatchByName_ResolvesFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Necroblossom Snarl", alice);

        card.Should().BeOfType<Land>(
            "the [CardName] dispatch table resolves Necroblossom Snarl to its factory, not a vanilla shell");
        card.Name.Should().Be("Necroblossom Snarl");
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
