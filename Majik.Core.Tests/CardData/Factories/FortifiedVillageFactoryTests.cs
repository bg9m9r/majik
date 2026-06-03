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
/// Tests for <see cref="FortifiedVillageFactory"/> — the Shadows over Innistrad
/// reveal-a-Forest-or-Plains dual land (the {G}/{W} sibling of
/// <see cref="GameTrailFactory"/>).
///
/// Oracle (verified against Scryfall):
/// "As this land enters, you may reveal a Forest or Plains card from your hand.
///  If you don't, this land enters tapped.
///  {T}: Add {G} or {W}."
///
/// Covers:
/// - Identity (Land type, printed name, no printed subtype, non-Basic,
///   non-Legendary, owner/controller wiring).
/// - Two painless mana abilities producing {G} and {W} (CR 605.1a).
/// - No activated / triggered abilities beyond mana.
/// - ETB reveal-or-tapped replacement via
///   <see cref="ConditionalEntersTappedReplacement"/> (CR 614.1c):
///     - reveal path (Forest OR Plains present, agent reveals) ⇒ untapped.
///     - decline path ⇒ tapped.
///     - no-match path ⇒ tapped, agent never prompted.
///     - no-agent ⇒ tapped (default decline posture).
/// - Args validation: null owner.
/// - Dispatch routing through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class FortifiedVillageFactoryTests : IDisposable
{
    public FortifiedVillageFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static Card ForestCard() =>
        new Land("Forest", subtypes: new[] { CardSubtype.Forest });

    private static Card PlainsCard() =>
        new Land("Plains", subtypes: new[] { CardSubtype.Plains });

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FortifiedVillage_IsLand_WithCorrectIdentity()
    {
        var alice = new Player("Alice", 20);

        var land = FortifiedVillageFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Fortified Village");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("it is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void FortifiedVillage_HasTwoManaAbilities_ProducingGW()
    {
        var alice = new Player("Alice", 20);

        var land = FortifiedVillageFactory.Create(alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(2, "Fortified Village taps for {G} or {W}");
        mana.Should().Contain(m => m.ManaGenerated.Green == 1);
        mana.Should().Contain(m => m.ManaGenerated.White == 1);
    }

    [Fact]
    public void FortifiedVillage_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = FortifiedVillageFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Fortified Village has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the reveal-or-tapped clause is a replacement (CR 614.1c), not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB reveal-a-Forest-or-Plains replacement (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void FortifiedVillage_EntersUntapped_WhenAgentRevealsForest()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(ForestCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = FortifiedVillageFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Forest lets the land enter untapped");
    }

    [Fact]
    public void FortifiedVillage_EntersUntapped_WhenAgentRevealsPlains()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(PlainsCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = FortifiedVillageFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Plains lets the land enter untapped");
    }

    [Fact]
    public void FortifiedVillage_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(ForestCard());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = FortifiedVillageFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "declining to reveal makes the land enter tapped");
    }

    [Fact]
    public void FortifiedVillage_EntersTapped_WhenNoForestOrPlainsInHand()
    {
        // Nothing to reveal ⇒ enters tapped. The agent is never prompted
        // (an empty ScriptedAgent queue would throw if it were).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(
            new Land("Island", subtypes: new[] { CardSubtype.Island }));
        var agent = new ScriptedAgent(); // no QueueYesNo — must not be asked
        AgentRegistry.Set(alice, agent);

        var land = FortifiedVillageFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "with no Forest or Plains card to reveal the land enters tapped");
    }

    [Fact]
    public void FortifiedVillage_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(ForestCard());
        // intentionally no AgentRegistry.Set

        var land = FortifiedVillageFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no registered agent ⇒ default decline ⇒ enters tapped");
    }

    [Fact]
    public void FortifiedVillage_ShapeOnlyPath_NoReplacementBus_StillHasMana()
    {
        // Single-arg path: no ReplacementBus, so the reveal-or-tapped
        // replacement is omitted (shape-only posture, matches the reveal /
        // shock / check land cycles).
        var alice = new Player("Alice", 20);

        var land = FortifiedVillageFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Args validation + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FortifiedVillage_Create_ThrowsOnNullOwner()
    {
        var act = () => FortifiedVillageFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FortifiedVillage_DispatchByName_ResolvesFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Fortified Village", alice);

        card.Should().BeOfType<Land>(
            "the [CardName] dispatch table resolves Fortified Village to its factory, not a vanilla shell");
        card.Name.Should().Be("Fortified Village");
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
