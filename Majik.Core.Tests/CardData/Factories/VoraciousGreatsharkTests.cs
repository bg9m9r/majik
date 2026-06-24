using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Voracious Greatshark (Modern Horizons 2, {3}{U}{U}).
///
/// Oracle text (verified against Scryfall):
///   "Flash
///    When this creature enters, counter target artifact or creature spell."
///
/// Mystic Snake / Frilled Mystic (Flash + ETB counter target spell) with two
/// differences: the counter is mandatory (no "may" rider → MinTargets = 1) and
/// the legal target is filtered to artifact or creature spells (CR 608.2b gate
/// cribbed from Strix Serenade).
///
/// Covers:
///   - Identity (name, type, Shark subtype, 5/4, mana cost) — non-vanilla stats.
///   - Flash keyword marker + a single ETB triggered ability.
///   - ETB target request shape is mandatory (MinTargets = 1) + filtered text.
///   - ETB counters a chosen creature spell (CR 701.5 — owner's graveyard).
///   - ETB counters a chosen artifact spell.
///   - ETB no-ops on a non-artifact/creature target (filter, CR 608.2b).
///   - ETB no-ops on an illegal target (target no longer on the stack).
/// </summary>
[Trait("Color", "U")]
public class VoraciousGreatsharkTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public VoraciousGreatsharkTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    [Fact]
    public void VoraciousGreatshark_Identity()
    {
        var c = VoraciousGreatsharkFactory.Create(_alice);

        c.Name.Should().Be("Voracious Greatshark");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shark).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "ETB counter-target-artifact-or-creature-spell trigger");
    }

    [Fact]
    public void VoraciousGreatshark_Etb_TargetRequestShape_IsMandatoryAndFiltered()
    {
        var c = VoraciousGreatsharkFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1, "the counter is mandatory (no \"may\" rider)");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("artifact or creature spell");
    }

    [Fact]
    public void VoraciousGreatshark_Etb_CountersCreatureSpell()
    {
        var shark = VoraciousGreatsharkFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(shark);
        shark.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bear, _bob);
        _stack.Push(spell);

        var etb = shark.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { spell } });

        foreach (var e in etb.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.5 — countered creature spell goes to its owner's graveyard");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _stack.GetAll().Should().NotContain(spell);
    }

    [Fact]
    public void VoraciousGreatshark_Etb_CountersArtifactSpell()
    {
        var shark = VoraciousGreatsharkFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(shark);
        shark.SetZone(ZoneType.Battlefield);

        var relic = new Artifact("Mind Stone", "{2}");
        relic.SetOwner(_bob);
        relic.SetController(_bob);
        relic.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(relic, _bob);
        _stack.Push(spell);

        var etb = shark.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { spell } });

        foreach (var e in etb.Effects) e.Execute();

        relic.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.5 — countered artifact spell goes to its owner's graveyard");
        _bob.Zones.Graveyard.GetCards().Should().Contain(relic);
        _stack.GetAll().Should().NotContain(spell);
    }

    [Fact]
    public void VoraciousGreatshark_Etb_NonArtifactOrCreatureTarget_NoOps()
    {
        // CR 608.2b — only artifact or creature spells are legal targets. A
        // non-creature, non-artifact spell (e.g. an instant) is not countered.
        var shark = VoraciousGreatsharkFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(shark);
        shark.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bolt, _bob);
        _stack.Push(spell);

        var etb = shark.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { spell } });

        foreach (var e in etb.Effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Stack,
            "an instant is neither an artifact nor a creature spell — not countered");
        _stack.GetAll().Should().Contain(spell);
    }

    [Fact]
    public void VoraciousGreatshark_Etb_IllegalTarget_SpellNotOnStack_NoOps()
    {
        // CR 608.2b — if the targeted spell is no longer on the stack at
        // resolution, the counter does nothing.
        var shark = VoraciousGreatsharkFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(shark);
        shark.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bear);
        var spell = new Majik.Core.Spells.Spell(bear, _bob);

        var etb = shark.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { spell } });

        foreach (var e in etb.Effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "target was not on the stack — counter no-ops");
    }
}
