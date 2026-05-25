using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Coverage tests for two iconic single-effect spells that should bind
/// through the existing template registry without any new factory:
///   - Counterspell  → CounterTargetSpellTemplate (Counter target spell.)
///   - Lightning Bolt → DamageAnyTargetTemplate   (deals 3 damage to any target.)
/// Identity + binding + resolution are exercised here so seeding either
/// card via <c>SeedImplementedCards</c> stays honest.
/// </summary>
public class CounterspellLightningBoltBindingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Counterspell_BindsToCounterTargetSpellTemplate_WithOneTargetSpell()
    {
        var stack = new Majik.Core.Stack.Stack();

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Counterspell",
                ManaCost = "{U}{U}",
                OracleText = "Counter target spell.",
            },
            _alice, raw => raw, stack);

        def.Should().NotBeNull("Counterspell should bind via CounterTargetSpellTemplate");
        def!.TargetRequests.Should().HaveCount(1, "Counterspell targets exactly one spell on the stack");
    }

    [Fact]
    public void Counterspell_Resolves_RemovesTargetSpellFromStackToGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack();
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        bolt.SetZone(Majik.Core.Zones.ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        stack.Push(bobSpell);

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Counterspell",
                ManaCost = "{U}{U}",
                OracleText = "Counter target spell.",
            },
            _alice, raw => raw, stack);

        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bobSpell } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        stack.GetAll().Should().NotContain(bobSpell, "Counterspell removes the target spell from the stack");
        bolt.Zone.Should().Be(Majik.Core.Zones.ZoneType.Graveyard,
            "the countered spell's card ends up in its owner's graveyard (CR 701.5)");
    }

    [Fact]
    public void LightningBolt_BindsToDamageAnyTargetTemplate_WithOneTarget()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Lightning Bolt",
                ManaCost = "{R}",
                OracleText = "Lightning Bolt deals 3 damage to any target.",
            },
            _alice, raw => raw, stack: null);

        def.Should().NotBeNull("Lightning Bolt should bind via DamageAnyTargetTemplate");
        def!.TargetRequests.Should().HaveCount(1, "Lightning Bolt targets exactly one any-target");
    }

    [Fact]
    public void LightningBolt_Resolves_Deals3DamageToTargetPlayer()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Lightning Bolt",
                ManaCost = "{R}",
                OracleText = "Lightning Bolt deals 3 damage to any target.",
            },
            _alice, raw => raw, stack: null);

        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)_bob } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(17, "Lightning Bolt deals 3 damage");
    }

    [Fact]
    public void LightningBolt_Resolves_Deals3DamageToTargetCreature()
    {
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };
        grizzly.SetZone(Majik.Core.Zones.ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Lightning Bolt",
                ManaCost = "{R}",
                OracleText = "Lightning Bolt deals 3 damage to any target.",
            },
            _alice, raw => raw, stack: null);

        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)grizzly } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        grizzly.Damage.Should().BeGreaterThanOrEqualTo(3,
            "Lightning Bolt deals 3 damage to the creature (CR 119.3)");
    }
}
