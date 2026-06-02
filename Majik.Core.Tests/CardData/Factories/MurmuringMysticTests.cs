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
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Murmuring Mystic (Guilds of Ravnica, {3}{U},
/// Creature — Human Wizard 1/5).
///
/// Oracle text (Scryfall, verified):
///   "Whenever you cast an instant or sorcery spell, create a 1/1 blue Bird
///    Illusion creature token with flying."
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost, owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - One triggered ability present on the card.
///   - Casting an instant → 1/1 blue Bird Illusion flying token created.
///   - Casting a sorcery → token created.
///   - Casting a creature spell → no token.
///   - Opponent casting an instant → no token for Alice.
/// </summary>
public class MurmuringMysticTests
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
    public void MurmuringMystic_Identity_HumanWizard_1_5_AtCost3U()
    {
        var mm = MurmuringMysticFactory.Create(_alice);

        mm.Name.Should().Be("Murmuring Mystic");
        mm.ManaCost.Should().Be("{3}{U}");
        mm.HasType(CardType.Creature).Should().BeTrue();
        mm.HasSubtype(CardSubtype.Human).Should().BeTrue();
        mm.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        mm.BasePower.Should().Be(1);
        mm.BaseToughness.Should().Be(5);
        mm.Owner.Should().BeSameAs(_alice);
        mm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MurmuringMystic()
    {
        var card = NamedCardFactory.Create("Murmuring Mystic", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Murmuring Mystic");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void MurmuringMystic_HasOneTriggeredAbility()
    {
        var mm = MurmuringMysticFactory.Create(_alice);
        mm.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Token trigger — instant spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_CreatesOneBlueBirdIllusionFlyingToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mm = MurmuringMysticFactory.Create(_alice, bus, triggers);
        mm.SetZone(ZoneType.Battlefield);

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
        token.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        token.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        CardColors.GetColors(token).Should().Contain(ManaColor.Blue);
        token.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "the printed Bird Illusion token has flying (CR 702.9)");
        token.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Token trigger — sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingSorcery_CreatesOneBirdIllusionToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var mm = MurmuringMysticFactory.Create(_alice, bus, triggers);
        mm.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(battlefieldBefore + 1);
        var token = battlefield.OfType<Creature>().Last();
        token.IsToken.Should().BeTrue();
        token.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        token.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
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

        var mm = MurmuringMysticFactory.Create(_alice, bus, triggers);
        mm.SetZone(ZoneType.Battlefield);

        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(battlefieldBefore);
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

        var mm = MurmuringMysticFactory.Create(_alice, bus, triggers);
        mm.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(0);
    }
}
