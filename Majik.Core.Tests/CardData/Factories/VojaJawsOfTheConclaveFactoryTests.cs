using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Tests.Helpers;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="VojaJawsOfTheConclaveFactory"/>.
///
/// Voja, Jaws of the Conclave — {2}{R}{G}{W} Legendary Creature — Wolf, 5/5.
/// Oracle text (verified against Scryfall):
///   "Vigilance, trample, ward {3}
///    Whenever Voja attacks, put X +1/+1 counters on each creature you
///    control, where X is the number of Elves you control. Draw a card for
///    each Wolf you control."
///
/// Covers:
///   - Identity: {2}{R}{G}{W} Legendary RGW Wolf 5/5, mana value 5.
///   - Vigilance / Trample / Ward keyword markers (CR 702.21 / 702.19).
///   - Attack trigger: "Whenever Voja attacks" fires when Voja attacks, places
///     X +1/+1 counters (X = Elves you control) on EACH creature you control,
///     and draws one card per Wolf you control (CR 508.3).
///   - X = 0 (no Elves) places no counters but still draws per Wolf.
///   - Trigger does NOT fire when a non-Voja creature attacks.
/// </summary>
[Trait("Color", "M")]
public class VojaJawsOfTheConclaveFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewCreature(Player controller, string name, CardSubtype? subtype = null)
    {
        var subtypes = subtype is null ? Array.Empty<CardSubtype>() : new[] { subtype.Value };
        var creature = new Creature(name, "{G}", 2, 2, subtypes: subtypes);
        creature.SetOwner(controller);
        creature.SetController(controller);
        return creature;
    }

    private void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Creature($"Lib{i}", "{G}", 1, 1);
            c.SetOwner(p);
            c.SetController(p);
            p.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Voja_Identity_LegendaryRgwWolf_5_5_AtCost2RGW()
    {
        var card = VojaJawsOfTheConclaveFactory.Create(_alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Voja, Jaws of the Conclave");
        card.ManaCost.Should().Be("{2}{R}{G}{W}");
        card.ManaCostValue.TotalValue.Should().Be(5, "{2}{R}{G}{W} is mana value 5");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wolf).Should().BeTrue();
        card.BasePower.Should().Be(5);
        card.BaseToughness.Should().Be(5);
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().Contain(ManaColor.Green);
        colors.Should().Contain(ManaColor.White);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Voja_HasVigilanceTrampleAndWardKeywordMarkers()
    {
        var card = VojaJawsOfTheConclaveFactory.Create(_alice);

        var keywords = card.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Any(k => string.Equals(k.Keyword, "Vigilance", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
        keywords.Any(k => string.Equals(k.Keyword, "Trample", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
        keywords.Any(k => string.Equals(k.Keyword, "Ward", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public void Voja_HasAttackTriggeredAbility()
    {
        var card = VojaJawsOfTheConclaveFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Attack trigger — counters + draw (CR 508.3).
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_VojaAttacks_PutsXCountersOnEachCreatureAndDrawsPerWolf()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);

        var card = VojaJawsOfTheConclaveFactory.Create(_alice, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        // Two Elves you control → X = 2.
        var elf1 = NewCreature(_alice, "Elf1", CardSubtype.Elf);
        _alice.Zones.Battlefield.AddCard(elf1);
        elf1.SetZone(ZoneType.Battlefield);
        var elf2 = NewCreature(_alice, "Elf2", CardSubtype.Elf);
        _alice.Zones.Battlefield.AddCard(elf2);
        elf2.SetZone(ZoneType.Battlefield);

        // A second Wolf you control → 2 Wolves total (Voja + this), draw 2.
        var wolf = NewCreature(_alice, "Wolf", CardSubtype.Wolf);
        _alice.Zones.Battlefield.AddCard(wolf);
        wolf.SetZone(ZoneType.Battlefield);

        // An opponent's Elf does NOT count toward X.
        var bobElf = NewCreature(_bob, "BobElf", CardSubtype.Elf);
        _bob.Zones.Battlefield.AddCard(bobElf);
        bobElf.SetZone(ZoneType.Battlefield);

        SeedLibrary(_alice, 5);
        var startHand = _alice.Zones.Hand.GetCards().Count();

        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(card, targetPlayer: _bob),
        });

        triggers.PendingCount.Should().Be(1, "'Whenever Voja attacks' fires when Voja attacks");

        var attack = card.Abilities.OfType<TriggeredAbility>().Single();
        ContextResolve.Resolve(attack, _alice, _alice, _bob);

        // X = 2 Elves → 2 +1/+1 counters on EACH creature you control
        // (Voja, elf1, elf2, wolf), and none on the opponent's Elf.
        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2, "Voja gets X counters too");
        elf1.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        elf2.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        wolf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        bobElf.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0, "only creatures YOU control");

        // 2 Wolves you control (Voja + wolf) → draw 2.
        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 2,
            "draw a card for each Wolf you control (Voja + the other Wolf = 2)");
    }

    [Fact]
    public void AttackTrigger_NoElves_PlacesNoCountersButStillDrawsPerWolf()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);

        var card = VojaJawsOfTheConclaveFactory.Create(_alice, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.ClearSummoningSickness();

        SeedLibrary(_alice, 3);
        var startHand = _alice.Zones.Hand.GetCards().Count();

        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(card, targetPlayer: _bob),
        });

        var attack = card.Abilities.OfType<TriggeredAbility>().Single();
        ContextResolve.Resolve(attack, _alice, _alice, _bob);

        // X = 0 Elves → no counters anywhere.
        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0, "X = 0, no counters placed");

        // Voja is a Wolf → still draw 1.
        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 1,
            "Voja herself is a Wolf you control, so draw 1");
    }

    [Fact]
    public void AttackTrigger_NonVojaAttacks_DoesNotFire()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var combat = new CombatManager(bus);

        var card = VojaJawsOfTheConclaveFactory.Create(_alice, triggers, replacements: null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // A different creature (not Voja) attacks.
        var bear = NewCreature(_alice, "Bear");
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        bear.ClearSummoningSickness();

        combat.StartCombat(_alice);
        combat.DeclareAttackers(_alice, new[]
        {
            new AttackerDeclaration(bear, targetPlayer: _bob),
        });

        triggers.PendingCount.Should().Be(0,
            "'Whenever Voja attacks' only fires when Voja is among the attackers");
    }
}
