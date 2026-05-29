using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Third Path Iconoclast (Dominaria United, {U}{R},
/// Creature — Human Monk 2/1).
///
/// Oracle (verified against Scryfall): "Whenever you cast a noncreature
/// spell, create a 1/1 colorless Soldier artifact creature token."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - One triggered ability present on the card.
///   - Casting an instant → 1/1 colourless Soldier artifact creature token.
///   - Casting a sorcery → token created.
///   - Casting an artifact (noncreature) spell → token created.
///   - Casting a creature spell → no token.
///   - Opponent casting a noncreature spell → no token for Alice.
/// </summary>
public class ThirdPathIconoclastTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstantSpell(Player controller, string name = "Bolt")
    {
        var instant = new Instant(name, "R") { Owner = controller };
        return new Majik.Core.Spells.Spell(instant, controller);
    }

    private static Majik.Core.Spells.Spell NewSorcerySpell(Player controller, string name = "Lava")
    {
        var sorcery = new Sorcery(name, "1R") { Owner = controller };
        return new Majik.Core.Spells.Spell(sorcery, controller);
    }

    private static Majik.Core.Spells.Spell NewArtifactSpell(Player controller, string name = "Trinket")
    {
        var artifact = new Artifact(name, "1") { Owner = controller };
        return new Majik.Core.Spells.Spell(artifact, controller);
    }

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ThirdPathIconoclast_Identity_HumanMonk_2_1_AtCostUR()
    {
        var tpi = ThirdPathIconoclastFactory.Create(_alice);

        tpi.Name.Should().Be("Third Path Iconoclast");
        tpi.ManaCost.Should().Be("{U}{R}");
        tpi.HasType(CardType.Creature).Should().BeTrue();
        tpi.HasSubtype(CardSubtype.Human).Should().BeTrue();
        tpi.HasSubtype(CardSubtype.Monk).Should().BeTrue();
        tpi.BasePower.Should().Be(2);
        tpi.BaseToughness.Should().Be(1);
        tpi.Owner.Should().BeSameAs(_alice);
        tpi.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ThirdPathIconoclast()
    {
        var card = NamedCardFactory.Create("Third Path Iconoclast", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Third Path Iconoclast");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Monk).Should().BeTrue();
    }

    [Fact]
    public void ThirdPathIconoclast_HasOneTriggeredAbility()
    {
        var tpi = ThirdPathIconoclastFactory.Create(_alice);
        tpi.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Token trigger — instant spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_CreatesOneColorlessSoldierArtifactCreatureToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tpi = ThirdPathIconoclastFactory.Create(_alice, bus, triggers);
        tpi.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(battlefieldBefore + 1);
        var token = battlefield.OfType<Creature>().Last();
        token.IsToken.Should().BeTrue();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        // CR 111.1 / 301.1 — artifact creature token.
        token.HasType(CardType.Artifact).Should().BeTrue();
        token.HasType(CardType.Creature).Should().BeTrue();
        // CR 105 / 111.4 — colourless.
        CardColors.GetColors(token).Should().BeEmpty();
        token.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Token trigger — sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingSorcery_CreatesOneSoldierToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tpi = ThirdPathIconoclastFactory.Create(_alice, bus, triggers);
        tpi.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(battlefieldBefore + 1);
        var token = battlefield.OfType<Creature>().Last();
        token.IsToken.Should().BeTrue();
        token.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        token.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Token trigger — noncreature artifact spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingNoncreatureArtifactSpell_CreatesOneSoldierToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tpi = ThirdPathIconoclastFactory.Create(_alice, bus, triggers);
        tpi.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewArtifactSpell(_alice, "Mishra's Bauble")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards().Count().Should().Be(battlefieldBefore + 1);
    }

    // -----------------------------------------------------------------------
    // No trigger on creature spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tpi = ThirdPathIconoclastFactory.Create(_alice, bus, triggers);
        tpi.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(battlefieldBefore);
    }

    // -----------------------------------------------------------------------
    // Opponent's cast does not trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastingNoncreatureSpell_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var tpi = ThirdPathIconoclastFactory.Create(_alice, bus, triggers);
        tpi.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
    }
}
