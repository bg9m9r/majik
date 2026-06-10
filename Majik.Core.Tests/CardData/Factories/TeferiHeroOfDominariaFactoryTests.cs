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
using System.Threading.Tasks;
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
/// These are FACTORY-level (shape / structure / direct-activation) tests. The
/// agent-targeted prod-path tests — the −3 puts the CHOSEN permanent third from
/// top, the +1 untaps the CHOSEN lands at the next end step — live in
/// <see cref="Majik.Core.Tests.Game.LoyaltyAbilityDispatchTests"/>, driven
/// through a real <c>TurnDriver</c> turn with a target-choosing agent.
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

    [Fact]
    public void NamedCardFactory_DispatchesTeferi()
    {
        var teferi = NamedCardFactory.Create("Teferi, Hero of Dominaria", _alice);

        teferi.Should().BeOfType<Planeswalker>();
        teferi.Name.Should().Be("Teferi, Hero of Dominaria");
        teferi.HasSubtype(CardSubtype.Teferi).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Loyalty abilities declare real TargetRequests (the agent-target infra
    // the prod loyalty path consumes).
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_DeclaresUpToTwoLandsTargetRequest()
    {
        var teferi = TeferiHeroOfDominariaFactory.Create(_alice);
        var plus1 = teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);

        plus1.TargetRequests.Should().HaveCount(1);
        var req = plus1.TargetRequests[0];
        req.MinTargets.Should().Be(0, "\"up to\" two lands");
        req.MaxTargets.Should().Be(2);
    }

    [Fact]
    public void Minus3_DeclaresTargetNonlandPermanentRequest()
    {
        var teferi = TeferiHeroOfDominariaFactory.Create(_alice);
        var minus3 = teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3);

        minus3.TargetRequests.Should().HaveCount(1);
        var req = minus3.TargetRequests[0];
        req.MinTargets.Should().Be(1, "−3 targets a single nonland permanent");
        req.MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // +1: Draw a card; at the beginning of the next end step untap up to two
    //     chosen lands. (Direct activation: simulate the chosen-targets the
    //     prod loyalty path would supply.)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Plus1_DrawsACard_AndSchedulesNextEndStepUntap_OfChosenLands()
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

        var teferi = TeferiHeroOfDominariaFactory.Create(_alice, triggers);
        var plus1 = teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);

        // Resolve the +1 with the two lands chosen (what DispatchLoyalty supplies).
        plus1.PayLoyaltyCost();
        var rc = ResolutionContext.For(
            _alice, agent: null, game: null,
            chosenTargets: new[] { new object[] { land1, land2 } },
            ct: default);
        foreach (var e in plus1.Effects) await e.ExecuteAsync(rc);

        // Draw happened.
        _alice.Zones.Hand.GetCards().Should().Contain(top);
        teferi.Loyalty.Should().Be(5); // 4 + 1

        // A delayed end-step trigger is now registered.
        var delayed = teferi.Abilities.OfType<DelayedTriggeredAbility>().Last();
        triggers.IsRegistered(delayed).Should().BeTrue();

        // Lands still tapped until the trigger resolves.
        land1.IsTapped.Should().BeTrue();
        land2.IsTapped.Should().BeTrue();

        // Resolve the delayed trigger's effect (the untap clause).
        foreach (var e in delayed.Effects) e.Execute();

        land1.IsTapped.Should().BeFalse();
        land2.IsTapped.Should().BeFalse();
    }

    [Fact]
    public async Task Plus1_NoChosenLands_DrawsButSchedulesNoUntap()
    {
        var top = new Card("Top", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var bus = new EventBus();
        var triggers = new TriggerManager(new MajikStack(bus), bus);
        var teferi = TeferiHeroOfDominariaFactory.Create(_alice, triggers);
        var plus1 = teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1);

        plus1.PayLoyaltyCost();
        var rc = ResolutionContext.For(
            _alice, agent: null, game: null,
            chosenTargets: System.Array.Empty<IReadOnlyList<object>>(),
            ct: default);
        foreach (var e in plus1.Effects) await e.ExecuteAsync(rc);

        _alice.Zones.Hand.GetCards().Should().Contain(top, "the draw always happens");
        teferi.Loyalty.Should().Be(5);
        teferi.Abilities.OfType<DelayedTriggeredAbility>().Should()
            .BeEmpty("\"up to two\" — choosing zero lands schedules no untap");
    }

    // -----------------------------------------------------------------------
    // −3: Put target nonland permanent into its owner's library third from the
    //     top. (Direct activation with the chosen target supplied.)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Minus3_PutsChosenNonlandPermanent_ThirdFromTopOfOwnersLibrary()
    {
        // Bob's library has two cards on top.
        var libTop = new Card("LibTop", "{1}") { Owner = _bob };
        var libSecond = new Card("LibSecond", "{1}") { Owner = _bob };
        _bob.Zones.Library.AddCard(libTop); libTop.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(libSecond); libSecond.SetZone(ZoneType.Library);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield); bears.SetController(_bob);

        var teferi = TeferiHeroOfDominariaFactory.Create(_alice);
        var minus3 = teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -3);

        minus3.PayLoyaltyCost();
        var rc = ResolutionContext.For(
            _alice, agent: null, game: null,
            chosenTargets: new[] { new object[] { bears } },
            ct: default);
        foreach (var e in minus3.Effects) await e.ExecuteAsync(rc);

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

    // -----------------------------------------------------------------------
    // −8: emblem with "Whenever you draw a card, exile target permanent an
    //     opponent controls."
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Minus8_CreatesEmblem_WithTargetedDrawTrigger_ThatExilesChosenPermanent()
    {
        // Bob controls a permanent the emblem can exile.
        var goblin = new Creature("Goblin", "{R}", 1, 1);
        goblin.SetOwner(_bob); _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield); goblin.SetController(_bob);

        var bus = new EventBus();
        var triggers = new TriggerManager(new MajikStack(bus), bus);

        var teferi = TeferiHeroOfDominariaFactory.Create(_alice, triggers);

        teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8)
            .CanActivate().Should().BeFalse("4 loyalty is not enough for −8");

        teferi.AddLoyalty(4); // 4 + 4 = 8
        var ult = teferi.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -8);
        ult.CanActivate().Should().BeTrue();
        ult.Activate();

        teferi.Loyalty.Should().Be(0); // 8 - 8

        // Emblem minted in Alice's command zone with a TARGETED draw trigger.
        _alice.Emblems.Should().HaveCount(1);
        var emblem = _alice.Emblems.Single();
        var drawTrigger = emblem.Abilities.OfType<TriggeredAbility>().Single();
        drawTrigger.TargetRequests.Should().HaveCount(1,
            "the emblem's draw trigger targets a permanent an opponent controls");

        // Fire the emblem's draw trigger with the chosen target supplied.
        var rc = ResolutionContext.For(
            _alice, agent: null, game: null,
            chosenTargets: new[] { new object[] { goblin } },
            ct: default);
        foreach (var e in drawTrigger.Effects) await e.ExecuteAsync(rc);

        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);
        _bob.Zones.Exile.GetCards().Should().Contain(goblin);
        goblin.Zone.Should().Be(ZoneType.Exile);
    }
}
