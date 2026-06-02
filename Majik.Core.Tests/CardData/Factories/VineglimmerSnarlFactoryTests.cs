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
/// Tests for <see cref="VineglimmerSnarlFactory"/> — the Strixhaven Snarl-cycle
/// reveal-a-Forest-or-Island dual land.
///
/// Oracle (verified against Scryfall):
/// "As this land enters, you may reveal a Forest or Island card from your
///  hand. If you don't, this land enters tapped.
///  {T}: Add {G} or {U}."
///
/// Covers:
/// - Identity (Land type, printed name, no printed subtype, non-Basic,
///   non-Legendary, owner/controller wiring).
/// - Two painless mana abilities producing {G} and {U} (CR 605.1a).
/// - No activated / triggered abilities beyond mana.
/// - ETB reveal-or-tapped replacement via
///   <see cref="ConditionalEntersTappedReplacement"/> (CR 614.1c):
///     - reveal path (Forest OR Island present, agent reveals) ⇒ untapped.
///     - decline path ⇒ tapped.
///     - no-match path ⇒ tapped, agent never prompted.
///     - no-agent ⇒ tapped (default decline posture).
/// - Args validation: null owner.
/// - Dispatch routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class VineglimmerSnarlFactoryTests : IDisposable
{
    public VineglimmerSnarlFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static Card ForestCard() =>
        new Land("Forest", subtypes: new[] { CardSubtype.Forest });

    private static Card IslandCard() =>
        new Land("Island", subtypes: new[] { CardSubtype.Island });

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void VineglimmerSnarl_IsLand_WithCorrectIdentity()
    {
        var alice = new Player("Alice", 20);

        var land = VineglimmerSnarlFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Vineglimmer Snarl");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("it is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void VineglimmerSnarl_HasTwoManaAbilities_ProducingGU()
    {
        var alice = new Player("Alice", 20);

        var land = VineglimmerSnarlFactory.Create(alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(2, "Vineglimmer Snarl taps for {G} or {U}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
    }

    [Fact]
    public void VineglimmerSnarl_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = VineglimmerSnarlFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Vineglimmer Snarl has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the reveal-or-tapped clause is a replacement (CR 614.1c), not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB reveal-a-Forest-or-Island replacement (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void VineglimmerSnarl_EntersUntapped_WhenAgentRevealsForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(ForestCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = VineglimmerSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Forest lets the land enter untapped");
    }

    [Fact]
    public void VineglimmerSnarl_EntersUntapped_WhenAgentRevealsIsland()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(IslandCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = VineglimmerSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing an Island lets the land enter untapped");
    }

    [Fact]
    public void VineglimmerSnarl_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(ForestCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = VineglimmerSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "declining to reveal makes the land enter tapped");
    }

    [Fact]
    public void VineglimmerSnarl_EntersTapped_WhenNoForestOrIslandInHand()
    {
        // Nothing to reveal ⇒ enters tapped. The agent is never prompted
        // (an empty ScriptedAgent queue would throw if it were).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(
            new Land("Mountain", subtypes: new[] { CardSubtype.Mountain }));
        var agent = new ScriptedAgent(); // no QueueYesNo — must not be asked
        AgentRegistry.Set(alice, agent);

        var land = VineglimmerSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "with no Forest or Island card to reveal the land enters tapped");
    }

    [Fact]
    public void VineglimmerSnarl_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(ForestCard());
        // intentionally no AgentRegistry.Set

        var land = VineglimmerSnarlFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no registered agent ⇒ default decline ⇒ enters tapped");
    }

    [Fact]
    public void VineglimmerSnarl_ShapeOnlyPath_NoReplacementBus_StillHasMana()
    {
        // Single-arg path: no ReplacementBus, so the reveal-or-tapped
        // replacement is omitted (shape-only posture, matches the reveal /
        // shock / check land cycles).
        var alice = new Player("Alice", 20);

        var land = VineglimmerSnarlFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Args validation + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void VineglimmerSnarl_Create_ThrowsOnNullOwner()
    {
        var act = () => VineglimmerSnarlFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void VineglimmerSnarl_DispatchByName_ResolvesFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Vineglimmer Snarl", alice);

        card.Should().BeOfType<Land>(
            "the [CardName] dispatch table resolves Vineglimmer Snarl to its factory, not a vanilla shell");
        card.Name.Should().Be("Vineglimmer Snarl");
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
