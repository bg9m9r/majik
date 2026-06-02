using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the COMBINED split card factory <see cref="FireIceFactory"/>
/// (Fire // Ice, {1}{R} // {1}{U}). Both faces are Instants.
///
/// Oracle text (verified against Scryfall):
///   Fire {1}{R} — "Fire deals 2 damage divided as you choose among one or
///     two targets."
///   Ice {1}{U} — "Tap target permanent.\nDraw a card."
///
/// Split cards present each half as its own castable face (CR 712.2 — a split
/// card has two faces on one card; the caster picks one face to cast, and only
/// that face's cost / effect applies). This factory mirrors the two-face
/// posture of <see cref="BoomBustFactory"/>: the combined card name is the
/// <c>[CardName]</c> dispatch key (matching the seed row "Fire // Ice"), the
/// card SHAPE is built from the embedded JSON definition, and each face's
/// resolve-time <see cref="Game.SpellDefinition"/> is delegated to the
/// already-implemented single-half factories
/// (<see cref="FireFactory"/> / <see cref="IceFactory"/>).
///
/// Covers:
///   - Combined card identity (Instant, combined name, red, front Fire cost).
///   - <see cref="NamedCardFactory"/> dispatch for the combined name.
///   - Fire face delegation — divided 2 damage among one or two targets.
///   - Ice face delegation — tap target permanent then the caster draws.
/// </summary>
[Trait("Color", "R")]
public class FireIceCombinedFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ────────────────────────────────────────────────

    [Fact]
    public void FireIce_IsInstant_WithFireFrontFaceCost()
    {
        var card = FireIceFactory.Create(_alice);

        card.Name.Should().Be("Fire // Ice");
        card.HasType(CardType.Instant).Should().BeTrue();
        // The combined card carries the front (Fire) face mana cost.
        card.ManaCost.ToString().Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FireIce_IsRed()
    {
        var card = FireIceFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.Red);
    }
    // ── Fire face — divided damage (delegated to FireFactory) ───────────────

    [Fact]
    public void FireFace_TwoTargets_DefaultSplit_OneEach()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = FireIceFactory.BuildFireDefinition(resolver: x => x);
        def.TargetRequests.Should().HaveCount(1, "Fire targets one or two targets");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(2);

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob, creature } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(19, "default split is 1 damage to each of two targets (CR 119.4)");
        creature.Damage.Should().Be(1, "the other 1 damage lands on the creature");
    }

    [Fact]
    public void FireFace_OneTarget_DealsAll2Damage()
    {
        var def = FireIceFactory.BuildFireDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { _bob } }, ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.LifeTotal.Should().Be(18, "all 2 damage lands on the single target");
    }

    // ── Ice face — tap + draw (delegated to IceFactory) ─────────────────────

    [Fact]
    public void IceFace_TapsTargetPermanent_ThenCasterDraws()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var topCard = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Controller = _alice };
        _alice.Zones.Library.AddCard(topCard);

        var def = FireIceFactory.BuildIceDefinition(_alice, resolver: x => x);
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { creature } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        creature.IsTapped.Should().BeTrue("Ice taps the target permanent (CR 701.27)");
        _alice.Zones.Hand.GetCards().Should().Contain(topCard, "the caster draws a card (CR 121.1)");
        _alice.Zones.Library.GetCards().Should().NotContain(topCard);
    }
}
