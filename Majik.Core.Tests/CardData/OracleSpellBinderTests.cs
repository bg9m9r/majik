using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData;

public class OracleSpellBinderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Damage_AnyTarget_BuildsDamageSpell()
    {
        var def = Bind("Lightning Bolt", "{R}", "Lightning Bolt deals 3 damage to any target.");

        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
        Resolve(def, target: _bob);

        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void Damage_TargetCreatureOrPlayer_BuildsDamageSpell()
    {
        var def = Bind("Shock", "{R}", "Shock deals 2 damage to any target.");
        def.Should().NotBeNull();
        Resolve(def!, target: _bob);
        _bob.LifeTotal.Should().Be(18);
    }

    [Fact]
    public void Damage_PlayerOnly_BuildsPlayerDamageSpell()
    {
        var def = Bind("Lava Spike", "{R}", "Lava Spike deals 3 damage to target player or planeswalker.");
        def.Should().NotBeNull();
        Resolve(def!, target: _bob);
        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void Draw_NCards_BuildsDrawSpell()
    {
        SeedLibrary(_alice, 5);
        var def = Bind("Divination", "{2}{U}", "Draw two cards.");
        def.Should().NotBeNull();
        Resolve(def!, target: null, caster: _alice);
        _alice.Zones.Hand.Count.Should().Be(2);
    }

    [Fact]
    public void TargetPlayerDiscards_BuildsDiscardSpell()
    {
        SeedHand(_bob, 5);
        var def = Bind("Mind Rot", "{2}{B}", "Target player discards two cards.");
        def.Should().NotBeNull();
        Resolve(def!, target: _bob);
        _bob.Zones.Hand.Count.Should().Be(3);
    }

    [Fact]
    public void DestroyTargetCreature_BuildsDestroySpell()
    {
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _bob };
        bear.Zone = Majik.Core.Zones.ZoneType.Battlefield;
        _bob.Zones.Battlefield.AddCard(bear);

        var def = Bind("Doom Blade", "{1}{B}", "Destroy target nonblack creature.");
        def.Should().NotBeNull();
        Resolve(def!, target: bear);

        bear.Zone.Should().Be(Majik.Core.Zones.ZoneType.Graveyard);
    }

    [Fact]
    public void CounterTargetSpell_RemovesFromStack()
    {
        var stack = new Majik.Core.Stack.Stack();
        var bolt = new Instant("Bolt", "R") { Owner = _bob };
        bolt.Zone = Majik.Core.Zones.ZoneType.Stack;
        var bobSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        stack.Push(bobSpell);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Counterspell", ManaCost = "{U}{U}", OracleText = "Counter target spell." },
            _alice, raw => raw, stack);

        def.Should().NotBeNull();
        Resolve(def!, target: bobSpell);

        stack.GetAll().Should().NotContain(bobSpell);
        bolt.Zone.Should().Be(Majik.Core.Zones.ZoneType.Graveyard);
    }

    [Fact]
    public void UnrecognizedText_ReturnsNull()
    {
        var def = Bind("Mystery", "{1}", "This card does something weird that we don't pattern-match.");
        def.Should().BeNull();
    }

    // ---- Helpers ----

    private SpellDefinition? Bind(string name, string cost, string oracle) =>
        OracleSpellBinder.Bind(
            new CardEntity { Name = name, ManaCost = cost, OracleText = oracle },
            _alice, raw => raw, stack: null);

    private void Resolve(SpellDefinition def, object? target, Player? caster = null)
    {
        var targets = target == null
            ? Array.Empty<IReadOnlyList<object>>()
            : new IReadOnlyList<object>[] { new[] { target } };
        var chosen = new ChosenSpellParams(null, null, targets, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Card($"L{i}", "");
            c.Owner = p; c.Zone = Majik.Core.Zones.ZoneType.Library;
            p.Zones.Library.AddCard(c);
        }
    }

    private void SeedHand(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Card($"H{i}", "");
            c.Owner = p; c.Zone = Majik.Core.Zones.ZoneType.Hand;
            p.Zones.Hand.AddCard(c);
        }
    }
}
