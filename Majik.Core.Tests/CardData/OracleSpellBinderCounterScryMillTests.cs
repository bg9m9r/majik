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

public class OracleSpellBinderCounterScryMillTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ManaLeak_BindsAsCounterUnless()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Mana Leak", ManaCost = "{1}{U}",
              OracleText = "Counter target spell unless its controller pays {3}." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
    }

    [Fact]
    public void Mill_MovesTopOfLibraryToGraveyard()
    {
        for (var i = 0; i < 5; i++)
        {
            var c = new Land($"L{i}") { Owner = _bob, Zone = ZoneType.Library };
            _bob.Zones.Library.AddCard(c);
        }

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Mill Spell", ManaCost = "{1}{U}",
              OracleText = "Target player mills 3 cards." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3);
        _bob.Zones.Library.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Scry_BindsWithNoTargets()
    {
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Preordain", ManaCost = "{U}",
              OracleText = "Scry 2, then draw a card." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().BeEmpty();
    }
}
