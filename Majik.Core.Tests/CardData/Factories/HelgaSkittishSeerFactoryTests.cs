using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HelgaSkittishSeerFactory"/> (Bloomburrow — Legendary
/// Creature — Frog Druid {G}{W}{U} 1/3).
///
/// Oracle text (verified against Scryfall):
///   "Whenever you cast a creature spell with mana value 4 or greater, you draw
///    a card, gain 1 life, and put a +1/+1 counter on Helga.
///    {T}: Add X mana of any one color, where X is Helga's power. Spend this
///    mana only to cast creature spells with mana value 4 or greater or creature
///    spells with {X} in their mana costs."
///
/// Covers (the card's UNIQUE behaviour):
/// - Identity (Legendary Frog Druid 1/3 {G}{W}{U}).
/// - Cast trigger (CR 603.1): matches a controller's MV-4+ creature spell; NOT
///   an MV-3 creature spell, NOT an MV-4+ noncreature spell, NOT an opponent's
///   spell.
/// - Resolution draws a card, gains 1 life, and puts a +1/+1 counter on Helga.
/// - {T} mana ability scales with power (alone → X = 1) and carries the
///   big/{X}-creature-spell spend restriction.
/// </summary>
[Trait("Color", "M")]
public class HelgaSkittishSeerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell CreatureSpell(Player controller, string cost, string name = "Big Beast")
    {
        var c = new Creature(name, cost, 4, 4) { Owner = controller };
        return new Majik.Core.Spells.Spell(c, controller);
    }

    private static Majik.Core.Spells.Spell NoncreatureSpell(Player controller, string cost, string name = "Big Spell")
    {
        var s = new Sorcery(name, cost) { Owner = controller };
        return new Majik.Core.Spells.Spell(s, controller);
    }

    private static TriggeredAbility CastTrigger(Creature helga)
        => helga.Abilities.OfType<TriggeredAbility>().Single();

    // ── Identity ───────────────────────────────────────────────────────

    [Fact]
    public void Identity()
    {
        var c = HelgaSkittishSeerFactory.Create(_alice);

        c.Name.Should().Be("Helga, Skittish Seer");
        c.ManaCost.Should().Be("{G}{W}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Frog).Should().BeTrue();
        c.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasCastTriggerAndFiveManaAbilities()
    {
        var c = HelgaSkittishSeerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the MV-4+ creature-cast draw/gain/counter trigger.");
        c.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "{T}: Add X mana of any one color — five WUBRG abilities (Ancient Ziggurat shape).");
    }

    // ── Cast trigger condition (CR 603.1) ───────────────────────────────

    [Fact]
    public void CastTrigger_Matches_ControllerCreatureSpell_MV4Plus()
    {
        var helga = HelgaSkittishSeerFactory.Create(_alice);
        helga.SetZone(ZoneType.Battlefield);

        var trigger = CastTrigger(helga);
        var evt = new SpellCastEvent(CreatureSpell(_alice, "{2}{G}{G}")); // MV 4

        trigger.Condition.Matches(evt, trigger).Should().BeTrue(
            "casting your own creature spell with mana value 4+ triggers it (CR 603.1).");
    }

    [Fact]
    public void CastTrigger_DoesNotMatch_CreatureSpell_BelowMV4()
    {
        var helga = HelgaSkittishSeerFactory.Create(_alice);
        helga.SetZone(ZoneType.Battlefield);

        var trigger = CastTrigger(helga);
        var evt = new SpellCastEvent(CreatureSpell(_alice, "{1}{G}")); // MV 2

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "a creature spell with mana value below 4 doesn't trigger it (CR 202.3).");
    }

    [Fact]
    public void CastTrigger_DoesNotMatch_NoncreatureSpell_MV4Plus()
    {
        var helga = HelgaSkittishSeerFactory.Create(_alice);
        helga.SetZone(ZoneType.Battlefield);

        var trigger = CastTrigger(helga);
        var evt = new SpellCastEvent(NoncreatureSpell(_alice, "{2}{U}{U}")); // MV 4 sorcery

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "only a CREATURE spell triggers it (CR 110.4), not a noncreature MV-4+ spell.");
    }

    [Fact]
    public void CastTrigger_DoesNotMatch_OpponentCreatureSpell()
    {
        var helga = HelgaSkittishSeerFactory.Create(_alice);
        helga.SetZone(ZoneType.Battlefield);

        var trigger = CastTrigger(helga);
        var evt = new SpellCastEvent(CreatureSpell(_bob, "{2}{G}{G}")); // MV 4, opponent

        trigger.Condition.Matches(evt, trigger).Should().BeFalse(
            "CR 603.1 — 'whenever YOU cast' excludes the opponent's spells.");
    }

    // ── Cast trigger resolution (draw / gain life / counter) ────────────

    [Fact]
    public void CastEffect_DrawsGainsLifeAndPutsCounterOnHelga()
    {
        var helga = HelgaSkittishSeerFactory.Create(_alice);
        helga.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(helga);

        // Seed the library so the draw has a card to take (CR 120).
        var topCard = new Creature("Reserve", "{1}", 1, 1);
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);

        int handBefore = _alice.Zones.Hand.GetCards().Count();
        int lifeBefore = _alice.LifeTotal;

        var trigger = CastTrigger(helga);
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1, "you draw a card (CR 120).");
        _alice.LifeTotal.Should().Be(lifeBefore + 1, "you gain 1 life (CR 119.3).");
        helga.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "a +1/+1 counter is placed on Helga (CR 122 / CR 121.2).");
    }

    // ── {T}: Add X mana of any one color (CR 605.1 / 107.1b) ────────────

    [Fact]
    public void ManaAbility_AloneProducesOneManaOfEachColor()
    {
        // The five abilities cover WUBRG; with power 1, X = 1, so each produces a
        // single pip of its own colour (CR 605.1 / 107.1b). Activation taps, so
        // build a fresh Helga per colour to read each ability in isolation.
        var produced = new System.Collections.Generic.List<string>();
        for (int i = 0; i < 5; i++)
        {
            var helga = HelgaSkittishSeerFactory.Create(_alice);
            helga.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(helga);
            // CR 302.6 — clear summoning sickness so the {T} ability is legal.
            helga.ClearSummoningSickness();

            var ability = helga.Abilities.OfType<ManaAbility>().ElementAt(i);
            ability.CanActivate().Should().BeTrue();
            produced.Add(ability.Activate().ToString());
            helga.IsTapped.Should().BeTrue("the {T} cost is paid on activation.");
        }

        produced.Should().BeEquivalentTo(
            new[] { "W", "U", "B", "R", "G" },
            "with power 1 each colour ability produces one pip of its colour (X = 1).");
    }

    [Fact]
    public void ManaAbilities_AllCarryBigOrXCreatureSpendRestriction()
    {
        var helga = HelgaSkittishSeerFactory.Create(_alice);

        var manaAbilities = helga.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5);
        manaAbilities.Should().OnlyContain(m => m.SpendRestriction != null,
            "every 'any color' ability stamps the spend restriction (CR 106.4).");

        var restriction = manaAbilities[0].SpendRestriction!;

        // MV-4+ creature spell → permitted.
        restriction.SatisfiedBy(CreatureSpell(_alice, "{2}{G}{G}")).Should().BeTrue(
            "creature spell with mana value 4+ may be paid by Helga's mana.");
        // Creature spell with {X} in its cost (MV < 4) → permitted.
        restriction.SatisfiedBy(CreatureSpell(_alice, "{X}{G}")).Should().BeTrue(
            "creature spell with {X} in its cost may be paid by Helga's mana.");
        // Small creature spell, no {X} → NOT permitted.
        restriction.SatisfiedBy(CreatureSpell(_alice, "{1}{G}")).Should().BeFalse(
            "a creature spell below MV 4 with no {X} can't be paid by Helga's mana.");
        // MV-4+ noncreature spell → NOT permitted.
        restriction.SatisfiedBy(NoncreatureSpell(_alice, "{2}{U}{U}")).Should().BeFalse(
            "a noncreature spell can't be paid by Helga's mana, even at MV 4+.");
    }
}
