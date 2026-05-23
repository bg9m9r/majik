using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CabalTherapyFactory"/>.
///
/// Card: Cabal Therapy — Sorcery {B} (Judgment / Modern Horizons 2).
///   "Name a nonland card. Target player reveals their hand and discards
///    all cards with that name.
///    Flashback—Sacrifice a creature."
///
/// Covers:
///   - Identity + <see cref="NamedCardFactory"/> dispatch.
///   - Cast: name "Lightning Bolt", opponent has 2 Bolts → both discarded.
///   - Empty match: named card not in hand → no discards, no exceptions.
///   - Flashback cast from graveyard: cost mana-zero + sacrifice rider
///     legal; OnResolved exiles the card (CR 702.34b).
///   - Hand reveal publishes one <see cref="CardRevealedEvent"/> per card.
/// </summary>
public class CabalTherapyTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CabalTherapy_Identity()
    {
        var c = CabalTherapyFactory.Create(_alice);

        c.Name.Should().Be("Cabal Therapy");
        c.ManaCost.Should().Be("{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CabalTherapy()
    {
        var card = NamedCardFactory.Create("Cabal Therapy", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Cabal Therapy");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve: name → reveal → discard all matching
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_NamingLightningBolt_DiscardsBothMatchingCopiesFromHand()
    {
        // Bob's hand: two Lightning Bolts + one Counterspell. Naming
        // "Lightning Bolt" should sweep both Bolts into the graveyard and
        // leave Counterspell behind.
        var bolt1 = SeedHandCard(_bob, "Lightning Bolt");
        var bolt2 = SeedHandCard(_bob, "Lightning Bolt");
        var counter = SeedHandCard(_bob, "Counterspell");

        var def = CabalTherapyFactory.BuildSpellDefinition(
            caster: _alice,
            resolver: o => o!,
            nameSelector: _ => "Lightning Bolt",
            eventBus: null);

        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(counter);
        _bob.Zones.Graveyard.GetCards().Should().Contain(new[] { bolt1, bolt2 });
        bolt1.Zone.Should().Be(ZoneType.Graveyard);
        bolt2.Zone.Should().Be(ZoneType.Graveyard);
        counter.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_EmptyMatch_NoDiscards()
    {
        // Bob's hand has no Lightning Bolts; naming "Lightning Bolt"
        // discards nothing.
        var c1 = SeedHandCard(_bob, "Counterspell");
        var c2 = SeedHandCard(_bob, "Brainstorm");

        var def = CabalTherapyFactory.BuildSpellDefinition(
            caster: _alice,
            resolver: o => o!,
            nameSelector: _ => "Lightning Bolt",
            eventBus: null);

        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2 });
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NullNameSelector_NoDiscardsSweep()
    {
        // Defensive guard: a null / empty name must not sweep cards. Bob's
        // hand must end unchanged.
        var c1 = SeedHandCard(_bob, "Counterspell");
        var c2 = SeedHandCard(_bob, "Brainstorm");

        var def = CabalTherapyFactory.BuildSpellDefinition(
            caster: _alice,
            resolver: o => o!,
            nameSelector: _ => null,
            eventBus: null);

        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(2);
        _bob.Zones.Hand.GetCards().Should().Contain(new[] { c1, c2 });
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_PublishesCardRevealedEventPerHandCard()
    {
        // RevealHelper.RevealHand publishes one CardRevealedEvent per card
        // in the revealed hand. Verify the count matches the hand size.
        var h1 = SeedHandCard(_bob, "Lightning Bolt");
        var h2 = SeedHandCard(_bob, "Counterspell");
        var h3 = SeedHandCard(_bob, "Brainstorm");

        var bus = new EventBus();
        var reveals = new List<CardRevealedEvent>();
        bus.Subscribe<CardRevealedEvent>(r => reveals.Add(r));

        var def = CabalTherapyFactory.BuildSpellDefinition(
            caster: _alice,
            resolver: o => o!,
            nameSelector: _ => "Lightning Bolt",
            eventBus: bus);

        var effects = def.EffectFactory(MakeChosen(targetPlayer: _bob));
        foreach (var e in effects) e.Execute();

        reveals.Should().HaveCount(3);
        reveals.Select(r => r.Card).Should().Contain(new[] { h1, h2, h3 });
        reveals.Select(r => r.Reason).Should().AllBe("Cabal Therapy");
    }

    // -----------------------------------------------------------------------
    // Flashback cast: from graveyard, mana-zero alt-cost + sacrifice rider
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackCost_IsManaZero_AndSacrificeRiderIsLegal()
    {
        var ct = CabalTherapyFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(ct);
        ct.SetZone(ZoneType.Graveyard);

        var fb = CabalTherapyFactory.BuildFlashbackCost();
        fb.AlternativeManaCost.Should().Be(ManaCost.Zero);
        fb.Description.Should().Contain("Flashback");
        fb.CanCastFor(ct, _alice).Should().BeTrue();

        // Alice has a creature she can sacrifice — flashback additional
        // cost can be paid.
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var additional = CabalTherapyFactory.BuildFlashbackAdditionalCosts();
        additional.Should().HaveCount(1);
        var sac = additional[0];
        sac.Should().BeOfType<SacrificeACreatureAdditionalCost>();
        sac.CanPay(_alice).Should().BeTrue();
        sac.Pay(_alice).Should().BeTrue();

        // Sacrifice payment moves the creature from battlefield to graveyard.
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bear);
        _alice.Zones.Graveyard.GetCards().Should().Contain(bear);

        // Post-resolve hook exiles Cabal Therapy (CR 702.34b).
        fb.OnResolved(ct, _alice);

        ct.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(ct);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ct);
    }

    [Fact]
    public void FlashbackCost_CannotCast_FromHandOrBattlefield()
    {
        // CR 702.34 — flashback is only castable from graveyard.
        var ct = CabalTherapyFactory.Create(_alice);
        ct.SetZone(ZoneType.Hand);

        var fb = CabalTherapyFactory.BuildFlashbackCost();
        fb.CanCastFor(ct, _alice).Should().BeFalse();
    }

    [Fact]
    public void FlashbackSacrificeRider_NoCreaturesAvailable_CannotPay()
    {
        // No creature on the battlefield — sacrifice rider can't be paid,
        // so the flashback cast would fail at the additional-cost step.
        var sac = CabalTherapyFactory.BuildFlashbackAdditionalCosts()[0];
        sac.CanPay(_alice).Should().BeFalse();
        sac.Pay(_alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ICard SeedHandCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    private static ChosenSpellParams MakeChosen(Player targetPlayer) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetPlayer } },
            Mana: ManaPayment.Empty);
}
