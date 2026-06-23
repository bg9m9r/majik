using FluentAssertions;
using Majik.Core.Abilities;
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
/// Tests for Mishra's Self-Replicator (The Brothers' War, {5}).
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (2/2 colourless Artifact Creature — Assembly-Worker, {5}).
///   - Cast-historic-spell trigger fires on the controller's HISTORIC spells
///     (artifact / legendary / Saga), ignores non-historic + opponent casts.
///   - Resolution mints a token that's a copy of this creature (same name,
///     P/T, Artifact + Creature types, Assembly-Worker subtype) AND the copy
///     carries the same cast trigger (the self-replicating snowball).
/// </summary>
[Trait("Color", "C")]
public class MishrasSelfReplicatorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewArtifactSpell(Player controller, string name = "Mox Opal")
    {
        var artifact = new Artifact(name, "{0}") { Owner = controller };
        return new Majik.Core.Spells.Spell(artifact, controller);
    }

    private static Majik.Core.Spells.Spell NewLegendarySpell(Player controller, string name = "Karn")
    {
        // Legendary noncreature spell — historic via the legend supertype.
        var sorcery = new Sorcery(name, "{2}", supertypes: new[] { CardSupertype.Legendary })
        {
            Owner = controller,
        };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "{R}") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Mishra_Identity_ColourlessArtifactCreatureAssemblyWorker_2_2_At5()
    {
        var card = MishrasSelfReplicatorFactory.Create(_alice);

        card.Name.Should().Be("Mishra's Self-Replicator");
        card.ManaCost.Should().Be("{5}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.AssemblyWorker).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        CardColors.GetColors(card).Should().BeEmpty("the printed card is colourless");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Mishra_HasOneCastTrigger()
    {
        var card = MishrasSelfReplicatorFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the whenever-you-cast-a-historic-spell trigger");
    }

    // -----------------------------------------------------------------------
    // Cast-historic-spell trigger — predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void HistoricArtifactCast_ByController_FiresTrigger()
    {
        var (_, triggers, _) = Wire();

        Bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Mox Opal")));

        triggers.PendingCount.Should().Be(1,
            "an artifact spell is historic — Mishra's trigger fires");
    }

    [Fact]
    public void HistoricLegendaryCast_ByController_FiresTrigger()
    {
        var (_, triggers, _) = Wire();

        Bus.Publish(new SpellCastEvent(NewLegendarySpell(_alice, "Karn")));

        triggers.PendingCount.Should().Be(1,
            "a legendary spell is historic — Mishra's trigger fires (CR 205.2b)");
    }

    [Fact]
    public void NonHistoricCast_ByController_DoesNotFireTrigger()
    {
        var (_, triggers, _) = Wire();

        Bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(0,
            "Lightning Bolt is not historic — Mishra's trigger does not fire");
    }

    [Fact]
    public void OpponentHistoricCast_DoesNotFireTrigger()
    {
        var (_, triggers, _) = Wire();

        Bus.Publish(new SpellCastEvent(NewArtifactSpell(_bob, "Bob's Mox")));

        triggers.PendingCount.Should().Be(0,
            "\"whenever YOU cast\" — Bob's historic spell does not fire it (CR 109.5)");
    }

    // -----------------------------------------------------------------------
    // Resolution — token copy of this creature
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenCopy_IsACopyOfThisCreature_AndCarriesTheCastTrigger()
    {
        // Agent-less path auto-pays {1} (Mentor of the Meek posture); seed
        // Alice's mana pool so PayMana succeeds.
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("1")); // a floating {1} to pay the optional cost

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = MishrasSelfReplicatorFactory.Create(alice, triggers, zoneService: null);
        card.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(card);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(alice, "Mox Opal")));
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(alice);
        stack.Pop()!.Resolve();

        var copy = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Mishra's Self-Replicator");

        copy.IsToken.Should().BeTrue("CR 111.1 — minted as a token");
        copy.BasePower.Should().Be(2);
        copy.BaseToughness.Should().Be(2);
        copy.HasType(CardType.Artifact).Should().BeTrue(
            "the copy is an Artifact Creature (CR 706.2 copies card types)");
        copy.HasType(CardType.Creature).Should().BeTrue();
        copy.HasSubtype(CardSubtype.AssemblyWorker).Should().BeTrue(
            "CR 706.2 — the copy snapshots the source's subtypes");
        copy.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "CR 706.2 — the copy carries the same cast trigger (self-replicating snowball)");
    }

    [Fact]
    public void TokenCopy_NotMade_WhenManaUnavailable()
    {
        // Agent-less auto-pay, but no mana in pool → PayMana fails →
        // trigger fizzles with no token (CR 117.5).
        var alice = new Player("Alice", 20);

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = MishrasSelfReplicatorFactory.Create(alice, triggers, zoneService: null);
        card.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(card);

        bus.Publish(new SpellCastEvent(NewArtifactSpell(alice, "Mox Opal")));
        triggers.PutPendingTriggersOnStack(alice);
        stack.Pop()!.Resolve();

        alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken).Should().Be(0,
                "no mana to pay {1} → trigger fizzles, no token (CR 117.5)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private EventBus Bus = null!;

    private (Creature card, TriggerManager triggers, Majik.Core.Stack.Stack stack) Wire()
    {
        Bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(Bus);
        var triggers = new TriggerManager(stack, Bus);
        var card = MishrasSelfReplicatorFactory.Create(_alice, triggers, zoneService: null);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);
        return (card, triggers, stack);
    }
}
