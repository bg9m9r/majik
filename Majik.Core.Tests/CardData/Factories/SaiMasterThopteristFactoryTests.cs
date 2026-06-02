using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Sai, Master Thopterist (Aether Revolt, {1}{U}).
///
/// Covers:
///   - Card identity (1/4 Legendary Human Artificer, {1}{U}).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Artifact-cast trigger fires on the controller's artifact spells
///     (creates a 1/1 colourless Thopter token with Flying + Artifact),
///     and ignores noncreature non-artifact spells / opponent casts.
///   - {2}, Sacrifice two artifacts: Draw a card — cost shape +
///     resolution.
/// </summary>
[Trait("Color", "U")]
public class SaiMasterThopteristFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewArtifactSpell(Player controller, string name = "Mox Opal")
    {
        var artifact = new Artifact(name, "{0}") { Owner = controller };
        return new Majik.Core.Spells.Spell(artifact, controller);
    }

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewArtifactCreatureSpell(
        Player controller, string name = "Ornithopter")
    {
        var creature = new Creature(name, "{0}", 0, 2) { Owner = controller };
        creature.AddCardType(CardType.Artifact);
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Sai_Identity_LegendaryHumanArtificer_1_4_At1U()
    {
        var sai = SaiMasterThopteristFactory.Create(_alice);

        sai.Name.Should().Be("Sai, Master Thopterist");
        sai.ManaCost.Should().Be("{1}{U}");
        sai.HasType(CardType.Creature).Should().BeTrue();
        sai.HasSubtype(CardSubtype.Human).Should().BeTrue();
        sai.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        sai.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        sai.BasePower.Should().Be(1);
        sai.BaseToughness.Should().Be(4);
        sai.Owner.Should().BeSameAs(_alice);
        sai.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Sai_HasOneCastTrigger_AndOneActivatedAbility()
    {
        var sai = SaiMasterThopteristFactory.Create(_alice);

        sai.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the artifact-cast → Thopter trigger");
        sai.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {2}, Sacrifice two artifacts: Draw a card activation");
    }

    [Fact]
    public void Sai_ActivatedAbility_CostShape_ManaPlusSacTwoArtifacts()
    {
        var sai = SaiMasterThopteristFactory.Create(_alice);
        var activated = sai.Abilities.OfType<ActivatedAbility>().Single();

        // {2} mana cost
        activated.Costs.OfType<ManaCostCost>().Should().HaveCount(1);

        // Sacrifice two artifacts
        var sac = activated.Costs.OfType<SacrificeTwoArtifactsCost>().Single();
        sac.Description.Should().Contain("two artifacts");
    }

    // -----------------------------------------------------------------------
    // Cast trigger — fires on the controller's artifact spell
    // -----------------------------------------------------------------------

    [Fact]
    public void ArtifactCast_ByController_FiresTrigger_AndMintsThopter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sai = SaiMasterThopteristFactory.Create(_alice, bus, triggers, zoneService: null);
        sai.SetZone(ZoneType.Battlefield);

        var before = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Count(c => c.Name == "Thopter");

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Mox Opal")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var thopter = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.Name == "Thopter");

        thopter.IsToken.Should().BeTrue("CR 111.1 — minted as a token");
        thopter.BasePower.Should().Be(1);
        thopter.BaseToughness.Should().Be(1);
        thopter.HasSubtype(CardSubtype.Thopter).Should().BeTrue();
        thopter.HasType(CardType.Creature).Should().BeTrue();
        thopter.HasType(CardType.Artifact).Should().BeTrue(
            "Thopter token is an Artifact Creature (CR 111.1)");
        thopter.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "the printed Thopter token has flying");
    }

    [Fact]
    public void ArtifactCreatureCast_ByController_AlsoFiresTrigger()
    {
        // CR 301.1 — Artifact Creatures satisfy the "artifact spell"
        // predicate. Ornithopter cast → Sai mints a Thopter.
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sai = SaiMasterThopteristFactory.Create(_alice, bus, triggers, zoneService: null);
        sai.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactCreatureSpell(_alice, "Ornithopter")));

        triggers.PendingCount.Should().Be(1,
            "artifact creature spells satisfy the artifact-spell predicate");
    }

    [Fact]
    public void NonArtifactCast_ByController_DoesNotFireTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sai = SaiMasterThopteristFactory.Create(_alice, bus, triggers, zoneService: null);
        sai.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(0,
            "Lightning Bolt is not an artifact spell — Sai's trigger does not fire");
    }

    [Fact]
    public void OpponentArtifactCast_DoesNotFireTrigger()
    {
        // "Whenever YOU cast an artifact spell" — opponent casts are
        // ignored (CR 109.5).
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var sai = SaiMasterThopteristFactory.Create(_alice, bus, triggers, zoneService: null);
        sai.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_bob, "Bob's Mox")));

        triggers.PendingCount.Should().Be(0,
            "Bob's artifact spell is not Alice's — Sai's trigger does not fire");
    }

    // -----------------------------------------------------------------------
    // Sacrifice two artifacts cost — pay / can-pay shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SacrificeTwoArtifactsCost_CanPay_WithTwoArtifacts()
    {
        var alice = new Player("Alice", 20);
        var a1 = new Artifact("Mox A", "{0}") { Owner = alice };
        var a2 = new Artifact("Mox B", "{0}") { Owner = alice };
        a1.SetZone(ZoneType.Battlefield);
        a2.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(a1);
        alice.Zones.Battlefield.AddCard(a2);

        var cost = new SacrificeTwoArtifactsCost();
        cost.CanPay(alice).Should().BeTrue();

        cost.Pay(alice);

        alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>().Should().BeEmpty(
                "both artifacts were sacrificed");
        alice.Zones.Graveyard.GetCards()
            .Should().HaveCount(2,
                "both sacrificed artifacts went to the graveyard");
    }

    [Fact]
    public void SacrificeTwoArtifactsCost_CannotPay_WithOnlyOneArtifact()
    {
        var alice = new Player("Alice", 20);
        var a1 = new Artifact("Mox A", "{0}") { Owner = alice };
        a1.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(a1);

        var cost = new SacrificeTwoArtifactsCost();
        cost.CanPay(alice).Should().BeFalse(
            "CR 117.3 — costs must be paid in full; one artifact is not enough");
    }

    [Fact]
    public void Sai_DrawActivation_DrawsOneCard()
    {
        // Seed Alice's library with one card so the draw is observable.
        var alice = new Player("Alice", 20);
        var libraryCard = new Instant("Counterspell", "UU") { Owner = alice };
        alice.Zones.Library.AddCard(libraryCard);
        libraryCard.SetZone(ZoneType.Library);

        var sai = SaiMasterThopteristFactory.Create(alice);
        sai.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(sai);

        var activated = sai.Abilities.OfType<ActivatedAbility>().Single();

        // Skip cost payment — assert the effect body draws one card.
        foreach (var effect in activated.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(libraryCard,
            "Sai's activation draws one card on resolution");
    }
}
