using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BontusLastReckoningFactory"/>.
///
/// Card: Bontu's Last Reckoning — {1}{B}{B} Sorcery (Hour of Devastation).
/// Oracle text (verified against Scryfall):
///   "Destroy all creatures. Lands you control don't untap during your
///    next untap step."
///
/// Covers the card's UNIQUE behaviour:
/// - Clause 1: symmetric "destroy all creatures" sweep across every supplied
///   player's battlefield, routed to owners' graveyards (CR 701.7), non-
///   creatures left alone.
/// - Clause 2: lands the caster controls are marked to skip the caster's next
///   untap step (CR 502.1 via UntapStepRestrictions); opponent lands and the
///   caster's non-lands are not marked.
/// - One-shot cleanup (bus-wired): the caster's next Untap step lifts the
///   skip; an opponent's Untap step does not.
/// Plus a single identity assert ({1}{B}{B}, black, Sorcery).
/// </summary>
[Trait("Color", "B")]
public class BontusLastReckoningFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public void Dispose() => UntapStepRestrictions.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BontusLastReckoning_Identity()
    {
        var card = BontusLastReckoningFactory.Create(_alice);

        card.Name.Should().Be("Bontu's Last Reckoning");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}{B}");
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Clause 1 — Destroy all creatures (CR 701.7, symmetric)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DestroysAllCreatures_BothPlayers_NonCreaturesUntouched()
    {
        var aBear = NewCreature(_alice, "Alice Bear");
        var bBear = NewCreature(_bob, "Bob Bear");
        var aLand = NewLand(_alice);

        var effects = BontusLastReckoningFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob }, eventBus: null);
        foreach (var e in effects) e.Execute();

        aBear.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aBear);
        bBear.Zone.Should().Be(ZoneType.Graveyard, "the sweep is symmetric (CR 700.3)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bBear);
        aLand.Zone.Should().Be(ZoneType.Battlefield, "Bontu's destroys only creatures");
    }

    // -----------------------------------------------------------------------
    // Clause 2 — Lands you control skip your next untap step (CR 502.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_MarksCasterLands_ToSkipNextUntapStep_OnlyCasterLands()
    {
        var myLand1 = NewLand(_alice);
        var myLand2 = NewLand(_alice);
        var oppLand = NewLand(_bob);

        var effects = BontusLastReckoningFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob }, eventBus: null);
        foreach (var e in effects) e.Execute();

        UntapStepRestrictions.ShouldSkipUntap(myLand1, _alice).Should().BeTrue(
            "lands you control skip your next untap step (CR 502.1)");
        UntapStepRestrictions.ShouldSkipUntap(myLand2, _alice).Should().BeTrue();
        UntapStepRestrictions.ShouldSkipUntap(oppLand, _bob).Should().BeFalse(
            "only LANDS YOU CONTROL are affected, not the opponent's lands");
    }

    [Fact]
    public void Resolve_DoesNotMarkCasterNonLands()
    {
        // The caster's surviving artifacts/etc. are not lands — they untap
        // normally. (Use an artifact so it survives the destroy-all-creatures
        // clause.)
        var myArtifact = new Artifact("Mox", "{0}");
        myArtifact.SetOwner(_alice);
        myArtifact.SetController(_alice);
        myArtifact.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(myArtifact);

        var effects = BontusLastReckoningFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob }, eventBus: null);
        foreach (var e in effects) e.Execute();

        UntapStepRestrictions.ShouldSkipUntap(myArtifact, _alice).Should().BeFalse(
            "the skip is restricted to lands you control, not other permanents");
    }

    // -----------------------------------------------------------------------
    // One-shot cleanup via bus (CR 502.1 / "your next untap step")
    // -----------------------------------------------------------------------

    [Fact]
    public void BusWired_SkipLiftsAfterCastersNextUntapStep()
    {
        var myLand = NewLand(_alice);

        var effects = BontusLastReckoningFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob }, eventBus: _bus);
        foreach (var e in effects) e.Execute();

        UntapStepRestrictions.ShouldSkipUntap(myLand, _alice).Should().BeTrue();

        // Caster's next Untap step fires — skip is lifted.
        _bus.Publish(new StepStartedEvent(StepStateType.Untap, _alice));

        UntapStepRestrictions.ShouldSkipUntap(myLand, _alice).Should().BeFalse(
            "the skip lifts after the caster's next untap step (CR 502.1)");
    }

    [Fact]
    public void BusWired_OpponentUntapStep_SkipPersists()
    {
        var myLand = NewLand(_alice);

        var effects = BontusLastReckoningFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob }, eventBus: _bus);
        foreach (var e in effects) e.Execute();

        // Bob's untap step fires — must NOT clear Alice's land restriction.
        _bus.Publish(new StepStartedEvent(StepStateType.Untap, _bob));

        UntapStepRestrictions.ShouldSkipUntap(myLand, _alice).Should().BeTrue(
            "only YOUR next untap step lifts the skip, not an opponent's");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature NewCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static Land NewLand(Player owner)
    {
        var land = new Land("Swamp", new[] { CardSupertype.Basic }, new[] { CardSubtype.Swamp });
        land.SetOwner(owner);
        land.SetController(owner);
        land.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }
}
