using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class OracleSpellBinderLongTailTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void GiantGrowth_Plus3Plus3_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice, ActiveEffects = svc };
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Giant Growth", ManaCost = "{G}", OracleText = "Target creature gets +3/+3 until end of turn." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        Resolve(def!, bear);

        bear.Power.Should().Be(5);
        bear.Toughness.Should().Be(5);

        svc.ExpireEndOfTurn();
        bear.Power.Should().Be(2);
    }

    [Fact]
    public void GrantsFlying_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice, ActiveEffects = svc };
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Crowned Ceratok", ManaCost = "{1}{G}", OracleText = "Target creature gains flying until end of turn." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        Resolve(def!, bear);

        CombatAbilities.HasFlying(bear).Should().BeTrue();

        svc.ExpireEndOfTurn();
        CombatAbilities.HasFlying(bear).Should().BeFalse();
    }

    [Fact]
    public void GrantsFirstStrike_TwoWordKeyword()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Controller = _alice, ActiveEffects = svc };
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "X", ManaCost = "W", OracleText = "Target creature gains first strike until end of turn." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, bear);
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();
    }

    [Fact]
    public void EachOpponentLosesLife_BindsButEffectIsPlaceholder()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "X", ManaCost = "1B", OracleText = "Each opponent loses 2 life." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().BeEmpty();
    }

    [Fact]
    public void EachPlayerDraws_Binds()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Howling Mine", ManaCost = "2", OracleText = "Each player draws a card." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().BeEmpty();
    }

    private void Resolve(SpellDefinition def, object target)
    {
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { target } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
