using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FestivalOfEmbersFactory"/>.
///
/// Card: Festival of Embers — Enchantment {4}{R} (Modern Horizons 3).
/// Oracle text (verified against Scryfall):
///   "During your turn, you may cast instant and sorcery spells from your
///    graveyard by paying 1 life in addition to their other costs.
///    If a card or token would be put into your graveyard from anywhere,
///    exile it instead.
///    {1}{R}: Sacrifice this enchantment."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (name, type, mana cost, color).
///   - Grave-cast gate predicate: battlefield + controller + your turn +
///     instant-or-sorcery only.
///   - BuildAlternativeCost: printed mana cost + 1-life rider; OnResolved
///     loses 1 life.
///   - Graveyard→exile replacement: controller-scoped, battlefield-gated,
///     NOT EOT-expirable.
///   - "{1}{R}: Sacrifice this enchantment" activated ability.
/// </summary>
[Trait("Color", "R")]
public class FestivalOfEmbersFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FestivalOfEmbers_Identity()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);

        c.Name.Should().Be("Festival of Embers");
        c.ManaCost.Should().Be("{4}{R}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Grave-cast gate
    // -----------------------------------------------------------------------

    [Fact]
    public void Gate_AllowsInstantOrSorcery_OnControllersTurn_WhileOnBattlefield()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        PlaceOnBattlefield(c, _alice);
        var gate = FestivalOfEmbersFactory.GetGate(c)!;
        gate.SetActivePlayer(_alice);

        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        var rot = GraveSorcery("Mind Rot", "{2}{B}", _alice);

        gate.CanCast(bolt, _alice).Should().BeTrue("Bolt is an instant in Alice's graveyard on her turn");
        gate.CanCast(rot, _alice).Should().BeTrue("Mind Rot is a sorcery in Alice's graveyard on her turn");
    }

    [Fact]
    public void Gate_RejectsNonInstantOrSorcery()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        PlaceOnBattlefield(c, _alice);
        var gate = FestivalOfEmbersFactory.GetGate(c)!;
        gate.SetActivePlayer(_alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bear.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bear);

        gate.CanCast(bear, _alice).Should().BeFalse(
            "Festival only grants instant and sorcery spells");
    }

    [Fact]
    public void Gate_RejectsOnOpponentsTurn()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        PlaceOnBattlefield(c, _alice);
        var gate = FestivalOfEmbersFactory.GetGate(c)!;
        gate.SetActivePlayer(_bob); // not Alice's turn

        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        gate.CanCast(bolt, _alice).Should().BeFalse(
            "\"During your turn\" — the grant is inert on the opponent's turn");
    }

    [Fact]
    public void Gate_IsInert_WhileFestivalNotOnBattlefield()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        // Not placed on the battlefield.
        var gate = FestivalOfEmbersFactory.GetGate(c)!;
        gate.SetActivePlayer(_alice);

        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        gate.CanCast(bolt, _alice).Should().BeFalse(
            "the grant only functions while Festival is on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Alternative cost — printed mana + 1-life rider
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildAlternativeCost_CarriesPrintedManaCost_AndOneLifeRider()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        PlaceOnBattlefield(c, _alice);
        var gate = FestivalOfEmbersFactory.GetGate(c)!;
        gate.SetActivePlayer(_alice);

        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        var alt = FestivalOfEmbersFactory.BuildAlternativeCost(bolt, gate);

        // "in addition to their other costs" — the mana cost is the card's
        // PRINTED cost (not waived to zero, unlike a flashback alt cost).
        alt.AlternativeManaCost.TotalValue.Should().Be(1, "Bolt's printed cost {R} is mv 1");
        alt.LifeCost.Should().Be(1, "Festival adds a 1-life rider");
        alt.CanCastFor(bolt, _alice).Should().BeTrue(
            "Bolt is an instant in Alice's graveyard on her turn while Festival is out");
    }

    [Fact]
    public void AlternativeCost_OnResolved_LosesOneLife()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        PlaceOnBattlefield(c, _alice);
        var gate = FestivalOfEmbersFactory.GetGate(c)!;
        gate.SetActivePlayer(_alice);

        var bolt = GraveInstant("Lightning Bolt", "{R}", _alice);
        var alt = FestivalOfEmbersFactory.BuildAlternativeCost(bolt, gate);

        var before = _alice.LifeTotal;
        alt.OnResolved(bolt, _alice);
        _alice.LifeTotal.Should().Be(before - 1, "the +1 life rider is paid on resolution (CR 118.8)");
    }

    [Fact]
    public void BuildAlternativeCost_Throws_ForNonInstantOrSorcery()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        var gate = FestivalOfEmbersFactory.GetGate(c)!;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };

        var act = () => FestivalOfEmbersFactory.BuildAlternativeCost(bear, gate);
        act.Should().Throw<InvalidOperationException>(
            "Festival only grants instant and sorcery spells");
    }

    // -----------------------------------------------------------------------
    // Graveyard → exile replacement
    // -----------------------------------------------------------------------

    [Fact]
    public void Replacement_RewritesControllersGraveyardMove_ToExile()
    {
        var bus = new ReplacementBus();
        var c = FestivalOfEmbersFactory.Create(_alice, replacements: bus, eventBus: null);
        PlaceOnBattlefield(c, _alice);

        // "a card or token … from anywhere" — covers any source zone.
        var spell = new Instant("Bolt", "{R}") { Owner = _alice };
        var intent = new ZoneMoveIntent(spell, ZoneType.Stack, ZoneType.Graveyard, _alice);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile);

        var creature = new Creature("Goyf", "{1}{G}", 4, 5) { Owner = _alice };
        var dying = new ZoneMoveIntent(creature, ZoneType.Battlefield, ZoneType.Graveyard, _alice);
        bus.Apply(dying)!.ToZone.Should().Be(ZoneType.Exile,
            "\"a card or token would be put into your graveyard from anywhere\" — a dying creature too");
    }

    [Fact]
    public void Replacement_DoesNotRewrite_OpponentsGraveyardMove()
    {
        var bus = new ReplacementBus();
        var c = FestivalOfEmbersFactory.Create(_alice, replacements: bus, eventBus: null);
        PlaceOnBattlefield(c, _alice);

        var bobsCard = new Instant("Bob's Bolt", "{R}") { Owner = _bob };
        var intent = new ZoneMoveIntent(bobsCard, ZoneType.Stack, ZoneType.Graveyard, _bob);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "Festival is scoped to \"your graveyard\" — opponent moves are unaffected");
    }

    [Fact]
    public void Replacement_IsInert_WhileNotOnBattlefield()
    {
        var bus = new ReplacementBus();
        var c = FestivalOfEmbersFactory.Create(_alice, replacements: bus, eventBus: null);
        // Not placed on the battlefield.

        var spell = new Instant("Bolt", "{R}") { Owner = _alice };
        var intent = new ZoneMoveIntent(spell, ZoneType.Stack, ZoneType.Graveyard, _alice);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "the replacement is gated on Festival being on the battlefield (CR 614.6)");
    }

    [Fact]
    public void Replacement_IsNotEndOfTurnExpirable()
    {
        var bus = new ReplacementBus();
        var c = FestivalOfEmbersFactory.Create(_alice, replacements: bus, eventBus: null);
        PlaceOnBattlefield(c, _alice);

        bus.ExpireEndOfTurn();

        var spell = new Instant("Bolt", "{R}") { Owner = _alice };
        var intent = new ZoneMoveIntent(spell, ZoneType.Stack, ZoneType.Graveyard, _alice);
        bus.Apply(intent)!.ToZone.Should().Be(ZoneType.Exile,
            "Festival is a persistent enchantment — its replacement survives the EOT sweep");
    }

    [Fact]
    public void SingleArgPath_DoesNotRegisterReplacement()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        PlaceOnBattlefield(c, _alice);

        var emptyBus = new ReplacementBus();
        var spell = new Instant("Bolt", "{R}") { Owner = _alice };
        var intent = new ZoneMoveIntent(spell, ZoneType.Stack, ZoneType.Graveyard, _alice);
        emptyBus.Apply(intent)!.ToZone.Should().Be(ZoneType.Graveyard,
            "no replacement is registered on the single-arg path");
    }

    // -----------------------------------------------------------------------
    // {1}{R}: Sacrifice this enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void SacrificeAbility_HasManaCost_AndSacrificesSelf()
    {
        var c = FestivalOfEmbersFactory.Create(_alice);
        PlaceOnBattlefield(c, _alice);

        var sac = c.Abilities.OfType<ActivatedAbility>().Single();
        sac.Costs.OfType<ManaCostCost>().Single()
            .Cost.TotalValue.Should().Be(2, "the sac ability costs {1}{R} — mana value 2");

        // Resolve the effect — Festival moves itself to the graveyard
        // (no bus → direct zone move, no PermanentSacrificedEvent).
        foreach (var e in sac.Effects) e.Execute();

        c.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(c);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Enchantment c, Player owner)
    {
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
    }

    private Instant GraveInstant(string name, string cost, Player owner)
    {
        var i = new Instant(name, cost) { Owner = owner };
        i.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(i);
        return i;
    }

    private Sorcery GraveSorcery(string name, string cost, Player owner)
    {
        var s = new Sorcery(name, cost) { Owner = owner };
        s.SetZone(ZoneType.Graveyard);
        owner.Zones.Graveyard.AddCard(s);
        return s;
    }
}
