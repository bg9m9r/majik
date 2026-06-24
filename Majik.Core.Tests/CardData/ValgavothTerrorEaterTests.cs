using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Valgavoth, Terror Eater (Duskmourn: House of Horror, {6}{B}{B}{B}).
///
/// Legendary Creature — Elder Demon 9/9. Oracle text (Scryfall, verified):
///   "Flying, lifelink
///    Ward—Sacrifice three nonland permanents.
///    If a card you didn't control would be put into an opponent's graveyard
///    from anywhere, exile it instead.
///    During your turn, you may play cards exiled with Valgavoth. If you cast a
///    spell this way, pay life equal to its mana value rather than pay its mana
///    cost."
///
/// Covers the card's UNIQUE behaviour:
///  - Identity (exact mana cost / P-T / supertype / subtypes) — single assert.
///  - Flying + Lifelink keyword markers (CR 702.9 / CR 702.15).
///  - Ward—Sacrifice three nonland permanents (CR 702.21c) via the bound
///    <see cref="WardEffect"/> charging a <see cref="SacrificeNNonlandPermanentsCost"/>(3).
///  - Replacement: an opponent's grave-bound card is exiled instead (CR 614),
///    funnelled through <see cref="ReplacementBus"/> on every
///    <see cref="ZoneMoveIntent"/> with destination Graveyard — "from anywhere".
///  - Controller's own card going to graveyard is NOT replaced.
///  - Play-from-exile: pay life equal to the spell's mana value (CR 118.9) via
///    <see cref="PayLifeEqualToManaValueAlternativeCost"/>.
/// </summary>
[Trait("Color", "B")]
public class ValgavothTerrorEaterTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ──────────────────────────────────────────────────────────

    [Fact]
    public void Valgavoth_Identity_LegendaryElderDemon_9_9_At6BBB()
    {
        var v = ValgavothTerrorEaterFactory.Create(_alice);

        v.Name.Should().Be("Valgavoth, Terror Eater");
        v.ManaCost.Should().Be("{6}{B}{B}{B}");
        v.HasType(CardType.Creature).Should().BeTrue();
        v.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue();
        v.HasSubtype(CardSubtype.Elder).Should().BeTrue();
        v.HasSubtype(CardSubtype.Demon).Should().BeTrue();
        v.BasePower.Should().Be(9);
        v.BaseToughness.Should().Be(9);
    }

    // ── Keywords ──────────────────────────────────────────────────────────

    [Fact]
    public void Valgavoth_HasFlyingAndLifelink()
    {
        var v = ValgavothTerrorEaterFactory.Create(_alice);

        var keywords = v.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Lifelink");
        keywords.Should().Contain("Ward");
    }

    // ── Ward—Sacrifice three nonland permanents (CR 702.21c) ──────────────

    [Fact]
    public void Ward_ChargesSacrificeThreeNonlandPermanents()
    {
        var v = ValgavothTerrorEaterFactory.Create(_alice);
        v.SetController(_alice);
        var ward = ValgavothTerrorEaterFactory.BuildWardEffect(v);

        ward.PaymentCost.Should().BeOfType<SacrificeNNonlandPermanentsCost>();
        ((SacrificeNNonlandPermanentsCost)ward.PaymentCost).Count.Should().Be(3);

        // Bob has only two nonland permanents — he cannot pay the ward, so his
        // targeting spell is countered (CR 702.21f).
        var bear1 = new Creature("Bear A", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bear2 = new Creature("Bear B", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(bear1); bear1.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear2); bear2.SetZone(ZoneType.Battlefield);

        ward.Resolve(_bob).Should().BeTrue("Bob controls only 2 nonland permanents — ward counters his spell");

        // Add a third nonland permanent — now Bob can pay, sacrificing all three.
        var bear3 = new Creature("Bear C", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        _bob.Zones.Battlefield.AddCard(bear3); bear3.SetZone(ZoneType.Battlefield);

        ward.Resolve(_bob).Should().BeFalse("Bob sacrifices three nonland permanents to pay the ward");
        _bob.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(3);
    }

    // ── Replacement — opponent's grave-bound card → exile ─────────────────

    [Fact]
    public void OpponentsCardToGraveyard_IsExiledInstead()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var (v, _) = ValgavothTerrorEaterFactory.Create(_alice, rep);
        PutOnBattlefield(v, _alice);

        var bobCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobCreature.SetOwner(_bob);
        PutOnBattlefield(bobCreature, _bob);

        zones.MoveCardTo(bobCreature, ZoneType.Graveyard);

        bobCreature.Zone.Should().Be(ZoneType.Exile, "the replacement rewrote Graveyard → Exile");
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bobCreature);
        _bob.Zones.Exile.GetCards().Should().Contain(bobCreature);

        var state = ValgavothTerrorEaterFactory.GetState(v);
        state.Should().NotBeNull();
        state!.ExiledCards.Should().Contain(bobCreature,
            "the exiled card is tracked as exiled-with-Valgavoth (playable during your turn)");
    }

    [Fact]
    public void OpponentsDiscard_HandToGraveyard_IsExiledInstead()
    {
        // "from ANYWHERE" — a hand → graveyard discard fires the replacement.
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var (v, _) = ValgavothTerrorEaterFactory.Create(_alice, rep);
        PutOnBattlefield(v, _alice);

        var bobSpell = new Sorcery("Thoughtseize", "{B}") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobSpell);
        bobSpell.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobSpell, ZoneType.Graveyard);

        bobSpell.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(bobSpell);
        ValgavothTerrorEaterFactory.GetState(v)!.ExiledCards.Should().Contain(bobSpell);
    }

    [Fact]
    public void ControllersOwnCardGoingToGraveyard_IsNotReplaced()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var (v, _) = ValgavothTerrorEaterFactory.Create(_alice, rep);
        PutOnBattlefield(v, _alice);

        var aliceCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        aliceCreature.SetOwner(_alice);
        PutOnBattlefield(aliceCreature, _alice);

        zones.MoveCardTo(aliceCreature, ZoneType.Graveyard);

        aliceCreature.Zone.Should().Be(ZoneType.Graveyard, "Valgavoth only exiles cards you DIDN'T control");
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCreature);
        ValgavothTerrorEaterFactory.GetState(v)!.ExiledCards.Should().NotContain(aliceCreature);
    }

    // ── Play-from-exile: pay life equal to mana value (CR 118.9) ──────────

    [Fact]
    public void PlayFromExile_AltCost_PaysLifeEqualToManaValue()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);

        var (v, _) = ValgavothTerrorEaterFactory.Create(_alice, rep);
        PutOnBattlefield(v, _alice);

        // Bob's {2}{R} spell (mana value 3) dies into Valgavoth's exile pile.
        var bobSpell = new Sorcery("Lava Spike", "{2}{R}") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobSpell);
        bobSpell.SetZone(ZoneType.Hand);
        zones.MoveCardTo(bobSpell, ZoneType.Graveyard);

        bobSpell.Zone.Should().Be(ZoneType.Exile);
        ValgavothTerrorEaterFactory.GetState(v)!.ExiledCards.Should().Contain(bobSpell);

        // Alice may play it during her turn paying life equal to its mana value.
        var altCost = ValgavothTerrorEaterFactory.BuildPlayFromExileCost();
        altCost.AlternativeManaCost.IsZero.Should().BeTrue("no mana is paid — life replaces the mana cost");
        PayLifeEqualToManaValueAlternativeCost.LifeAmountFor(bobSpell).Should().Be(3,
            "{2}{R} has mana value 3 — Alice pays 3 life");
        altCost.CanCastFor(bobSpell, _alice).Should().BeTrue("Alice has 20 life ≥ 3");
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static void PutOnBattlefield(ICard card, Player controller)
    {
        card.SetOwner(card.Owner ?? controller);
        card.SetController(controller);
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
