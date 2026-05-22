using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates.Templates.Bespoke;

public class DamagePreventionTemplateTests
{
    private static SpellBindContext Ctx(string text, ReplacementBus? bus, Player? caster = null)
        => new(new CardEntity { Name = "X", OracleText = text },
            caster ?? new Player("A", 20), _ => _, Effects: null, Stack: null,
            Replacements: bus);

    // -------- Family A: Prevent the next N to any target --------

    [Theory]
    [InlineData("Prevent the next 3 damage that would be dealt to any target this turn.")]
    [InlineData("Prevent the next 4 damage that would be dealt to any target this turn.")]
    [InlineData("Prevent the next 7 damage that would be dealt to any target this turn.")]
    public void PreventNextN_BindsForFamilyTexts(string oracle)
    {
        new PreventNextNDamageToAnyTargetTemplate().TryBind(Ctx(oracle, new ReplacementBus()))
            .Should().NotBeNull();
    }

    [Fact]
    public void PreventNextN_GatesOnReplacementsBus()
    {
        new PreventNextNDamageToAnyTargetTemplate().TryBind(Ctx(
            "Prevent the next 3 damage that would be dealt to any target this turn.",
            bus: null))
            .Should().BeNull();
    }

    [Fact]
    public void PreventNextN_DoesNotBindWhenTargetIsACreatureOnly()
    {
        // "target creature" not "any target" — out of family.
        new PreventNextNDamageToAnyTargetTemplate().TryBind(Ctx(
            "Prevent the next 3 damage that would be dealt to target creature this turn.",
            new ReplacementBus()))
            .Should().BeNull();
    }

