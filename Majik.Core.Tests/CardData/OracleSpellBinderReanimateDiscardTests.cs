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
using Instant = Majik.Core.Cards.Instant;

public class OracleSpellBinderReanimateDiscardTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RaiseDead_ReturnsCreatureFromGraveyardToHand()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Raise Dead", ManaCost = "{B}",
              OracleText = "Return target creature card from your graveyard to your hand." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void RegrowthAny_ReturnsAnyCardFromGraveyard()
    {
        var instant = new Instant("Bolt", "R")
        { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(instant);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Regrowth", ManaCost = "{1}{G}",
              OracleText = "Return target card from your graveyard to your hand." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { instant } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();
        instant.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void TargetOpponentDiscards_TargetPlayerLosesACard()
    {
        var c1 = new Creature("X", "1G", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        var c2 = new Creature("Y", "1G", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(c1);
        _bob.Zones.Hand.AddCard(c2);

        // Existing template covers "Target player discards N cards" already
        // via Discard regex. Verify it binds + works for opponent.
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Mind Rot", ManaCost = "{2}{B}",
              OracleText = "Target player discards two cards." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _bob.Zones.Hand.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
    }
}
