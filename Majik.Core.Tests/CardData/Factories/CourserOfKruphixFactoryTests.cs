using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="CourserOfKruphixFactory"/> (Born of the Gods, {1}{G}{G}).
///
/// Card: Courser of Kruphix — Enchantment Creature — Centaur 2/4.
///   "Play with the top card of your library revealed.
///    You may play lands from the top of your library.
///    Landfall — Whenever a land you control enters, you gain 1 life."
///
/// Covers identity + dispatch, the description riders, the battlefield-gated
/// play-lands-from-top + reveal grant (CR 601.3e / CR 305.6 / CR 715.4)
/// registered/revoked via the bus lifecycle, the land-from-top play advancing
/// the revealed top, the landfall +1 life trigger, and the nonland-top
/// non-playability.
/// </summary>
[Trait("Color", "G")]
public class CourserOfKruphixFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => LibraryTopPlayPermissions.Clear();

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, EventBus bus) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, bus);
    }

    private static Land NewLand(Player owner, string name = "Forest")
    {
        var land = new Land(name, subtypes: new[] { CardSubtype.Forest });
        land.SetOwner(owner);
        return land;
    }

    /// <summary>Put <paramref name="card"/> in <paramref name="owner"/>'s hand,
    /// then move it to the battlefield via the bus so the lifecycle re-syncs
    /// (mirrors a real ETB; the source-move event registers the grant).</summary>
    private static void EnterBattlefield(ZoneService zones, Player owner, ICard card)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        zones.MoveCardTo(card, ZoneType.Battlefield, controller: owner);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Courser_Identity_EnchantmentCreatureCentaur_2_4_At1GG()
    {
        var courser = CourserOfKruphixFactory.Create(_alice);

        courser.Name.Should().Be("Courser of Kruphix");
        courser.ManaCost.Should().Be("{1}{G}{G}");
        courser.HasType(CardType.Creature).Should().BeTrue();
        courser.HasType(CardType.Enchantment).Should().BeTrue();
        courser.HasSubtype(CardSubtype.Centaur).Should().BeTrue();
        courser.BasePower.Should().Be(2);
        courser.BaseToughness.Should().Be(4);
        courser.Owner.Should().BeSameAs(_alice);
        courser.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Courser()
    {
        var card = NamedCardFactory.Create("Courser of Kruphix", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Courser of Kruphix");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasSubtype(CardSubtype.Centaur).Should().BeTrue();
    }

    [Fact]
    public void Courser_HasRiders_RevealTop_PlayLands_Landfall()
    {
        var courser = CourserOfKruphixFactory.Create(_alice);

        var statics = courser.Abilities.OfType<StaticAbility>().Select(s => s.Description).ToList();
        statics.Should().Contain(CourserOfKruphixFactory.RevealTopDescription);
        statics.Should().Contain(CourserOfKruphixFactory.PlayLandsFromTopDescription);

        courser.Abilities.OfType<TriggeredAbility>().Should().ContainSingle(
            "the landfall lifegain is the single triggered ability");
    }

    // -----------------------------------------------------------------------
    // Battlefield-gated play-from-top + reveal grant
    // -----------------------------------------------------------------------

    [Fact]
    public void Courser_OnBattlefield_TopLand_IsPlayableAndRevealed()
    {
        var (zones, _, triggers, bus) = BuildEngine();
        var courser = CourserOfKruphixFactory.Create(_alice, bus, triggers);

        // Top of Alice's library is a land.
        var forest = NewLand(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // Before Courser is on the battlefield: no permission.
        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeFalse();

        // Courser enters the battlefield (via the bus so the lifecycle re-syncs).
        EnterBattlefield(zones, _alice, courser);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeTrue(
            "Courser grants 'may play lands from the top of your library'");
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeTrue(
            "Courser plays with the top card revealed (CR 715.4)");
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeSameAs(forest);
    }

    [Fact]
    public void Courser_OnBattlefield_TopNonLand_NotPlayable()
    {
        var (zones, _, triggers, bus) = BuildEngine();
        var courser = CourserOfKruphixFactory.Create(_alice, bus, triggers);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        EnterBattlefield(zones, _alice, courser);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, bolt).Should().BeFalse(
            "Courser only lets you play LANDS from the top, not a nonland");
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeNull();
        // But the top is still revealed.
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeTrue();
    }

    [Fact]
    public void Courser_LeavesBattlefield_PermissionRevoked()
    {
        var (zones, _, triggers, bus) = BuildEngine();
        var courser = CourserOfKruphixFactory.Create(_alice, bus, triggers);

        var forest = NewLand(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        EnterBattlefield(zones, _alice, courser);
        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeTrue();

        // Courser dies / leaves — grant revoked (CR 603.6e).
        zones.MoveCardTo(courser, ZoneType.Graveyard, controller: _alice);

        LibraryTopPlayPermissions.MayPlayTopCard(_alice, forest).Should().BeFalse(
            "the grant ends when Courser leaves the battlefield");
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Playing the top land: advances the revealed top + landfall +1 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Courser_PlayTopLand_AdvancesRevealedTop_AndGainsOneLife()
    {
        var (zones, stack, triggers, bus) = BuildEngine();
        var courser = CourserOfKruphixFactory.Create(_alice, bus, triggers);
        EnterBattlefield(zones, _alice, courser);

        // Library top is a land; a second card sits beneath it.
        var topForest = NewLand(_alice, "Forest");
        var nextCard = new Instant("Opt", "{U}");
        nextCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topForest);   // top
        topForest.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(nextCard);    // second
        nextCard.SetZone(ZoneType.Library);

        // The top land is the legal land-from-top play this turn.
        LibraryTopPlayPermissions.PlayableLandFromTop(_alice).Should().BeSameAs(topForest);

        var lifeBefore = _alice.LifeTotal;

        // Play the top land — it is played from the library (the land-play path
        // moves a land from whatever zone it occupies). CR 601.3e.
        zones.MoveCardTo(topForest, ZoneType.Battlefield, controller: _alice);

        // Landfall queued — resolve it for +1 life (CR 614).
        triggers.PendingCount.Should().Be(1, "landfall fires when the land enters");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(lifeBefore + CourserOfKruphixFactory.LifeGainPerLandfall,
            "Courser's landfall gains 1 life");

        // The played land left the library; the next card is now the revealed top.
        _alice.Zones.Library.GetCards().Should().NotContain(topForest);
        CourserOfKruphixFactory.RevealedTopCard(_alice).Should().BeSameAs(nextCard,
            "after playing the top land the next card becomes the revealed top");
        topForest.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Courser_Landfall_DoesNotFireForOpponentLand()
    {
        var (zones, _, triggers, bus) = BuildEngine();
        var courser = CourserOfKruphixFactory.Create(_alice, bus, triggers);
        EnterBattlefield(zones, _alice, courser);

        var swamp = new Land("Swamp");
        swamp.SetOwner(_bob);
        swamp.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(swamp);

        zones.MoveCardTo(swamp, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "Courser's landfall only triggers on a land YOU control entering");
    }
}
