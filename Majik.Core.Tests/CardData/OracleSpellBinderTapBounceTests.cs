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
using Artifact = Majik.Core.Cards.Artifact;

public class OracleSpellBinderTapBounceTests
{
    private readonly Player _alice = new("Alice", 20);

    // ---------- Tap target permanent ----------

    [Fact]
    public void TapTargetPermanent_TapsCreature()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Twiddle", ManaCost = "{U}",
              OracleText = "Tap target permanent." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, bear);
        bear.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TapTargetCreature_TapsCreature()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Hold the Reins", ManaCost = "{U}",
              OracleText = "Tap target creature." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, bear);
        bear.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void TapTargetArtifact_TapsArtifact()
    {
        var sol = new Artifact("Sol Ring", "{1}")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "X", ManaCost = "{U}",
              OracleText = "Tap target artifact." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, sol);
        sol.IsTapped.Should().BeTrue();
    }

    // ---------- Destroy target permanent (expanded) ----------

    [Fact]
    public void DestroyTargetLand_MovesToGraveyard()
    {
        var mtn = new Land("Mountain")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(mtn);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Stone Rain", ManaCost = "{2}{R}",
              OracleText = "Destroy target land." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, mtn);
        mtn.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DestroyTargetNonlandPermanent_DestroysCreature()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Vindicate", ManaCost = "{1}{W}{B}",
              OracleText = "Destroy target nonland permanent." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, bear);
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    // ---------- Bounce ----------

    [Fact]
    public void ReturnTargetCreature_GoesToHand()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Unsummon", ManaCost = "{U}",
              OracleText = "Return target creature to its owner's hand." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, bear);

        bear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(bear);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    [Fact]
    public void ReturnTargetPermanent_GoesToHand()
    {
        var sol = new Artifact("Sol Ring", "{1}")
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(sol);

        var def = OracleSpellBinder.Bind(
            new CardEntity { Name = "Boomerang", ManaCost = "{U}{U}",
              OracleText = "Return target permanent to its owner's hand." },
            _alice, raw => raw, null);
        def.Should().NotBeNull();
        Resolve(def!, sol);

        sol.Zone.Should().Be(ZoneType.Hand);
    }

    private void Resolve(SpellDefinition def, object target)
    {
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { target } },
            ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
