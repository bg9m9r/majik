using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Misc;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Misc;

public class FogTemplateTests
{
    private static SpellBindContext Ctx(string text, ReplacementBus? bus)
        => new(new CardEntity { Name = "Fog", OracleText = text },
            new Player("A", 20), _ => _, Effects: null, Stack: null,
            Replacements: bus);

    [Fact]
    public void TryBind_NullReplacements_ReturnsNullViaCanBind()
    {
        new FogTemplate().TryBind(Ctx(
            "Prevent all combat damage that would be dealt this turn.",
            bus: null))
            .Should().BeNull();
    }

    [Fact]
    public void Rehydrate_RegistersPreventionShield()
    {
        var bus = new ReplacementBus();
        var spell = new FogTemplate().TryBind(Ctx(
            "Prevent all combat damage that would be dealt this turn.",
            bus));
        spell.Should().NotBeNull();

        // Execute the spell's effect list — simulates resolution.
        foreach (var fx in spell!.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty)))
        {
            fx.Execute();
        }

        // A combat damage intent (source is a creature) now cancels.
        var attacker = new Creature("attacker", "", 3, 3);
        var defender = new Player("D", 20);
        var intent = new DamageIntent(attacker, 3, TargetPlayer: defender);
        bus.Apply(intent).Should().BeNull("Fog shield cancels combat damage");
    }

    [Fact]
    public void Shield_DoesNotApplyToNonCombatDamage()
    {
        // Shield only matches DamageIntent whose Source is a Creature
        // (i.e., combat damage). A spell-source intent (Source is a Player
        // proxy or an Instant card) passes through unchanged.
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllCombatDamageShield());

        var spellSource = new Instant("Lightning Bolt", "{R}");
        var target = new Player("V", 20);
        var intent = new DamageIntent(spellSource, 3, TargetPlayer: target);
        bus.Apply(intent).Should().NotBeNull("non-creature source isn't combat damage");
    }

    [Fact]
    public void Shield_ExpiresAtEndOfTurn()
    {
        var bus = new ReplacementBus();
        bus.Register<DamageIntent>(new PreventAllCombatDamageShield());

        // Combat damage intent cancelled while shield active.
        var atk = new Creature("a", "", 2, 2);
        bus.Apply(new DamageIntent(atk, 2, TargetPlayer: new Player("P", 20)))
            .Should().BeNull();

        // After end-of-turn cleanup, shield drops; same intent passes.
        bus.ExpireEndOfTurn();
        bus.Apply(new DamageIntent(atk, 2, TargetPlayer: new Player("P", 20)))
            .Should().NotBeNull();
    }
}
