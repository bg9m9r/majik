using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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

    [Fact]
    public void MillSelf_NoTarget_CasterMillsOwnLibrary()
    {
        for (var i = 0; i < 3; i++)
        {
            var c = new Land($"L{i}") { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(c);
        }

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Self Mill", ManaCost = "{U}",
              OracleText = "Mill 2 cards." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().BeEmpty();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2);
        _alice.Zones.Library.GetCards().Should().HaveCount(1);

        // bob's library must be untouched
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void SurveilSelf_DefaultDecision_AllToGraveyard()
    {
        for (var i = 0; i < 3; i++)
        {
            var c = new Land($"L{i}") { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(c);
        }

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Surveil Spell", ManaCost = "{U}",
              OracleText = "Surveil 2." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().BeEmpty();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Surveil 2 with default "all to graveyard" decision: top 2 go to GY,
        // 1 remaining card stays in library.
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2);
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void ScrySelf_DefaultDecision_AllToBottom()
    {
        // Set up alice with 3 cards in library: A (top), B, C (bottom).
        var cardA = new Land("A") { Owner = _alice, Zone = ZoneType.Library };
        var cardB = new Land("B") { Owner = _alice, Zone = ZoneType.Library };
        var cardC = new Land("C") { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(cardA);
        _alice.Zones.Library.AddCard(cardB);
        _alice.Zones.Library.AddCard(cardC);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Scry Spell", ManaCost = "{U}",
              OracleText = "Scry 2." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().BeEmpty();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Scry 2 with default all-to-bottom: top 2 (A, B) go to bottom;
        // remaining library order is C (top), then A, B on bottom.
        var library = _alice.Zones.Library.GetCards().ToList();
        library.Should().HaveCount(3);
        library[0].Should().BeSameAs(cardC);
        library[1].Should().BeSameAs(cardA);
        library[2].Should().BeSameAs(cardB);
        // Nothing milled to graveyard.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void EachOpponentMills_MillsAllOpponents()
    {
        // Set up bob with 5 cards in library.
        for (var i = 0; i < 5; i++)
        {
            var c = new Land($"BL{i}") { Owner = _bob, Zone = ZoneType.Library };
            _bob.Zones.Library.AddCard(c);
        }
        // Alice (caster) also has cards; they should NOT be milled.
        for (var i = 0; i < 5; i++)
        {
            var c = new Land($"AL{i}") { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(c);
        }

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Opponent Mill", ManaCost = "{2}{B}",
              OracleText = "Each opponent mills 2 cards." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().BeEmpty();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Bob (opponent) milled 2.
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
        _bob.Zones.Library.GetCards().Should().HaveCount(3);
        // Alice (caster) untouched.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(5);
    }

    [Fact]
    public void EachPlayerMills_MillsCasterAndOpponents()
    {
        // Set up both players with 5 cards.
        for (var i = 0; i < 5; i++)
        {
            var ac = new Land($"AL{i}") { Owner = _alice, Zone = ZoneType.Library };
            _alice.Zones.Library.AddCard(ac);
            var bc = new Land($"BL{i}") { Owner = _bob, Zone = ZoneType.Library };
            _bob.Zones.Library.AddCard(bc);
        }

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Each Player Mill", ManaCost = "{3}{B}",
              OracleText = "Each player mills 2 cards." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().BeEmpty();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        // Both players milled 2.
        _alice.Zones.Graveyard.GetCards().Should().HaveCount(2);
        _alice.Zones.Library.GetCards().Should().HaveCount(3);
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
        _bob.Zones.Library.GetCards().Should().HaveCount(3);
    }
}
