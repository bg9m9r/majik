using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Graveyard Trespasser // Graveyard Glutton (MID DFC
/// front face) — Creature — Human Werewolf {2}{B} 3/3, Daybound.
///   "Ward—Discard a card. Whenever this creature enters or attacks, exile up
///    to one target card from a graveyard. If a creature card was exiled this
///    way, each opponent loses 1 life and you gain 1 life. Daybound."
///
/// Validates: identity + dispatch; MdfcState (Trespasser/Glutton);
/// daybound/nightbound markers (CR 702.145); the enters/attacks exile + drain
/// rider (CR 119.3); and the day→night transform via the DayboundNightbound
/// engine surface (PR2).
/// </summary>
public class GraveyardTrespasserTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private List<Player> Players => new() { _alice, _bob };

    // ------------------------------------------------------------------
    // Identity + dispatch
    // ------------------------------------------------------------------

    [Fact]
    public void GraveyardTrespasser_IsHumanWerewolf_At2B_3_3()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);

        gt.Name.Should().Be("Graveyard Trespasser");
        gt.HasType(CardType.Creature).Should().BeTrue();
        gt.HasSubtype(CardSubtype.Human).Should().BeTrue();
        gt.HasSubtype(CardSubtype.Werewolf).Should().BeTrue();
        gt.ManaCost.Should().Be("{2}{B}");
        gt.Power.Should().Be(3);
        gt.Toughness.Should().Be(3);
        gt.Owner.Should().BeSameAs(_alice);
        gt.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GraveyardTrespasser_AsWerewolfWithMdfc()
    {
        var dispatched = NamedCardFactory.Create("Graveyard Trespasser", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Graveyard Trespasser");
        dispatched.ManaCost.Should().Be("{2}{B}");

        var gt = (Creature)dispatched;
        gt.MdfcState.Should().NotBeNull("the dispatcher route must attach the DFC face-tracker");
        gt.MdfcState!.FrontFaceName.Should().Be("Graveyard Trespasser");
        gt.MdfcState.BackFaceName.Should().Be("Graveyard Glutton");
        gt.MdfcState.IsBackFace.Should().BeFalse("starts on the front face");
    }

    [Fact]
    public void GraveyardTrespasser_HasDayboundAndNightboundMarkers()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);

        // CR 702.145 — daybound on the front face, nightbound on the back.
        DayboundNightbound.HasDaybound(gt).Should().BeTrue();
        DayboundNightbound.HasNightbound(gt).Should().BeTrue();
    }

    [Fact]
    public void GraveyardTrespasser_HasEntersAndAttacksTriggers()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);

        gt.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "one ETB trigger + one attack trigger (CR 603.1 / 508.1f)");
    }

    // ------------------------------------------------------------------
    // CR 702.145c — front-face daybound werewolf transforms when it becomes
    // night (exercises the PR2 transform surface directly).
    // ------------------------------------------------------------------

    [Fact]
    public void GraveyardTrespasser_TransformsToGlutton_WhenItBecomesNight()
    {
        var gt = GraveyardTrespasserFactory.Create(_alice);

        DayboundNightbound.OnDayNightChanged(new[] { (Card)gt }, DayNightDesignation.Night);

        gt.MdfcState!.IsBackFace.Should().BeTrue();
        gt.MdfcState.ActiveFaceName.Should().Be("Graveyard Glutton");
    }

    // ------------------------------------------------------------------
    // CR 119.3 — enters/attacks: exile a creature card from a graveyard →
    // each opponent loses 1, controller gains 1.
    // ------------------------------------------------------------------

    [Fact]
    public void EntersOrAttacks_ExilesCreatureCardFromGraveyard_DrainsAndGains()
    {
        var players = Players;
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);

        // Put a creature card in Bob's graveyard.
        var corpse = new Creature("Dead Bear", "{1}{G}", 2, 2) { Owner = _bob };
        _bob.Zones.Graveyard.AddCard(corpse);
        corpse.SetZone(ZoneType.Graveyard);

        // Fire the ETB trigger effect.
        var etb = gt.Abilities.OfType<TriggeredAbility>().First();
        foreach (var fx in etb.Effects) fx.Execute();

        corpse.Zone.Should().Be(ZoneType.Exile, "the creature card is exiled from the graveyard");
        _bob.LifeTotal.Should().Be(19, "each opponent loses 1 life");
        _alice.LifeTotal.Should().Be(21, "controller gains 1 life");
    }

    [Fact]
    public void EntersOrAttacks_ExilesNonCreatureCard_NoDrain()
    {
        var players = Players;
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);

        // Only a non-creature card in a graveyard.
        var spell = new Sorcery("Old Spell", "{1}{B}") { Owner = _bob };
        _bob.Zones.Graveyard.AddCard(spell);
        spell.SetZone(ZoneType.Graveyard);

        var etb = gt.Abilities.OfType<TriggeredAbility>().First();
        foreach (var fx in etb.Effects) fx.Execute();

        spell.Zone.Should().Be(ZoneType.Exile, "the card is still exiled (up to one target)");
        _bob.LifeTotal.Should().Be(20, "no creature exiled → no life loss");
        _alice.LifeTotal.Should().Be(20, "no creature exiled → no life gain");
    }

    [Fact]
    public void EntersOrAttacks_EmptyGraveyards_NoOp()
    {
        var players = Players;
        var gt = GraveyardTrespasserFactory.Create(_alice, triggers: null, players: players);
        _alice.Zones.Battlefield.AddCard(gt);
        gt.SetZone(ZoneType.Battlefield);

        var etb = gt.Abilities.OfType<TriggeredAbility>().First();
        var act = () => { foreach (var fx in etb.Effects) fx.Execute(); };

        act.Should().NotThrow("up to one target — empty graveyards resolve as a no-op");
        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }
}
