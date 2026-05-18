using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

public class OracleSpellBinderExtraTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TargetPlayerGainsLife_Binds()
    {
        _alice.LoseLife(10);
        var def = Bind("Healing Salve", "{W}",
            "Choose one — Target player gains 3 life; or prevent the next 3 damage that would be dealt to any target this turn.");
        def.Should().NotBeNull();

        Resolve(def!, _alice);

        _alice.LifeTotal.Should().Be(13);
    }

    [Fact]
    public void CounterTargetNoncreatureSpell_BindsAndOnlyCountersNoncreature()
    {
        var stack = new Majik.Core.Stack.Stack();
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Negate", ManaCost = "{1}{U}", OracleText = "Counter target noncreature spell." },
            _alice, raw => raw, stack)!;

        var bolt = new Instant("Bolt", "R") { Owner = _bob };
        bolt.Zone = ZoneType.Stack;
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        stack.Push(boltSpell);

        Resolve(def, boltSpell);

        stack.GetAll().Should().NotContain(boltSpell);
        bolt.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void CounterTargetNoncreature_LeavesCreatureSpellAlone()
    {
        var stack = new Majik.Core.Stack.Stack();
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Negate", ManaCost = "{1}{U}", OracleText = "Counter target noncreature spell." },
            _alice, raw => raw, stack)!;

        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob };
        bear.Zone = ZoneType.Stack;
        var bearSpell = new Majik.Core.Spells.Spell(bear, _bob);
        stack.Push(bearSpell);

        Resolve(def, bearSpell);

        // Creature spell stays on stack — counter spell did nothing.
        stack.GetAll().Should().Contain(bearSpell);
    }

    [Fact]
    public void CounterTargetCreatureSpell_OppositeFilter()
    {
        var stack = new Majik.Core.Stack.Stack();
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Essence Scatter", ManaCost = "{1}{U}", OracleText = "Counter target creature spell." },
            _alice, raw => raw, stack)!;

        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob };
        bear.Zone = ZoneType.Stack;
        var bearSpell = new Majik.Core.Spells.Spell(bear, _bob);
        stack.Push(bearSpell);

        Resolve(def, bearSpell);

        stack.GetAll().Should().NotContain(bearSpell);
    }

    private SpellDefinition? Bind(string name, string cost, string oracle) =>
        OracleSpellBinder.Bind(
            new CardEntity { Name = name, ManaCost = cost, OracleText = oracle },
            _alice, raw => raw, null);

    private void Resolve(SpellDefinition def, object? target)
    {
        var targets = target == null
            ? System.Array.Empty<IReadOnlyList<object>>()
            : new IReadOnlyList<object>[] { new[] { target } };
        var chosen = new ChosenSpellParams(null, null, targets, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
