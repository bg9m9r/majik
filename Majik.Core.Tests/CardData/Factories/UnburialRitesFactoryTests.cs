using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="UnburialRitesFactory"/>.
///
/// Unburial Rites (Avacyn Restored, {4}{B}). Sorcery. Oracle text
/// (verified against Scryfall 2026-05-29):
///   "Return target creature card from your graveyard to the battlefield.
///    Flashback {3}{W} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// Covers:
/// - Card identity (name, Sorcery type, {4}{B} mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Resolve: returns the targeted creature card from the CASTER's graveyard
///   to the caster's battlefield (no life loss — unlike Reanimate).
/// - Resolve illegal-target gate (CR 608.2b): non-creature / wrong-zone /
///   wrong-owner targets are no-ops.
/// - Resolve routes through ZoneService when supplied (CR 603.6a — ETB).
/// - Flashback cost ({3}{W}) parsed from the printed oracle text.
/// </summary>
[Trait("Color", "B")]
public class UnburialRitesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void UnburialRites_Identity()
    {
        var c = UnburialRitesFactory.Create(_alice);

        c.Name.Should().Be("Unburial Rites");
        c.Should().BeOfType<Sorcery>();
        c.ManaCost.Should().Be("{4}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void UnburialRites_Resolve_ReturnsTargetCreature_NoLifeLoss()
    {
        var alice = new Player("Alice", 20);

        // Hill Giant — printed mv 4 ({3}{R}). Unburial Rites — unlike
        // Reanimate — costs no life, so mana value is irrelevant.
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var def = UnburialRitesFactory.BuildSpellDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null, Targets: new[] { new object[] { giant } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }

        giant.Zone.Should().Be(ZoneType.Battlefield,
            "the target creature card was returned to the caster's battlefield");
        alice.Zones.Graveyard.GetCards().Should().NotContain(giant);
        alice.Zones.Battlefield.GetCards().Should().Contain(giant);
        giant.Controller.Should().BeSameAs(alice,
            "the returned permanent enters under the caster's control (CR 110.2)");
        alice.LifeTotal.Should().Be(20,
            "Unburial Rites has no life-loss clause — caster's life is unchanged");
    }

    [Fact]
    public void UnburialRites_Resolve_IgnoresNonCreatureTarget()
    {
        var alice = new Player("Alice", 20);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var def = UnburialRitesFactory.BuildSpellDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null, Targets: new[] { new object[] { bolt } }, Mana: ManaPayment.Empty);
        var act = () =>
        {
            foreach (var effect in def.EffectFactory(chosen))
            {
                effect.Execute();
            }
        };

        act.Should().NotThrow(
            "a non-creature target is illegal — resolve no-ops (CR 608.2b)");
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "instants are not creature cards — must remain in graveyard");
    }

    [Fact]
    public void UnburialRites_Resolve_IgnoresCreatureNotInCastersGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Creature card sits in BOB's graveyard — "your graveyard" means the
        // caster's only, so it is not a legal target (CR 608.2b).
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(bob);
        bob.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var def = UnburialRitesFactory.BuildSpellDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null, Targets: new[] { new object[] { giant } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }

        giant.Zone.Should().Be(ZoneType.Graveyard,
            "the creature belongs to another player's graveyard — not 'your graveyard'");
        alice.Zones.Battlefield.GetCards().Should().NotContain(giant);
    }

    [Fact]
    public void UnburialRites_Resolve_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var def = UnburialRitesFactory.BuildSpellDefinition(alice, o => o, zoneService: zones);
        var chosen = new ChosenSpellParams(ModeIndex: null, X: null, Targets: new[] { new object[] { bear } }, Mana: ManaPayment.Empty);
        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.Execute();
        }

        bear.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, bear)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }

    [Fact]
    public void UnburialRites_FlashbackCost_IsThreeGenericWhite()
    {
        var cost = UnburialRitesFactory.BuildFlashbackCost();

        // CR 702.34 — Flashback {3}{W}. Parsed from the printed oracle text
        // via FlashbackOracleParser so the named-factory path agrees with the
        // data-driven oracle binder path.
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("{3}{W}"));
    }
}
