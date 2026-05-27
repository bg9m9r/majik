using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Pyromancer Ascension (Zendikar, {U}{R}, Enchantment).
///
/// Oracle:
///   "Whenever you cast an instant or sorcery spell that has the same name
///    as a card in your graveyard, you may put a quest counter on Pyromancer
///    Ascension.
///    As long as Pyromancer Ascension has two or more quest counters on it,
///    if you would cast an instant or sorcery spell, instead you cast that
///    spell and a copy of it."
///
/// Coverage:
/// - Identity + NamedCardFactory dispatch.
/// - Two structural triggered abilities attached.
/// - Quest-counter trigger fires only when a same-named card is in the
///   controller's graveyard AND the cast is by the controller AND the
///   spell is instant/sorcery.
/// - Copy trigger fires only at ≥2 quest counters; ignored otherwise.
/// </summary>
public class PyromancerAscensionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Create_HasEnchantmentShape_UR()
    {
        var p = PyromancerAscensionFactory.Create(_alice);

        p.Name.Should().Be("Pyromancer Ascension");
        p.HasType(CardType.Enchantment).Should().BeTrue();
        p.ManaCost.Should().Be("{U}{R}");
        p.ManaCostValue.TotalValue.Should().Be(2);
        var colors = CardColors.GetColors(p);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().Contain(ManaColor.Red);
        p.Owner.Should().BeSameAs(_alice);
        p.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsAscensionShape()
    {
        var dispatched = NamedCardFactory.Create("Pyromancer Ascension", _alice);

        dispatched.Should().BeOfType<Enchantment>();
        dispatched.Name.Should().Be("Pyromancer Ascension");
        dispatched.HasType(CardType.Enchantment).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{U}{R}");
    }

    // ---------------------------------------------------------------
    // Structural shape — two triggered abilities attached
    // ---------------------------------------------------------------

    [Fact]
    public void Card_HasTwoStructuralTriggers_OnSpellCastEvent()
    {
        var p = PyromancerAscensionFactory.Create(_alice);

        var triggers = p.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "Pyromancer Ascension models its quest-counter trigger and its " +
            "static copy effect as two SpellCastEvent triggered abilities");

        foreach (var t in triggers)
        {
            t.Source.Should().BeSameAs(p);
            t.Controller.Should().BeSameAs(_alice);
            t.ActiveZones.Should().Contain(ZoneType.Battlefield);
            t.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
        }
    }

    // ---------------------------------------------------------------
    // Quest-counter trigger: same-named instant/sorcery in graveyard
    // ---------------------------------------------------------------

    [Fact]
    public void QuestTrigger_PlacesCounter_WhenSameNamedInstantInGraveyard()
    {
        var p = PyromancerAscensionFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);

        // Seed the controller's graveyard with a Lightning Bolt.
        var graveBolt = new Instant("Lightning Bolt", "{R}");
        graveBolt.SetOwner(_alice);
        graveBolt.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(graveBolt);
        graveBolt.SetZone(ZoneType.Graveyard);

        // Cast a second Lightning Bolt (same name).
        var castBolt = new Instant("Lightning Bolt", "{R}");
        castBolt.SetOwner(_alice);
        castBolt.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(castBolt, _alice);

        var questTrigger = p.Abilities.OfType<TriggeredAbility>().First();
        var evt = new SpellCastEvent(spell);
        questTrigger.Condition.Matches(evt, questTrigger).Should().BeTrue();
        foreach (var e in questTrigger.Effects) e.Execute();

        p.Counters.Count(CounterType.Quest).Should().Be(1,
            "casting a same-named instant with a Bolt in the graveyard places one quest counter");
    }

    [Fact]
    public void QuestTrigger_DoesNotFire_WhenNoSameNamedCardInGraveyard()
    {
        var p = PyromancerAscensionFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);

        // Graveyard has a Shock — different name than the cast spell.
        var graveShock = new Instant("Shock", "{R}");
        graveShock.SetOwner(_alice);
        graveShock.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(graveShock);

        var castBolt = new Instant("Lightning Bolt", "{R}");
        castBolt.SetOwner(_alice);
        castBolt.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(castBolt, _alice);

        var questTrigger = p.Abilities.OfType<TriggeredAbility>().First();
        var evt = new SpellCastEvent(spell);
        questTrigger.Condition.Matches(evt, questTrigger).Should().BeFalse(
            "Lightning Bolt's name does not match Shock in the graveyard");
    }

    [Fact]
    public void QuestTrigger_DoesNotFire_ForOpponentCast()
    {
        var p = PyromancerAscensionFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);

        // Even with a same-named card in Alice's graveyard, Bob casting
        // his own spell does not satisfy "you cast" for Alice's Ascension.
        var graveBolt = new Instant("Lightning Bolt", "{R}");
        graveBolt.SetOwner(_alice);
        graveBolt.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(graveBolt);

        var bobBolt = new Instant("Lightning Bolt", "{R}");
        bobBolt.SetOwner(_bob);
        bobBolt.SetController(_bob);
        var spell = new Majik.Core.Spells.Spell(bobBolt, _bob);

        var questTrigger = p.Abilities.OfType<TriggeredAbility>().First();
        var evt = new SpellCastEvent(spell);
        questTrigger.Condition.Matches(evt, questTrigger).Should().BeFalse(
            "Bob is not the Ascension's controller — 'whenever you cast' does not fire");
    }

    [Fact]
    public void QuestTrigger_DoesNotFire_ForNonInstantSorcerySpell()
    {
        var p = PyromancerAscensionFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);

        var graveBlob = new Creature("Blob", "{R}", 1, 1);
        graveBlob.SetOwner(_alice);
        graveBlob.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(graveBlob);

        var castBlob = new Creature("Blob", "{R}", 1, 1);
        castBlob.SetOwner(_alice);
        castBlob.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(castBlob, _alice);

        var questTrigger = p.Abilities.OfType<TriggeredAbility>().First();
        var evt = new SpellCastEvent(spell);
        questTrigger.Condition.Matches(evt, questTrigger).Should().BeFalse(
            "creature spells are not instant or sorcery — trigger filters them out");
    }

    // ---------------------------------------------------------------
    // Copy trigger: ≥2 quest counters threshold
    // ---------------------------------------------------------------

    [Fact]
    public void CopyTrigger_DoesNotFire_BelowTwoQuestCounters()
    {
        var p = PyromancerAscensionFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);
        // One quest counter — below threshold.
        Majik.Core.Primitives.Fx.PlaceCounter(p, CounterType.Quest, 1);

        var castBolt = new Instant("Lightning Bolt", "{R}");
        castBolt.SetOwner(_alice);
        castBolt.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(castBolt, _alice);

        var copyTrigger = p.Abilities.OfType<TriggeredAbility>().Last();
        var evt = new SpellCastEvent(spell);
        copyTrigger.Condition.Matches(evt, copyTrigger).Should().BeFalse(
            "1 quest counter is below the 2-counter threshold");
    }

    [Fact]
    public void CopyTrigger_Fires_AtTwoQuestCounters_ForControllerInstantOrSorcery()
    {
        var p = PyromancerAscensionFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);
        Majik.Core.Primitives.Fx.PlaceCounter(p, CounterType.Quest, 2);

        var castBolt = new Instant("Lightning Bolt", "{R}");
        castBolt.SetOwner(_alice);
        castBolt.SetController(_alice);
        var spell = new Majik.Core.Spells.Spell(castBolt, _alice);

        var copyTrigger = p.Abilities.OfType<TriggeredAbility>().Last();
        var evt = new SpellCastEvent(spell);
        copyTrigger.Condition.Matches(evt, copyTrigger).Should().BeTrue(
            "at ≥2 quest counters the copy trigger fires for every instant/sorcery the controller casts");
    }

    [Fact]
    public void CopyTrigger_DoesNotFire_ForOpponentCast_EvenAtThreshold()
    {
        var p = PyromancerAscensionFactory.Create(_alice);
        p.SetZone(ZoneType.Battlefield);
        Majik.Core.Primitives.Fx.PlaceCounter(p, CounterType.Quest, 2);

        var bobBolt = new Instant("Lightning Bolt", "{R}");
        bobBolt.SetOwner(_bob);
        bobBolt.SetController(_bob);
        var spell = new Majik.Core.Spells.Spell(bobBolt, _bob);

        var copyTrigger = p.Abilities.OfType<TriggeredAbility>().Last();
        var evt = new SpellCastEvent(spell);
        copyTrigger.Condition.Matches(evt, copyTrigger).Should().BeFalse(
            "the copy effect applies only to Ascension's controller's casts");
    }

    [Fact]
    public void CopyTrigger_PushesCopy_ReExecutesSpellEffects()
    {
        var p = PyromancerAscensionFactory.Create(
            _alice,
            triggers: null,
            stack: new Majik.Core.Stack.Stack());
        p.SetZone(ZoneType.Battlefield);
        Majik.Core.Primitives.Fx.PlaceCounter(p, CounterType.Quest, 2);

        // Cast a synthetic spell whose effect ticks a counter — verifying
        // the copy re-runs the spell's effect list.
        var castBolt = new Instant("Lightning Bolt", "{R}");
        castBolt.SetOwner(_alice);
        castBolt.SetController(_alice);

        var ticks = 0;
        var tickEffect = new Effect("tick", () => ticks++);
        var spell = new Majik.Core.Spells.Spell(
            castBolt, _alice, targets: null, costs: null,
            effects: new IEffect[] { tickEffect });

        var copyTrigger = p.Abilities.OfType<TriggeredAbility>().Last();
        var evt = new SpellCastEvent(spell);
        copyTrigger.Condition.Matches(evt, copyTrigger).Should().BeTrue();

        // Resolve original + copy.
        foreach (var e in spell.Effects) e.Execute();
        foreach (var e in copyTrigger.Effects) e.Execute();

        ticks.Should().Be(2,
            "the original spell + the Ascension copy each execute the tick effect once");
    }
}
