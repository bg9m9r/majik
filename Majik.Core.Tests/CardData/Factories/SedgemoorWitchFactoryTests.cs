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
/// Unit tests for <see cref="SedgemoorWitchFactory"/>
/// (Strixhaven: School of Mages, {2}{B}).
///
/// Creature — Human Warlock 3/2. Oracle text (Scryfall, verified):
///   "Menace
///    Ward—Pay 3 life. (Whenever this creature becomes the target of a spell
///    or ability an opponent controls, counter it unless that player pays
///    3 life.)
///    Magecraft — Whenever you cast or copy an instant or sorcery spell,
///    create a 1/1 black and green Pest creature token with 'When this token
///    dies, you gain 1 life.'"
///
/// Covers:
///   - Identity (Creature — Human Warlock, {2}{B}, 3/2, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Menace + Ward keyword markers.
///   - <see cref="SedgemoorWitchFactory.BuildWardEffect"/> charges a real
///     PayLifeCost(3) on Resolve against an opponent.
///   - Magecraft cast-half: casting an instant / sorcery → one 1/1 black-and-
///     green Pest token; creature spell / opponent's cast → no token.
///   - Pest token carries the "When this token dies, you gain 1 life" trigger.
/// </summary>
public class SedgemoorWitchFactoryTests
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
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SedgemoorWitch_Identity_HumanWarlock_3_2_At2B()
    {
        var witch = SedgemoorWitchFactory.Create(_alice);

        witch.Name.Should().Be("Sedgemoor Witch");
        witch.ManaCost.Should().Be("{2}{B}");
        witch.HasType(CardType.Creature).Should().BeTrue();
        witch.HasSubtype(CardSubtype.Human).Should().BeTrue();
        witch.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        witch.BasePower.Should().Be(3);
        witch.BaseToughness.Should().Be(2);
        witch.Owner.Should().BeSameAs(_alice);
        witch.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SedgemoorWitch_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Sedgemoor Witch", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sedgemoor Witch");
        card.HasSubtype(CardSubtype.Warlock).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Keyword markers + Ward
    // -----------------------------------------------------------------------

    [Fact]
    public void SedgemoorWitch_HasMenaceAndWardMarkers()
    {
        var witch = SedgemoorWitchFactory.Create(_alice);
        var keywords = witch.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().Contain("Menace", "CR 702.111 — Menace marker");
        keywords.Should().Contain("Ward",
            "CR 702.21 — Ward marker (printed 'Pay 3 life' rider)");
    }

    [Fact]
    public void SedgemoorWitch_Ward_OpponentTargets_PaysThreeLifeOrSpellCountered()
    {
        // CR 702.21c — Ward—Pay 3 life. The bound WardEffect charges a real
        // PayLifeCost(3) on Resolve against an opponent.
        var witch = SedgemoorWitchFactory.Create(_alice);
        witch.SetController(_alice);
        var ward = SedgemoorWitchFactory.BuildWardEffect(witch);

        ward.Source.Should().BeSameAs(witch);
        ward.Cost.TotalValue.Should().Be(0,
            "printed cost is non-mana (Pay 3 life) — mana portion is zero");

        var bob = new Player("Bob", 20);
        var countered = ward.Resolve(bob);
        countered.Should().BeFalse("Bob can pay 3 life from 20");
        bob.LifeTotal.Should().Be(17, "Ward—Pay 3 life charged 3 life");

        var poorBob = new Player("PoorBob", 2);
        var countered2 = ward.Resolve(poorBob);
        countered2.Should().BeTrue("PoorBob cannot pay 3 life from 2");
        poorBob.LifeTotal.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Magecraft cast-half — instant
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingInstant_CreatesOnePestToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var witch = SedgemoorWitchFactory.Create(_alice, bus, triggers);
        witch.SetZone(ZoneType.Battlefield);

        var before = _alice.Zones.Battlefield.GetCards().Count();

        bus.Publish(new SpellCastEvent(NewInstantSpell(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var battlefield = _alice.Zones.Battlefield.GetCards();
        battlefield.Should().HaveCount(before + 1);
        var token = battlefield.OfType<Creature>().Last();
        token.IsToken.Should().BeTrue();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Pest).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);

        // CR 105 / 111.4 — black AND green Pest token.
        var colors = CardColors.GetColors(token);
        colors.Should().Contain(ManaColor.Black);
        colors.Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void CastingSorcery_CreatesOnePestToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var witch = SedgemoorWitchFactory.Create(_alice, bus, triggers);
        witch.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewSorcerySpell(_alice, "Lava Spike")));
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        var token = _alice.Zones.Battlefield.GetCards().OfType<Creature>().Last();
        token.IsToken.Should().BeTrue();
        token.HasSubtype(CardSubtype.Pest).Should().BeTrue();
    }

    [Fact]
    public void CastingCreatureSpell_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var witch = SedgemoorWitchFactory.Create(_alice, bus, triggers);
        witch.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));
        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void OpponentCastingInstant_DoesNotCreateToken()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var witch = SedgemoorWitchFactory.Create(_alice, bus, triggers);
        witch.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstantSpell(_bob, "Bob's Bolt")));
        triggers.PendingCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Pest token dies trigger — "When this token dies, you gain 1 life."
    // -----------------------------------------------------------------------

    [Fact]
    public void PestToken_CarriesDiesGainLifeTrigger()
    {
        var token = SedgemoorWitchFactory.CreatePestToken(_alice, triggers: null);

        var diesTriggers = token.Abilities.OfType<TriggeredAbility>().ToList();
        diesTriggers.Should().HaveCount(1,
            "Pest token has the 'When this token dies, you gain 1 life' trigger");
    }
}
