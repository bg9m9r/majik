using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Stormsplitter (Outlaws of Thunder Junction, {3}{R},
/// Creature — Otter Wizard 1/4).
///
/// Oracle text:
///   "Haste
///    Whenever you cast an instant or sorcery spell, create a token that's a
///    copy of this creature. Exile that token at the beginning of the next end
///    step."
///
/// Covers the card's UNIQUE behaviour:
///   - Casting an instant / sorcery → a token copy of Stormsplitter (1/4 Otter
///     Wizard with Haste, summoning sickness cleared) on the battlefield.
///   - Casting a creature spell → no token.
///   - An opponent's instant → no token for Alice.
///   - The spawned token is exiled at the beginning of the next end step.
///   - Card identity (mana cost / P-T / subtypes / Haste).
/// </summary>
[Trait("Color", "R")]
public class StormsplitterTests
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

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear")
    {
        var creature = new Creature(name, "1G", 2, 2) { Owner = controller };
        return new Majik.Core.Spells.Spell(creature, controller);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Stormsplitter_Identity_OtterWizard_1_4_AtCost3R_WithHaste()
    {
        var card = StormsplitterFactory.Create(_alice);

        card.Name.Should().Be("Stormsplitter");
        card.ManaCost.Should().Be("{3}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Otter).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(4);
        card.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Haste").Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Cast trigger — instant → self-copy token
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_CreatesTokenCopyOfStormsplitter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = StormsplitterFactory.Create(_alice, bus, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var before = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(before + 1);

        var token = battlefield.OfType<Creature>().Single(c => c.IsToken);
        token.Name.Should().Be("Stormsplitter");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(4);
        token.HasSubtype(CardSubtype.Otter).Should().BeTrue();
        token.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        token.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Haste").Should().BeTrue();
        // CR 702.10b — copied Haste clears summoning sickness.
        token.HasSummoningSickness.Should().BeFalse();
        token.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cast trigger — sorcery → self-copy token
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingSorcery_CreatesTokenCopyOfStormsplitter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = StormsplitterFactory.Create(_alice, bus, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        var before = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().HaveCount(before + 1);
        var token = _alice.Zones.Battlefield.GetCards().OfType<Creature>().Single(c => c.IsToken);
        token.Name.Should().Be("Stormsplitter");
    }

    // -----------------------------------------------------------------------
    // No trigger on a creature spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = StormsplitterFactory.Create(_alice, bus, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Opponent's cast does not trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastingInstant_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = StormsplitterFactory.Create(_alice, bus, triggers);
        card.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Delayed end-step exile (CR 603.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpawnedToken_IsExiledAtNextEndStep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var card = StormsplitterFactory.Create(_alice, bus, triggers);
        card.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(card);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var token = _alice.Zones.Battlefield.GetCards().OfType<Creature>().Single(c => c.IsToken);
        token.Zone.Should().Be(ZoneType.Battlefield);

        // CR 603.7 — at the beginning of the next end step, exile the token.
        bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        token.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(token);
        _alice.Zones.Exile.GetCards().Should().Contain(token);
    }
}
