using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the split card Boom // Bust (Planeshift, {1}{R} // {5}{R}).
///
/// Oracle text (verified against Scryfall):
///   Boom {1}{R} — Sorcery: "Destroy target land you control and target land
///     you don't control."
///   Bust {5}{R} — Sorcery: "Destroy all lands."
///
/// Split cards present each half as its own castable face (CR 712.2 — a
/// split card has two faces on one card; the caster picks one face to cast,
/// and only that face's cost / effect applies). Both faces are Sorceries.
///
/// This factory follows the two-face posture of
/// <see cref="SinkIntoStuporFactory"/>: the combined card name is the
/// <c>[CardName]</c> dispatch key (matching the seed row "Boom // Bust"),
/// the card SHAPE is built from the embedded JSON definition, and each
/// face's resolve-time <see cref="SpellDefinition"/> is built on demand.
///
/// Covers:
///   - Card identity (Sorcery, combined card name) + dispatch.
///   - Boom: two 1..1 target-land requests (one you control, one you don't);
///     resolve destroys both (CR 701.7 → owner's graveyard); illegal picks
///     (wrong controller side / non-land) do nothing (CR 608.2b).
///   - Boom candidate gatherer: "land you control" offers only the caster's
///     lands; "land you don't control" offers only opponents' lands.
///   - Bust: symmetric "destroy all lands" sweep across every supplied
///     player's battlefield (Armageddon-style), regardless of controller.
/// </summary>
[Trait("Color", "R")]
public class BoomBustFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BoomBust_IsSorcery_WithBoomFrontFaceCost()
    {
        var card = BoomBustFactory.Create(_alice);

        card.Name.Should().Be("Boom // Bust");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        // The combined card carries the front (Boom) face mana cost.
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void BoomBust_IsRed()
    {
        var card = BoomBustFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
    }

    // -----------------------------------------------------------------------
    // Boom — resolve destroys both lands (CR 701.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void Boom_DestroysBothLands_MovesToOwnersGraveyards()
    {
        var mine = NewLand(_alice);
        var theirs = NewLand(_bob);

        var def = BoomBustFactory.BuildBoomDefinition(_alice, raw => raw);
        def.TargetRequests.Should().HaveCount(2,
            "Boom targets a land you control AND a land you don't control");

        // Targets[0] = "land you control", Targets[1] = "land you don't control".
        ResolveTwo(def, mine, theirs);

        mine.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(mine);
        theirs.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(theirs);
    }

    [Fact]
    public void Boom_WrongControllerSide_DoesNothingForThatTarget()
    {
        // CR 608.2b — "target land you control" must be controlled by the
        // caster; an opponent's land in that slot is illegal → no destroy.
        // "target land you don't control" must NOT be the caster's; the
        // caster's own land in that slot is illegal → no destroy.
        var theirs = NewLand(_bob);
        var mine = NewLand(_alice);

        var def = BoomBustFactory.BuildBoomDefinition(_alice, raw => raw);
        // Slots intentionally swapped: opponent land in the "you control"
        // slot, own land in the "you don't control" slot.
        ResolveTwo(def, theirs, mine);

        theirs.Zone.Should().Be(ZoneType.Battlefield,
            "opponent land is illegal in the 'land you control' slot");
        mine.Zone.Should().Be(ZoneType.Battlefield,
            "own land is illegal in the 'land you don't control' slot");
    }

    [Fact]
    public void Boom_NonLandTarget_DoesNothing()
    {
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(bear);
        var theirs = NewLand(_bob);

        var def = BoomBustFactory.BuildBoomDefinition(_alice, raw => raw);
        ResolveTwo(def, bear, theirs);

        bear.Zone.Should().Be(ZoneType.Battlefield, "a creature is not a land");
        theirs.Zone.Should().Be(ZoneType.Graveyard, "the legal land target is destroyed");
    }

    // -----------------------------------------------------------------------
    // Boom — candidate gatherers split by control relative to the caster
    // -----------------------------------------------------------------------

    [Fact]
    public void Boom_CandidateGatherers_SplitByControl()
    {
        var mine = NewLand(_alice);
        var theirs = NewLand(_bob);
        var myBear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(myBear);

        var def = BoomBustFactory.BuildBoomDefinition(_alice, raw => raw);
        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: PhaseStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var youControl = def.TargetRequests[0].ResolveCandidates(ctx);
        var youDontControl = def.TargetRequests[1].ResolveCandidates(ctx);

        youControl.Should().Contain(mine, "your land is a 'land you control' candidate");
        youControl.Should().NotContain(theirs);
        youControl.Should().NotContain(myBear, "only lands are candidates");

        youDontControl.Should().Contain(theirs, "opponent land is a 'land you don't control' candidate");
        youDontControl.Should().NotContain(mine);
    }

    // -----------------------------------------------------------------------
    // Bust — destroy all lands (symmetric sweep)
    // -----------------------------------------------------------------------

    [Fact]
    public void Bust_DestroysAllLands_BothPlayers()
    {
        var aLand1 = NewLand(_alice);
        var aLand2 = NewLand(_alice);
        var bLand = NewLand(_bob);
        var aBear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield };
        _alice.Zones.Battlefield.AddCard(aBear);

        var effects = BoomBustFactory.BuildBustResolveEffect(new[] { _alice, _bob });
        foreach (var e in effects) e.Execute();

        aLand1.Zone.Should().Be(ZoneType.Graveyard);
        aLand2.Zone.Should().Be(ZoneType.Graveyard);
        bLand.Zone.Should().Be(ZoneType.Graveyard, "Bust is symmetric — hits every player's lands");
        aBear.Zone.Should().Be(ZoneType.Battlefield, "Bust destroys only lands");
    }

    // -----------------------------------------------------------------------
    // helpers
    // -----------------------------------------------------------------------

    private static Land NewLand(Player owner)
    {
        var land = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        owner.Zones.Battlefield.AddCard(land);
        return land;
    }

    private static void ResolveTwo(SpellDefinition def, object target0, object target1)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new[] { target0 }, new[] { target1 } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
