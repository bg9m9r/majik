using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class OracleSpellBinderUntapLifeTokenTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void UntapTargetCreature_Untaps()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bear.Tap();

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Untap Spell", ManaCost = "{U}",
              OracleText = "Untap target creature." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, bear);
        bear.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void UntapTargetPermanent_Untaps()
    {
        var bear = new Creature("X", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        bear.Tap();
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Twiddle", ManaCost = "{U}",
              OracleText = "Untap target permanent." },
            _alice, raw => raw, null);
        Resolve(def!, bear);
        bear.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void YouGainN_GainsLifeForCaster()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Healing Salve", ManaCost = "{W}",
              OracleText = "You gain 3 life." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();
        _alice.LifeTotal.Should().Be(23);
    }

    [Fact]
    public void CreateTokens_AddsTokensToBattlefield()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Raise the Alarm", ManaCost = "{1}{W}",
              OracleText = "Create two 1/1 white Soldier creature tokens." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken).ToList();
        tokens.Should().HaveCount(2);
        tokens.All(t => t.BasePower == 1 && t.BaseToughness == 1).Should().BeTrue();
    }

    [Fact]
    public void CreateToken_Singular_AddsOne()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "X", ManaCost = "{2}{G}",
              OracleText = "Create a 3/3 green Beast creature token." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>().Where(c => c.IsToken).ToList();
        tokens.Should().HaveCount(1);
        tokens[0].BasePower.Should().Be(3);
        tokens[0].BaseToughness.Should().Be(3);
    }

    private void Resolve(SpellDefinition def, object target)
    {
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { target } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
