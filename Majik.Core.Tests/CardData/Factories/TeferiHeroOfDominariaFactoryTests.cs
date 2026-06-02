using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using MajikStack = Majik.Core.Stack.Stack;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Teferi, Hero of Dominaria (Dominaria, {3}{W}{U}).
///
/// Legendary Planeswalker — Teferi, starting loyalty 4. Oracle text
/// (Scryfall, verified):
///   "+1: Draw a card. At the beginning of the next end step, untap up to
///        two lands.
///    −3: Put target nonland permanent into its owner's library third from
///        the top.
///    −8: You get an emblem with 'Whenever you draw a card, exile target
///        permanent an opponent controls.'"
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Teferi, loyalty 4, {3}{W}{U}),
///     materialised from the embedded JSON definition.
///   - +1: draw + delayed-trigger untap-up-to-two-lands at the next end step.
///   - −3: target nonland permanent → owner's library third from the top.
///   - −8: emblem with a draw-trigger that exiles an opponent's permanent.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "M")]
public class TeferiHeroOfDominariaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Teferi_IsLegendaryPlaneswalker_Teferi_4Loyalty_AtCost3WU()
    {
        var teferi = TeferiHeroOfDominariaFactory.Create(_alice);

        teferi.Name.Should().Be("Teferi, Hero of Dominaria");
        teferi.ManaCost.Should().Be("{3}{W}{U}");
        teferi.HasType(CardType.Planeswalker).Should().BeTrue();
        teferi.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        teferi.HasSubtype(CardSubtype.Teferi).Should().BeTrue();
        teferi.Loyalty.Should().Be(4);
        teferi.StartingLoyalty.Should().Be(4);
        teferi.Owner.Should().BeSameAs(_alice);
        teferi.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Teferi_HasPlus1_Minus3_Minus8_LoyaltyAbilities()
    {
        var teferi = TeferiHeroOfDominariaFactory.Create(_alice);

        var loyalty = teferi.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -3, -8 });
    }
    // -----------------------------------------------------------------------
    // +1: Draw a card; at the beginning of the next end step untap up to two
    //     lands.
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_DrawsACard_AndSchedulesNextEndStepUntap()
    {
        var top = new Card("Top", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Two tapped lands Alice controls.
        var land1 = new Land("Island", new[] { CardSupertype.Basic }, new[] { CardSubtype.Island });
        land1.SetOwner(_alice); _alice.Zones.Battlefield.AddCard(land1);
        land1.SetZone(ZoneType.Battlefield); land1.SetController(_alice); land1.Tap();
        var land2 = new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains });
        land2.SetOwner(_alice); _alice.Zones.Battlefield.AddCard(land2);
        land2.SetZone(ZoneType.Battlefield); land2.SetController(_alice); land2.Tap();

        var bus = new EventBus();
        var triggers = new TriggerManager(new MajikStack(bus), bus);

        var teferi = TeferiHeroOfDominariaFactory.Create(
            _alice,
            landUntapResolver: () => new[] { land1, land2 },
            targetPermanentResolver: null,
            opponentPermanentResolver: null,
            triggers: triggers);

        var plus1 = teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        // Draw happened.
        _alice.Zones.Hand.GetCards().Should().Contain(top);
        teferi.Loyalty.Should().Be(5); // 4 + 1

        // A delayed end-step trigger is now registered.
        triggers.IsRegistered(teferi.Abilities.OfType<DelayedTriggeredAbility>().Last())
            .Should().BeTrue();

        // Lands still tapped until the trigger resolves.
        land1.IsTapped.Should().BeTrue();
        land2.IsTapped.Should().BeTrue();

        // Resolve the delayed trigger's effect (the untap clause).
        var delayed = teferi.Abilities.OfType<DelayedTriggeredAbility>().Last();
        foreach (var e in delayed.Effects) e.Execute();

        land1.IsTapped.Should().BeFalse();
        land2.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void Plus1_UntapsAtMostTwoLands()
    {
        var lands = new List<Land>();
        for (var i = 0; i < 4; i++)
        {
            var l = new Land($"Forest{i}", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
            l.SetOwner(_alice); _alice.Zones.Battlefield.AddCard(l);
            l.SetZone(ZoneType.Battlefield); l.SetController(_alice); l.Tap();
            lands.Add(l);
        }

        var bus = new EventBus();
        var triggers = new TriggerManager(new MajikStack(bus), bus);
        var teferi = TeferiHeroOfDominariaFactory.Create(
            _alice,
            landUntapResolver: () => lands,
            targetPermanentResolver: null,
            opponentPermanentResolver: null,
            triggers: triggers);

        teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();
        var delayed = teferi.Abilities.OfType<DelayedTriggeredAbility>().Last();
        foreach (var e in delayed.Effects) e.Execute();

        // "up to two" — exactly two untapped.
        lands.Count(l => !l.IsTapped).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // −3: Put target nonland permanent into its owner's library third from the
    //     top.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus3_PutsTargetNonlandPermanent_ThirdFromTopOfOwnersLibrary()
    {
        // Bob's library has two cards on top.
        var libTop = new Card("LibTop", "{1}") { Owner = _bob };
        var libSecond = new Card("LibSecond", "{1}") { Owner = _bob };
        _bob.Zones.Library.AddCard(libTop); libTop.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libSecond); libSecond.SetZone(ZoneType.Library);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield); bears.SetController(_bob);

        var teferi = TeferiHeroOfDominariaFactory.Create(
            _alice,
            landUntapResolver: null,
            targetPermanentResolver: () => new[] { (Permanent)bears },
            opponentPermanentResolver: null,
            triggers: null);

        teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3).Activate();

        teferi.Loyalty.Should().Be(1); // 4 - 3
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        bears.Zone.Should().Be(ZoneType.Library);

        // "third from the top" — index 2 in a top-first library.
        var lib = _bob.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(3);
        lib[0].Should().BeSameAs(libTop);
        lib[1].Should().BeSameAs(libSecond);
        lib[2].Should().BeSameAs(bears);
    }

    [Fact]
    public void Minus3_DoesNotTargetLands()
    {
        var land = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        land.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield); land.SetController(_bob);

        var teferi = TeferiHeroOfDominariaFactory.Create(
            _alice,
            landUntapResolver: null,
            targetPermanentResolver: () => new[] { (Permanent)land },
            opponentPermanentResolver: null,
            triggers: null);

        teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3).Activate();

        // "nonland permanent" — the land is skipped.
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
        _bob.Zones.Library.GetCards().Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // −8: emblem with "Whenever you draw a card, exile target permanent an
    //     opponent controls."
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus8_CreatesEmblem_WithDrawTriggerThatExilesOpponentPermanent()
    {
        // Bob controls a permanent the emblem can exile.
        var goblin = new Creature("Goblin", "{R}", 1, 1);
        goblin.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield); goblin.SetController(_bob);

        var bus = new EventBus();
        var triggers = new TriggerManager(new MajikStack(bus), bus);

        var teferi = TeferiHeroOfDominariaFactory.Create(
            _alice,
            landUntapResolver: null,
            targetPermanentResolver: null,
            opponentPermanentResolver: () => new[] { (Permanent)goblin },
            triggers: triggers);

        teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8)
            .CanActivate().Should().BeFalse("4 loyalty is not enough for −8");

        teferi.AddLoyalty(4); // 4 + 4 = 8
        var ult = teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8);
        ult.CanActivate().Should().BeTrue();
        ult.Activate();

        teferi.Loyalty.Should().Be(0); // 8 - 8

        // Emblem minted in Alice's command zone.
        _alice.Emblems.Should().HaveCount(1);
        var emblem = _alice.Emblems.Single();
        emblem.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);

        // Fire the emblem's draw trigger effect — exiles the opponent's permanent.
        var drawTrigger = emblem.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in drawTrigger.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
        _bob.Zones.Exile.GetCards().Should().Contain(goblin);
        goblin.Zone.Should().Be(ZoneType.Exile);
    }
}
