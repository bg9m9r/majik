using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Land = Majik.Core.Cards.Land;

public class OracleSpellBinderTutorXExileTests
{
    private readonly Player _alice = new("Alice", 20);

    // ---------- Tutor ----------

    [Fact]
    public void SearchYourLibraryForLand_MovesFirstMatchToHand()
    {
        var mtn = new Land("Mountain") { Owner = _alice, Zone = ZoneType.Library };
        var forest = new Land("Forest") { Owner = _alice, Zone = ZoneType.Library };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(mtn);
        _alice.Zones.Library.AddCard(forest);
        _alice.Zones.Library.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Lay of the Land", ManaCost = "{G}",
              OracleText = "Search your library for a basic land card, reveal it, put it into your hand, then shuffle." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null, new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().ContainSingle(c => c.Name == "Mountain");
        _alice.Zones.Library.GetCards().Should().NotContain(mtn);
    }

    [Fact]
    public void SearchYourLibraryForCreature_MovesFirstCreatureToHand()
    {
        var mtn = new Land("Mountain") { Owner = _alice, Zone = ZoneType.Library };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(mtn);
        _alice.Zones.Library.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "X", ManaCost = "1G",
              OracleText = "Search your library for a creature card, reveal it, put it into your hand, then shuffle." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null, new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bear);
    }

    // ---------- Variable X damage ----------

    [Fact]
    public void DealsXDamageToAnyTarget_UsesXValue()
    {
        var bob = new Player("Bob", 20);
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Demonfire", ManaCost = "{X}{R}",
              OracleText = "Demonfire deals X damage to any target." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.HasVariableX.Should().BeTrue();

        var chosen = new ChosenSpellParams(null, X: 5,
            new[] { new object[] { bob } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bob.LifeTotal.Should().Be(15);
    }

    // ---------- Exile ----------

    [Fact]
    public void ExileTargetCreature_MovesToExile()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Path to Exile", ManaCost = "{W}",
              OracleText = "Exile target creature." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void ExileTargetPermanent_MovesToExile()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "X", ManaCost = "2W",
              OracleText = "Exile target permanent." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Zone.Should().Be(ZoneType.Exile);
    }
}
