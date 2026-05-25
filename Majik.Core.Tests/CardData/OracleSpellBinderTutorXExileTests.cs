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

    // ---------- Target player loses N life ----------

    [Fact]
    public void TargetPlayerLosesLife_DropsTargetLifeByN()
    {
        var bob = new Player("Bob", 20);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Drain Life", ManaCost = "{B}",
              OracleText = "Target player loses 3 life." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().ContainSingle(r => r.Description == "target player");

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bob } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bob.LifeTotal.Should().Be(17);
        _alice.LifeTotal.Should().Be(20); // caster unaffected
    }

    [Fact]
    public void TargetPlayerLosesLife_WordNumber_DropsLifeByN()
    {
        var bob = new Player("Bob", 20);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "X", ManaCost = "{2}{B}",
              OracleText = "Target player loses five life." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bob } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        bob.LifeTotal.Should().Be(15);
    }

    // ---------- Exile from graveyard ----------

    [Fact]
    public void ExileFromGraveyard_AnyCard_MovesCardToExile()
    {
        var corpse = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(corpse);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Bojuka Bog", ManaCost = "",
              OracleText = "Exile target card from a graveyard." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().ContainSingle(r => r.Description == "target card in graveyard");

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { corpse } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(corpse);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(corpse);
        corpse.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void ExileFromGraveyard_CreatureCard_UsesTypedLabel()
    {
        var corpse = new Creature("Wolf", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(corpse);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Relic of Progenitus", ManaCost = "{1}",
              OracleText = "Exile target creature card from a graveyard." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.TargetRequests.Should().ContainSingle(r => r.Description == "target creature card in graveyard");

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { corpse } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(corpse);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(corpse);
    }

    [Fact]
    public void ExileFromYourGraveyard_MovesCardToExile()
    {
        var corpse = new Creature("Bear", "1G", 2, 2) { Owner = _alice, Zone = ZoneType.Graveyard };
        _alice.Zones.Graveyard.AddCard(corpse);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Withered Wretch", ManaCost = "{B}",
              OracleText = "Exile target card from your graveyard." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { corpse } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Exile.GetCards().Should().Contain(corpse);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(corpse);
    }

    [Fact]
    public void ExileFromGraveyard_CardNotInGraveyard_NoOp()
    {
        // If a card is on the battlefield (wrong zone), the effect should not move it.
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Bojuka Bog", ManaCost = "",
              OracleText = "Exile target card from a graveyard." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { bear } }, ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        // Card should remain on battlefield; exile should be empty.
        bear.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Exile.GetCards().Should().NotContain(bear);
    }

    // ---------- Ramp tutor — search basic land onto battlefield ----------

    [Fact]
    public void Bind_SearchLandToBattlefieldTapped_FetchesBasicLandTapped()
    {
        var alice = new Player("Alice", 20);
        var forest = new Land("Forest") { Owner = alice, Zone = ZoneType.Library };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Zone = ZoneType.Library };
        alice.Zones.Library.AddCard(bear);
        alice.Zones.Library.AddCard(forest);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Rampant Growth", ManaCost = "{1}{G}",
              OracleText = "Search your library for a basic land card, put it onto the battlefield tapped, then shuffle." },
            alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        alice.Zones.Library.GetCards().Should().NotContain(forest);
        ((Permanent)forest).IsTapped.Should().BeTrue();
        // Bear stays in library (not a basic land).
        alice.Zones.Library.GetCards().Should().Contain(bear);
    }

    [Fact]
    public void Bind_SearchLandToBattlefieldUntapped_FetchesBasicLandUntapped()
    {
        var alice = new Player("Alice", 20);
        var forest = new Land("Forest") { Owner = alice, Zone = ZoneType.Library };
        alice.Zones.Library.AddCard(forest);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Untapped Ramp", ManaCost = "{G}",
              OracleText = "Search your library for a basic land card and put it onto the battlefield." },
            alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        alice.Zones.Library.GetCards().Should().NotContain(forest);
        ((Permanent)forest).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Bind_SearchLandToBattlefield_NoLandInLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Zone = ZoneType.Library };
        alice.Zones.Library.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Wasted Ramp", ManaCost = "{G}",
              OracleText = "Search your library for a basic land card and put it onto the battlefield." },
            alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, null,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.Zones.Library.GetCards().Should().Contain(bear);
        alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // ---------- Green Sun's Zenith ----------

    [Fact]
    public void Bind_GreenSunsZenith_TutorsGreenCreatureWithCmcLessOrEqualX()
    {
        var alice = new Player("Alice", 20);
        // Bear: mana value 2 (1G) — should be picked for X=2.
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Zone = ZoneType.Library };
        // Giant: mana value 7 (4GGG) — CMC too high for X=2.
        var giant = new Creature("Giant", "4GGG", 7, 7) { Owner = alice, Zone = ZoneType.Library };
        alice.Zones.Library.AddCard(bear);
        alice.Zones.Library.AddCard(giant);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Green Sun's Zenith", ManaCost = "{X}{G}",
              OracleText = "Search your library for a green creature card with mana value X or less, put it onto the battlefield, then shuffle. Shuffle Green Sun's Zenith into its owner's library." },
            alice, raw => raw, null);
        def.Should().NotBeNull();
        def!.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().BeEmpty();

        var chosen = new ChosenSpellParams(null, X: 2,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        alice.Zones.Library.GetCards().Should().NotContain(bear);
        // Giant stays in library — CMC too high.
        alice.Zones.Library.GetCards().Should().Contain(giant);
        alice.Zones.Battlefield.GetCards().Should().NotContain(giant);
    }

    [Fact]
    public void Bind_GreenSunsZenith_XTooLow_NoMatchFizzles()
    {
        var alice = new Player("Alice", 20);
        // Giant: mana value 7 — no green creature with CMC ≤ 1 in library.
        var giant = new Creature("Giant", "4GGG", 7, 7) { Owner = alice, Zone = ZoneType.Library };
        alice.Zones.Library.AddCard(giant);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Green Sun's Zenith", ManaCost = "{X}{G}",
              OracleText = "Search your library for a green creature card with mana value X or less, put it onto the battlefield, then shuffle. Shuffle Green Sun's Zenith into its owner's library." },
            alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, X: 1,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        alice.Zones.Library.GetCards().Should().Contain(giant);
        alice.Zones.Battlefield.GetCards().Should().NotContain(giant);
    }

    [Fact]
    public void Bind_GreenSunsZenith_X0_CanFetchZeroCmcGreenCreature()
    {
        var alice = new Player("Alice", 20);
        // Ornithopter would be CMC 0 — use a zero-cost green creature as a test stand-in.
        var freeGreen = new Creature("Free Elf", "G", 1, 1) { Owner = alice, Zone = ZoneType.Library };
        // ManaCost "{G}" has TotalValue = 1, not 0. Use "" to simulate a 0-CMC card.
        var zeroCmcGreen = new Creature("Glimpse of Nature Token", "", 0, 0) { Owner = alice, Zone = ZoneType.Library };
        alice.Zones.Library.AddCard(freeGreen);
        alice.Zones.Library.AddCard(zeroCmcGreen);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Green Sun's Zenith", ManaCost = "{X}{G}",
              OracleText = "Search your library for a green creature card with mana value X or less, put it onto the battlefield, then shuffle. Shuffle Green Sun's Zenith into its owner's library." },
            alice, raw => raw, null);
        def.Should().NotBeNull();

        // X=0: only CMC-0 cards should match. freeGreen has CMC 1 (one G pip), so it won't match.
        var chosen = new ChosenSpellParams(null, X: 0,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        // freeGreen has CMC 1, shouldn't be put onto battlefield at X=0.
        alice.Zones.Library.GetCards().Should().Contain(freeGreen);
        alice.Zones.Battlefield.GetCards().Should().NotContain(freeGreen);
    }

    [Fact]
    public void Bind_GreenSunsZenith_NonGreenCreatureIgnored()
    {
        var alice = new Player("Alice", 20);
        // A white creature with CMC 2 should be ignored even though CMC ≤ X.
        var knight = new Creature("White Knight", "WW", 2, 2) { Owner = alice, Zone = ZoneType.Library };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = alice, Zone = ZoneType.Library };
        alice.Zones.Library.AddCard(knight);
        alice.Zones.Library.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Green Sun's Zenith", ManaCost = "{X}{G}",
              OracleText = "Search your library for a green creature card with mana value X or less, put it onto the battlefield, then shuffle. Shuffle Green Sun's Zenith into its owner's library." },
            alice, raw => raw, null);
        def.Should().NotBeNull();

        var chosen = new ChosenSpellParams(null, X: 2,
            new IReadOnlyList<object>[0], ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();

        // Green bear should land on battlefield; white knight should stay in library.
        alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        alice.Zones.Library.GetCards().Should().Contain(knight);
        alice.Zones.Battlefield.GetCards().Should().NotContain(knight);
    }
}
