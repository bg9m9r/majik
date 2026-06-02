using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Frilled Mystic (Ravnica Allegiance, {G}{G}{U}{U}).
///
/// Oracle text (verified against Scryfall):
///   "Flash
///    When this creature enters, you may counter target spell."
///
/// A near-functional reprint of Mystic Snake (Flash + ETB counter target
/// spell) with a "may" rider — so the ETB target is optional (CR 700.2:
/// MinTargets = 0, "up to one"). Mirrors <see cref="MysticSnakeTests"/>.
///
/// Covers:
///   - Identity (name, type, Elf/Lizard/Wizard subtypes, 3/2, mana cost).
///   - NamedCardFactory dispatch.
///   - Flash keyword marker + a single ETB triggered ability.
///   - ETB target request shape is optional (MinTargets = 0).
///   - ETB counters any chosen target spell (CR 701.5 — owner's graveyard).
///   - "you may" decline: no target chosen → clean no-op.
///   - ETB no-ops on an illegal target (target no longer on the stack).
/// </summary>
public class FrilledMysticTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FrilledMysticTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    [Fact]
    public void FrilledMystic_Identity()
    {
        var c = FrilledMysticFactory.Create(_alice);

        c.Name.Should().Be("Frilled Mystic");
        c.ManaCost.Should().Be("{G}{G}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Lizard).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "ETB may-counter-target-spell trigger");
    }

    [Fact]
    public void FrilledMystic_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Frilled Mystic", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Frilled Mystic");
        c.HasSubtype(CardSubtype.Lizard).Should().BeTrue();
    }

    [Fact]
    public void FrilledMystic_Etb_TargetRequestShape_IsOptional()
    {
        var c = FrilledMysticFactory.Create(_alice);
        var etb = c.Abilities.OfType<TriggeredAbility>().Single();

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.TargetRequests.Should().HaveCount(1);
        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(0, "\"you may\" — the counter is optional");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("target spell");
    }

    [Fact]
    public void FrilledMystic_Etb_CountersChosenSpell()
    {
        var mystic = FrilledMysticFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(mystic);
        mystic.SetZone(ZoneType.Battlefield);

        var bigSpell = new Sorcery("Expensive Sorcery", "{5}{U}{U}");
        bigSpell.SetOwner(_bob);
        bigSpell.SetController(_bob);
        bigSpell.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bigSpell, _bob);
        _stack.Push(spell);

        var etb = mystic.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });

        foreach (var e in etb.Effects) e.Execute();

        bigSpell.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.5 — countered spell goes to its owner's graveyard");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bigSpell);
        _stack.GetAll().Should().NotContain(spell);
    }

    [Fact]
    public void FrilledMystic_Etb_MayDecline_NoTargetChosen_NoOps()
    {
        // "you may" — controller declines by choosing no target (CR 700.2).
        // The spell on the stack is left untouched.
        var mystic = FrilledMysticFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(mystic);
        mystic.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Stack);
        var spell = new Majik.Core.Spells.Spell(bolt, _bob);
        _stack.Push(spell);

        var etb = mystic.Abilities.OfType<TriggeredAbility>().Single();
        // No targets chosen — the "may" decline path.
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
        });

        foreach (var e in etb.Effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Stack,
            "declined counter — the spell stays on the stack");
        _stack.GetAll().Should().Contain(spell);
    }

    [Fact]
    public void FrilledMystic_Etb_IllegalTarget_SpellNotOnStack_NoOps()
    {
        // CR 608.2b — if the targeted spell is no longer on the stack at
        // resolution, the counter does nothing.
        var mystic = FrilledMysticFactory.Create(_alice, _stack, triggers: null);
        _alice.Zones.Battlefield.AddCard(mystic);
        mystic.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bolt);
        var spell = new Majik.Core.Spells.Spell(bolt, _bob);

        var etb = mystic.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { spell },
        });

        foreach (var e in etb.Effects) e.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "target was not on the stack — counter no-ops");
    }
}
