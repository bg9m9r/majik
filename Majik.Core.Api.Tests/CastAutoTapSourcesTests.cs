using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Coverage for the portal "Auto-pay" button (mana cost prompt).
///
/// The portal sends a <see cref="ChooseManaCommand"/> with an EMPTY source
/// list to mean "auto-tap my untapped lands to cover this cost". Previously
/// the server tapped nothing (the resolver only activates the listed
/// sources), so casting with an empty floating pool + Auto-pay silently
/// rotated the spell back to hand. <c>TurnDriver.DispatchCast</c> now
/// detects the empty-but-not-cancelled payment and asks
/// <c>ManaPaymentResolver.TryAutoSelectSources</c> to greedily pick untapped
/// mana sources before paying.
/// </summary>
public class CastAutoTapSourcesTests
{
    // ── 1. Empty pool + empty payment → auto-tap untapped lands ─────────

    [Fact]
    public async Task Cast_EmptyPool_EmptyPayment_AutoTapsLandsToCoverCost()
    {
        // {1}{G} creature, empty floating pool. Two untapped Forests plus a
        // tapped-out / other land on the battlefield. Agent returns an empty
        // ManaPayment (Auto-pay). The engine must auto-select the Forests and
        // pay the cost; the spell leaves the hand.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { bear }, Array.Empty<ICard>());

        var forest1 = BuildBasicLand("Forest", CardSubtype.Forest, facade.Alice);
        var forest2 = BuildBasicLand("Forest", CardSubtype.Forest, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(forest1);
        facade.Alice.Zones.Battlefield.AddCard(forest2);

        facade.Alice.Zones.Library.RemoveCard(bear);
        facade.Alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        await facade.StartAsync();
        var aliceId = facade.Alice.Id;

        await facade.SubmitAsync(new CastSpellCommand(
            CardInstanceId: bear.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null)
        { PlayerId = aliceId });

        // Auto-pay: respond to the cost-payment prompt with NO sources.
        await facade.SubmitAsync(new ChooseManaCommand(Array.Empty<Guid>())
        { PlayerId = aliceId });

        facade.Alice.Zones.Hand.GetCards().Should().NotContain(bear,
            "auto-tap covered {1}{G} so the spell reached the stack/battlefield.");
        (forest1.IsTapped && forest2.IsTapped).Should().BeTrue(
            "both Forests were auto-tapped to pay {1}{G}.");
        facade.Alice.ManaPool.IsEmpty.Should().BeTrue(
            "the generated GG exactly paid {1}{G}.");
    }

    // ── 2. Pool partially covers → auto-tap covers the remainder ────────

    [Fact]
    public async Task Cast_PoolPartiallyCovers_AutoTapsForGenericRemainder()
    {
        // {1}{G}, one floating G already covers the colored pip. One untapped
        // Forest auto-taps to cover the generic {1}.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { bear }, Array.Empty<ICard>());

        var forest = BuildBasicLand("Forest", CardSubtype.Forest, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(forest);

        facade.Alice.Zones.Library.RemoveCard(bear);
        facade.Alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);
        facade.Alice.AddManaToPool(ManaCost.Parse("G"));

        await facade.StartAsync();
        var aliceId = facade.Alice.Id;

        await facade.SubmitAsync(new CastSpellCommand(
            CardInstanceId: bear.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null)
        { PlayerId = aliceId });

        await facade.SubmitAsync(new ChooseManaCommand(Array.Empty<Guid>())
        { PlayerId = aliceId });

        facade.Alice.Zones.Hand.GetCards().Should().NotContain(bear,
            "floating G + auto-tapped Forest covered {1}{G}.");
        forest.IsTapped.Should().BeTrue("the Forest auto-tapped for the generic {1}.");
        facade.Alice.ManaPool.IsEmpty.Should().BeTrue("all generated/floating mana spent.");
    }

    // ── 3. Colored matching → tap the right land ────────────────────────

    [Fact]
    public async Task Cast_ColoredCost_AutoTapsMatchingColorSource()
    {
        // {R} with an untapped Forest AND an untapped Mountain. Auto-select
        // must tap the Mountain (produces R) and leave the Forest untapped.
        var goblin = new Creature("Goblin", "R", 1, 1);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { goblin }, Array.Empty<ICard>());

        var forest = BuildBasicLand("Forest", CardSubtype.Forest, facade.Alice);
        var mountain = BuildBasicLand("Mountain", CardSubtype.Mountain, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(forest);
        facade.Alice.Zones.Battlefield.AddCard(mountain);

        facade.Alice.Zones.Library.RemoveCard(goblin);
        facade.Alice.Zones.Hand.AddCard(goblin);
        goblin.SetZone(ZoneType.Hand);

        await facade.StartAsync();
        var aliceId = facade.Alice.Id;

        await facade.SubmitAsync(new CastSpellCommand(
            CardInstanceId: goblin.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null)
        { PlayerId = aliceId });

        await facade.SubmitAsync(new ChooseManaCommand(Array.Empty<Guid>())
        { PlayerId = aliceId });

        mountain.IsTapped.Should().BeTrue("the Mountain produces the {R} the cost needs.");
        forest.IsTapped.Should().BeFalse("the Forest can't pay {R}, so it stays untapped.");
        facade.Alice.Zones.Hand.GetCards().Should().NotContain(goblin);
    }

    // ── 4. Insufficient sources → fail gracefully, nothing tapped ───────

    [Fact]
    public async Task Cast_InsufficientSources_FailsGracefully_NothingTapped()
    {
        // {G}{G} but only one untapped Forest. Auto-select can't cover the
        // cost → cast fails; the spell stays/returns to hand and NO land is
        // tapped (atomicity — Pay simulates before committing).
        var hydra = new Creature("Greenwheel Hydra", "GG", 3, 3);
        var facade = GameFacade.Create("Alice", "Bob",
            new ICard[] { hydra }, Array.Empty<ICard>());

        var forest = BuildBasicLand("Forest", CardSubtype.Forest, facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(forest);

        facade.Alice.Zones.Library.RemoveCard(hydra);
        facade.Alice.Zones.Hand.AddCard(hydra);
        hydra.SetZone(ZoneType.Hand);

        await facade.StartAsync();
        var aliceId = facade.Alice.Id;

        await facade.SubmitAsync(new CastSpellCommand(
            CardInstanceId: hydra.InstanceId,
            TargetInstanceIds: Array.Empty<Guid>(),
            XValue: null,
            ModeIndex: null)
        { PlayerId = aliceId });

        await facade.SubmitAsync(new ChooseManaCommand(Array.Empty<Guid>())
        { PlayerId = aliceId });

        facade.Alice.Zones.Hand.GetCards().Should().Contain(hydra,
            "auto-select couldn't cover {G}{G}, so the cast failed and the spell stays in hand.");
        forest.IsTapped.Should().BeFalse(
            "atomicity — Pay simulates before tapping, so a failed pay leaves the land untapped.");
        facade.Alice.ManaPool.IsEmpty.Should().BeTrue("no mana was generated for a failed cast.");
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static Land BuildBasicLand(string name, CardSubtype subtype, Player controller)
    {
        var land = new Land(
            name,
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { subtype });
        land.SetOwner(controller);
        land.ChangeController(controller);
        land.SetZone(ZoneType.Battlefield);
        OracleManaBinder.BindBasicLandMana(land, controller);
        return land;
    }
}
