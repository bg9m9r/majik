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
/// Tests for <see cref="MurmuringBoskFactory"/> — the Lorwyn reveal-a-Treefolk
/// painland.
///
/// Oracle (verified against Scryfall):
/// "({T}: Add {G}.)
///  As this land enters, you may reveal a Treefolk card from your hand. If you
///  don't, this land enters tapped.
///  {T}: Add {W} or {B}. This land deals 1 damage to you."
///
/// Covers:
/// - Identity (Land type, printed name, Forest subtype, non-Basic,
///   non-Legendary, owner/controller wiring).
/// - Three mana abilities: a painless {G} ({T}: Add {G}) plus a {W} and a
///   {B} pain mode ({T}: Add {W}/{B}, deals 1 damage to you).
/// - ETB reveal-or-tapped replacement via
///   <see cref="ConditionalEntersTappedReplacement"/> (CR 614.1c):
///     - reveal path: controller has a Treefolk in hand and the agent reveals
///       it ⇒ enters untapped.
///     - decline path: agent declines to reveal ⇒ enters tapped.
///     - no-Treefolk path: nothing to reveal ⇒ enters tapped, agent never
///       prompted.
///     - no-agent: enters tapped (default decline posture).
/// - Args validation: null owner.
/// </summary>
[Trait("Color", "C")]
public class MurmuringBoskFactoryTests : IDisposable
{
    public MurmuringBoskFactoryTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    private static Creature Treefolk(string name = "Treefolk Harbinger") =>
        new(name, "G", 0, 1, subtypes: new[] { CardSubtype.Treefolk });

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MurmuringBosk_IsLand_WithCorrectIdentity()
    {
        var alice = new Player("Alice", 20);

        var land = MurmuringBoskFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Murmuring Bosk");
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
        land.HasSubtype(CardSubtype.Forest).Should().BeTrue("Murmuring Bosk is a Forest");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("it is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void MurmuringBosk_HasThreeManaAbilities_GWB()
    {
        var alice = new Player("Alice", 20);

        var land = MurmuringBoskFactory.Create(alice);

        var mana = land.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(3, "painless {G} plus pain {W} and {B}");

        mana.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("G")));
        mana.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("W")));
        mana.Should().Contain(m => SameCost(m.ManaGenerated, ManaCost.Parse("B")));
    }

    [Fact]
    public void MurmuringBosk_PainColours_DealOneDamage_ButGreenDoesNot()
    {
        // The {W} and {B} modes deal 1 damage to you (CR 120.3 — damage to a
        // player reduces life). The {G} mode is painless.
        var alice = new Player("Alice", 20);
        var land = MurmuringBoskFactory.Create(alice);

        ActivatePainMode(land, ManaCost.Parse("W"));
        alice.LifeTotal.Should().Be(19, "the {W} mode deals 1 damage to you");

        ActivatePainMode(land, ManaCost.Parse("B"));
        alice.LifeTotal.Should().Be(18, "the {B} mode deals 1 damage to you");
    }

    [Fact]
    public void MurmuringBosk_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = MurmuringBoskFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Murmuring Bosk has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the reveal-or-tapped clause is a replacement (CR 614.1c), not a trigger");
    }

    // -----------------------------------------------------------------------
    // ETB reveal-a-Treefolk replacement (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void MurmuringBosk_EntersUntapped_WhenAgentRevealsTreefolk()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(Treefolk());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        AgentRegistry.Set(alice, agent);

        var land = MurmuringBoskFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "revealing a Treefolk lets the land enter untapped");
    }

    [Fact]
    public void MurmuringBosk_EntersTapped_WhenAgentDeclines()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(Treefolk());
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);
        AgentRegistry.Set(alice, agent);

        var land = MurmuringBoskFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "declining to reveal makes the land enter tapped");
    }

    [Fact]
    public void MurmuringBosk_EntersTapped_WhenNoTreefolkInHand()
    {
        // Nothing to reveal ⇒ enters tapped. The agent is never prompted
        // (an empty ScriptedAgent queue would throw if it were).
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(
            new Creature("Grizzly Bears", "1G", 2, 2, subtypes: new[] { CardSubtype.Bear }));
        var agent = new ScriptedAgent(); // no QueueYesNo — must not be asked
        AgentRegistry.Set(alice, agent);

        var land = MurmuringBoskFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "with no Treefolk to reveal the land enters tapped");
    }

    [Fact]
    public void MurmuringBosk_EntersTapped_WhenNoAgentRegistered()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        alice.Zones.Hand.AddCard(Treefolk());
        // intentionally no AgentRegistry.Set

        var land = MurmuringBoskFactory.Create(alice, replacements: bus);

        var after = bus.Apply(MoveIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "no registered agent ⇒ default decline ⇒ enters tapped");
    }

    [Fact]
    public void MurmuringBosk_ShapeOnlyPath_NoReplacementBus_EntersUntapped()
    {
        // Single-arg path: no ReplacementBus, so the reveal-or-tapped
        // replacement is omitted (shape-only posture, matches the
        // Shock / Check land cycles).
        var alice = new Player("Alice", 20);

        var land = MurmuringBoskFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(3);
    }

    // -----------------------------------------------------------------------
    // Args validation + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MurmuringBosk_Create_ThrowsOnNullOwner()
    {
        var act = () => MurmuringBoskFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MurmuringBosk_DispatchByName_ResolvesFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Murmuring Bosk", alice);

        card.Should().BeOfType<Land>(
            "the [CardName] dispatch table resolves Murmuring Bosk to its factory, not a vanilla shell");
        card.Name.Should().Be("Murmuring Bosk");
        card.HasSubtype(CardSubtype.Forest).Should().BeTrue();
        ((Land)card).Abilities.OfType<ManaAbility>().Should().HaveCount(3);
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

    private static void ActivatePainMode(Land land, ManaCost colour)
    {
        var ability = land.Abilities.OfType<ManaAbility>()
            .First(m => SameCost(m.ManaGenerated, colour));
        // Untap between activations so canActivateCheck (!IsTapped) passes.
        if (land.IsTapped) land.Untap();
        ability.Activate();
    }

    private static bool SameCost(ManaCost a, ManaCost b) =>
        a.White == b.White &&
        a.Blue == b.Blue &&
        a.Black == b.Black &&
        a.Red == b.Red &&
        a.Green == b.Green &&
        a.Generic == b.Generic;
}
