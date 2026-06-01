using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Deferral #19 residual (b) — back-face ABILITY text swap (CR 711.3).
///
/// Deferral #19 swapped the BODY (P/T / type / colour) of a transformed DFC
/// via the Layer-0 seed, but NOT the distinct triggered-ability text. The
/// front face (Graveyard Trespasser) reads "exile up to ONE target card; if a
/// creature card was exiled, each opponent loses 1 / you gain 1". The back
/// face (Graveyard Glutton) reads "exile up to TWO target cards; for EACH
/// creature card exiled this way, each opponent loses 1 / you gain 1".
///
/// These tests pin the ability swap: while on the front face the front rider
/// (up-to-one) is the active trigger and the back rider is suppressed; once
/// flipped to the back face the back rider (up-to-two + per-creature drain) is
/// the active trigger and the front rider is suppressed.
/// </summary>
public class GraveyardGluttonBackFaceAbilityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private List<Player> Players => new() { _alice, _bob };

    // The ETB trigger reacts to a CardMovedEvent; the attack trigger reacts to
    // a CreatureAttacksEvent. Pick the currently-active ETB trigger by probing
    // IsTriggered with a fabricated self-ETB event (which also exercises the
    // ActiveWhen face gate).
    private static TriggeredAbility ActiveEtbTrigger(Creature gt)
    {
        var etbEvent = new Majik.Core.Events.CardMovedEvent(
            gt, ZoneType.Hand, ZoneType.Battlefield);
        return gt.Abilities.OfType<TriggeredAbility>().First(t => t.IsTriggered(etbEvent));
    }

    // ------------------------------------------------------------------
    // Front + back ability sets both attached; only active-face fires.
    // ------------------------------------------------------------------

    [Fact]
    public void CarriesBothFaces_FrontAndBack_TriggerSets()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: Players);

        // Front: 2 triggers (ETB + attack). Back: 2 triggers (ETB + attack).
        gt.Abilities.OfType<TriggeredAbility>().Should().HaveCount(4,
            "front face (2) + back face (2) ability sets are both attached (CR 711.3)");

        // Each non-null face gate partitions the 4 into 2 front + 2 back.
        var gated = gt.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.ActiveWhen is not null).ToList();
        gated.Should().HaveCount(4, "every trigger carries a face gate");
    }

    [Fact]
    public void OnFrontFace_OnlyFrontTriggersAreActive()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: Players);
        gt.MdfcState!.IsBackFace.Should().BeFalse();

        var active = gt.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.ActiveWhen is null || t.ActiveWhen());
        active.Should().Be(2, "only the 2 front-face triggers are active on the front face");
    }

    [Fact]
    public void OnBackFace_OnlyBackTriggersAreActive()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: Players);
        gt.MdfcState!.Transform(); // flip to Glutton
        gt.MdfcState.IsBackFace.Should().BeTrue();

        var active = gt.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.ActiveWhen is null || t.ActiveWhen());
        active.Should().Be(2, "only the 2 back-face triggers are active on the back face");
    }

    // ------------------------------------------------------------------
    // Back face (Graveyard Glutton): exile up to TWO + per-creature drain.
    // ------------------------------------------------------------------

    [Fact]
    public void Glutton_BackFace_ExilesTwoCreatureCards_DrainsPerCreature()
    {
        var players = Players;
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);
        gt.MdfcState!.Transform(); // become Graveyard Glutton (back face)

        // Two creature cards in Bob's graveyard.
        var c1 = new Creature("Dead Bear", "{1}{G}", 2, 2) { Owner = _bob };
        var c2 = new Creature("Dead Wolf", "{2}{G}", 3, 3) { Owner = _bob };
        foreach (var c in new[] { c1, c2 })
        {
            _bob.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        var etb = ActiveEtbTrigger(gt);
        foreach (var fx in etb.Effects) fx.Execute();

        c1.Zone.Should().Be(ZoneType.Exile, "the back face exiles up to TWO cards");
        c2.Zone.Should().Be(ZoneType.Exile, "the back face exiles up to TWO cards");
        // Per-creature drain: 2 creature cards exiled → each opp loses 2, you gain 2.
        _bob.LifeTotal.Should().Be(18, "for EACH creature card exiled, each opponent loses 1");
        _alice.LifeTotal.Should().Be(22, "for EACH creature card exiled, you gain 1");
    }

    [Fact]
    public void Glutton_BackFace_OneCreatureOneNonCreature_DrainsOnlyForCreature()
    {
        var players = Players;
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);
        gt.MdfcState!.Transform();

        var creature = new Creature("Dead Bear", "{1}{G}", 2, 2) { Owner = _bob };
        var spell = new Sorcery("Old Spell", "{1}{B}") { Owner = _bob };
        _bob.Zones.Graveyard.AddCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(spell);
        spell.SetZone(ZoneType.Graveyard);

        var etb = ActiveEtbTrigger(gt);
        foreach (var fx in etb.Effects) fx.Execute();

        creature.Zone.Should().Be(ZoneType.Exile);
        spell.Zone.Should().Be(ZoneType.Exile, "up to two cards exiled regardless of type");
        _bob.LifeTotal.Should().Be(19, "only the ONE creature card drives the drain");
        _alice.LifeTotal.Should().Be(21);
    }

    // ------------------------------------------------------------------
    // Front face (Graveyard Trespasser): exile up to ONE only.
    // ------------------------------------------------------------------

    [Fact]
    public void Trespasser_FrontFace_ExilesOnlyOneCard()
    {
        var players = Players;
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);
        // front face — no transform

        var c1 = new Creature("Dead Bear", "{1}{G}", 2, 2) { Owner = _bob };
        var c2 = new Creature("Dead Wolf", "{2}{G}", 3, 3) { Owner = _bob };
        foreach (var c in new[] { c1, c2 })
        {
            _bob.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        var etb = ActiveEtbTrigger(gt);
        foreach (var fx in etb.Effects) fx.Execute();

        var exiled = new[] { c1, c2 }.Count(c => c.Zone == ZoneType.Exile);
        exiled.Should().Be(1, "the FRONT face exiles up to ONE card only");
        // One creature exiled → opp loses 1, you gain 1.
        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(21);
    }
}