    [Fact]
    public void PreventNextN_Rehydrate_RegistersShieldWithCorrectPool()
    {
        var bus = new ReplacementBus();
        var spell = new PreventNextNDamageToAnyTargetTemplate().TryBind(Ctx(
            "Prevent the next 4 damage that would be dealt to any target this turn.",
            bus));
        spell.Should().NotBeNull();

        foreach (var fx in spell!.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null, Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty)))
        {
            fx.Execute();
        }

        // Resolve: a 5-damage intent gets 4 absorbed, 1 passes through.
        var attacker = new Creature("a", "", 5, 5);
        var defender = new Player("D", 20);
        var result = bus.Apply(new DamageIntent(attacker, 5, TargetPlayer: defender));
        result.Should().NotBeNull();
        result!.Amount.Should().Be(1);
    }

    // -------- Family B: Prevent all damage to you and permanents/creatures you control --------

    [Theory]
    [InlineData("Prevent all damage that would be dealt to you and permanents you control this turn.")]
    [InlineData("Prevent all damage that would be dealt to you and creatures you control this turn.")]
    public void PreventAllToYouAndPermanents_BindsForFamilyTexts(string oracle)
    {
        new PreventAllDamageToYouAndPermanentsTemplate().TryBind(Ctx(oracle, new ReplacementBus()))
            .Should().NotBeNull();
    }

    [Fact]
    public void PreventAllToYouAndPermanents_GatesOnReplacementsBus()
    {
        new PreventAllDamageToYouAndPermanentsTemplate().TryBind(Ctx(
            "Prevent all damage that would be dealt to you and permanents you control this turn.",
            bus: null))
            .Should().BeNull();
    }

    [Fact]
    public void PreventAllToYouAndPermanents_DoesNotBindTrailingRider()
    {
        // Channel Harm-style — trailing "by sources you don't control" / rider.
        // The lead clause has extra qualifier so the strict pattern rejects it.
        new PreventAllDamageToYouAndPermanentsTemplate().TryBind(Ctx(
            "Prevent all damage that would be dealt to you and permanents you control this turn by sources you don't control.",
            new ReplacementBus()))
            .Should().BeNull();
    }

    [Fact]
    public void PreventAllToYouAndPermanents_Rehydrate_BlocksDamageToCasterAndPermanents()
    {
        var bus = new ReplacementBus();
        var caster = new Player("A", 20);
        var opp = new Player("B", 20);
        var spell = new PreventAllDamageToYouAndPermanentsTemplate().TryBind(Ctx(
            "Prevent all damage that would be dealt to you and permanents you control this turn.",
            bus, caster));
        spell.Should().NotBeNull();

        foreach (var fx in spell!.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null, Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty)))
        {
            fx.Execute();
        }

        var src = new Creature("src", "", 3, 3);
        var myCreature = new Creature("mine", "", 2, 2) { Owner = caster, Controller = caster };
        var oppCreature = new Creature("yours", "", 2, 2) { Owner = opp, Controller = opp };

        // Damage to caster: prevented.
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: caster)).Should().BeNull();
        // Damage to caster's creature: prevented.
        bus.Apply(new DamageIntent(src, 2, TargetCreature: myCreature)).Should().BeNull();
        // Damage to opponent's creature: passes.
        bus.Apply(new DamageIntent(src, 2, TargetCreature: oppCreature)).Should().NotBeNull();
        // Damage to opponent (player): passes.
        bus.Apply(new DamageIntent(src, 3, TargetPlayer: opp)).Should().NotBeNull();
    }

    // -------- Family C: Prevent all combat damage to players --------

    [Theory]
    [InlineData("Prevent all combat damage that would be dealt to players this turn.")]
    public void PreventAllCombatDamageToPlayers_Binds(string oracle)
    {
        new PreventAllCombatDamageToPlayersTemplate().TryBind(Ctx(oracle, new ReplacementBus()))
            .Should().NotBeNull();
    }

    [Fact]
    public void PreventAllCombatDamageToPlayers_GatesOnReplacementsBus()
    {
        new PreventAllCombatDamageToPlayersTemplate().TryBind(Ctx(
            "Prevent all combat damage that would be dealt to players this turn.",
            bus: null))
            .Should().BeNull();
    }

    [Fact]
    public void PreventAllCombatDamageToPlayers_DoesNotBindFog()
    {
        // Plain Fog — no "to players" filter, belongs to FogTemplate.
        new PreventAllCombatDamageToPlayersTemplate().TryBind(Ctx(
            "Prevent all combat damage that would be dealt this turn.",
            new ReplacementBus()))
            .Should().BeNull();
    }

    [Fact]
    public void PreventAllCombatDamageToPlayers_Rehydrate_PreventsOnlyPlayerBoundCombatDamage()
    {
        var bus = new ReplacementBus();
        var spell = new PreventAllCombatDamageToPlayersTemplate().TryBind(Ctx(
            "Prevent all combat damage that would be dealt to players this turn.",
            bus));
        spell.Should().NotBeNull();

        foreach (var fx in spell!.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null, Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty)))
        {
            fx.Execute();
        }

        var attacker = new Creature("a", "", 4, 4);
        var blocker = new Creature("b", "", 2, 2);
        var defender = new Player("D", 20);

        bus.Apply(new DamageIntent(attacker, 4, TargetPlayer: defender)).Should().BeNull();
        bus.Apply(new DamageIntent(attacker, 4, TargetCreature: blocker))
            .Should().NotBeNull("creature-bound combat damage still resolves");
    }

    // -------- End-to-end: real cards bind via OracleSpellBinder.Registry --------

    [Theory]
    [InlineData("Hold at Bay", "Prevent the next 7 damage that would be dealt to any target this turn.")]
    [InlineData("Mending Hands", "Prevent the next 4 damage that would be dealt to any target this turn.")]
    [InlineData("Shieldmate's Blessing", "Prevent the next 3 damage that would be dealt to any target this turn.")]
    [InlineData("Endure", "Prevent all damage that would be dealt to you and permanents you control this turn.")]
    [InlineData("Safe Passage", "Prevent all damage that would be dealt to you and creatures you control this turn.")]
    [InlineData("Commencement of Festivities", "Prevent all combat damage that would be dealt to players this turn.")]
    [InlineData("Defend the Hearth", "Prevent all combat damage that would be dealt to players this turn.")]
    public void OracleBinder_BindsRealCards(string cardName, string oracleText)
    {
        var bus = new ReplacementBus();
        var entity = new CardEntity { Name = cardName, OracleText = oracleText };
        var caster = new Player("A", 20);

        var spell = Majik.Core.CardData.OracleSpellBinder.Bind(
            entity, caster, _ => _, effects: null, stack: null, replacements: bus);

        spell.Should().NotBeNull($"{cardName} should bind via the registry");
    }
}
