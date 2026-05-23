using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Spell Pierce — Instant {U}. "Counter target noncreature spell unless its
/// controller pays {2}." Should bind through the existing CounterUnlessPay
/// template family (no new factory) once the regex accepts the optional
/// "noncreature" type qualifier between "target" and "spell". Resolution
/// honors:
///   - Type rider — creature spells are not legal targets (CR 608.2b: the
///     effect does nothing if the target is illegal at resolution).
///   - Pay rider — if the target spell's controller has {2} generic mana
///     available, it's spent and the spell resolves; otherwise the spell
///     is countered (CR 701.5 / CR 118.4).
/// </summary>
public class SpellPierceTests
{
    private const string SpellPierceOracle =
        "Counter target noncreature spell unless its controller pays {2}.";

    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SpellPierce_BindsViaCounterUnlessPayTemplate_WithOneTargetSpell()
    {
        var stack = new Majik.Core.Stack.Stack();

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Spell Pierce",
                ManaCost = "{U}",
                OracleText = SpellPierceOracle,
            },
            _alice, raw => raw, stack);

        def.Should().NotBeNull(
            "Spell Pierce's oracle text must match the broadened CounterUnlessPay regex");
        def!.TargetRequests.Should().HaveCount(1,
            "Spell Pierce targets exactly one spell on the stack");
    }

    [Fact]
    public void SpellPierce_OpponentCannotPay_CountersTheNoncreatureSpell()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Bob casts a noncreature spell (Lightning Bolt) onto the stack.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        bolt.SetZone(Majik.Core.Zones.ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        stack.Push(bobSpell);
        // Bob has no mana floating — he can't pay {2}.

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Spell Pierce",
                ManaCost = "{U}",
                OracleText = SpellPierceOracle,
            },
            _alice, raw => raw, stack);

        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bobSpell } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        stack.GetAll().Should().NotContain(bobSpell,
            "controller can't pay {2}, so Spell Pierce counters the target spell");
        bolt.Zone.Should().Be(Majik.Core.Zones.ZoneType.Graveyard,
            "the countered spell goes to its owner's graveyard (CR 701.5)");
    }

    [Fact]
    public void SpellPierce_OpponentPaysTwo_SpellResolves()
    {
        var stack = new Majik.Core.Stack.Stack();

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        bolt.SetZone(Majik.Core.Zones.ZoneType.Stack);
        var bobSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        stack.Push(bobSpell);

        // Bob floats {2} so he can pay Spell Pierce's rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Spell Pierce",
                ManaCost = "{U}",
                OracleText = SpellPierceOracle,
            },
            _alice, raw => raw, stack);

        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)bobSpell } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        stack.GetAll().Should().Contain(bobSpell,
            "controller paid {2}, so Spell Pierce's counter doesn't trigger and the spell stays on the stack to resolve normally");
        bolt.Zone.Should().Be(Majik.Core.Zones.ZoneType.Stack,
            "uncountered spell remains in its current zone until normal resolution");
    }

    [Fact]
    public void SpellPierce_TargetIsCreatureSpell_EffectDoesNothing()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Bob casts a creature spell — an illegal target for Spell Pierce.
        var grizzly = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _bob };
        grizzly.SetZone(Majik.Core.Zones.ZoneType.Stack);
        var creatureSpell = new Majik.Core.Spells.Spell(grizzly, _bob);
        stack.Push(creatureSpell);

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Spell Pierce",
                ManaCost = "{U}",
                OracleText = SpellPierceOracle,
            },
            _alice, raw => raw, stack);

        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(
            null, null,
            new IReadOnlyList<object>[] { new[] { (object)creatureSpell } },
            ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        stack.GetAll().Should().Contain(creatureSpell,
            "creature spell is an illegal target for Spell Pierce — effect does nothing (CR 608.2b)");
        grizzly.Zone.Should().Be(Majik.Core.Zones.ZoneType.Stack,
            "the creature spell is untouched by Spell Pierce's effect");
    }

    [Fact]
    public void SpellPierce_ExtractsParams_NEqualsTwo_AndNoncreatureQualifier()
    {
        var template = new Majik.Core.CardData.SpellTemplates.Templates.Counter
            .CounterUnlessPayTemplate();
        var @params = template.TryExtractParams(SpellPierceOracle);

        @params.Should().NotBeNull(
            "the broadened regex must match Spell Pierce's oracle text");
        @params!["n"].Should().Be("2",
            "Spell Pierce's rider charges {2}");
        @params["q"].Should().Be("noncreature",
            "the type qualifier between 'target' and 'spell' is captured as a param so Rehydrate can route to the typed factory");
    }
}
