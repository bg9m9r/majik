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

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Cyclonic Rift (Return to Ravnica, {1}{U}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Return target nonland permanent you don't control to its owner's hand.
///    Overload {6}{U} (You may cast this spell for its overload cost. If you
///    do, change "target" in its text to "each.")"
///
/// After the CR 702.96b substitution, the overloaded cast reads:
///   "Return each nonland permanent you don't control to its owner's hand."
///
/// Covers:
///   - Card identity (Instant, {1}{U}, Blue, owner/controller).
///   - NamedCardFactory dispatch (Create materialises the embedded JSON shape).
///   - SpellDefinition shape: single 1..1 "target nonland permanent you don't
///     control" request; candidate gatherer excludes lands (CR 305) and the
///     controller's own permanents (CR 109.5 — "you" = the spell's controller).
///   - Default (not overloaded) resolve → bounces one targeted nonland
///     permanent the controller does NOT control (CR 701.20).
///   - No-op if target is a land (wrong type — CR 305 / CR 608.2b).
///   - No-op if target is the controller's own permanent (CR 109.5).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
///   - Structural overloaded branch → bounces EACH nonland permanent the
///     controller does NOT control; the controller's own permanents + lands
///     are untouched (CR 702.96b).
///
/// Overload (CR 702.96) is an alternative cost. Per <c>MODERN_COVERAGE.md</c>
/// and the <see cref="VandalblastFactory"/> analogue, the
/// <see cref="Majik.Core.Costs.OverloadAlternativeCost"/> primitive is a stub
/// not yet plumbed through <see cref="Majik.Core.Services.SpellCastFlow"/>, so
/// production casts ship not-overloaded. The overloaded branch is exercised
/// here by passing <c>wasOverloaded: true</c> through the spell-definition
/// builder directly (same posture as Vandalblast).
/// </summary>
[Trait("Color", "U")]
public class CyclonicRiftFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CyclonicRift_HasInstantShape_Blue_AtCost1U()
    {
        var card = CyclonicRiftFactory.Create(_alice);

        card.Name.Should().Be("Cyclonic Rift");
        card.ManaCost.Should().Be("{1}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape (default / not overloaded)
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellDefinition_DeclaresSingleNonlandPermanentTargetRequest()
    {
        var def = CyclonicRiftFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("nonland permanent");
    }

    [Fact]
    public void CandidateGatherer_ExcludesControllersOwnPermanents_AndLands()
    {
        // CR 109.5 / oracle "you don't control": only opponents' permanents
        // are legal candidates. CR 305: lands are excluded ("nonland").
        var bobCreature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobLand = NewControlledPermanent<Land>(_bob, "Island", "");
        var aliceCreature = NewControlledPermanent<Creature>(_alice, "Llanowar Elves", "{G}", 1, 1);

        var def = CyclonicRiftFactory.BuildDefinition(_alice, o => o);
        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(bobCreature);
        candidates.Should().NotContain(bobLand,
            because: "Cyclonic Rift returns a NONLAND permanent (CR 305)");
        candidates.Should().NotContain(aliceCreature,
            because: "Cyclonic Rift targets a permanent you DON'T control (CR 109.5)");
    }

    // -----------------------------------------------------------------------
    // Default (not overloaded) resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void BouncesTargetedOpponentPermanent_ToOwnersHand()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(creature, wasOverloaded: false);

        creature.Zone.Should().Be(ZoneType.Hand,
            because: "Cyclonic Rift returns target nonland permanent you don't control to its owner's hand (CR 701.20)");
        _bob.Zones.Hand.GetCards().Should().Contain(creature,
            because: "it is returned to ITS OWNER's hand");
    }

    [Fact]
    public void DoesNotBounce_ControllersOwnPermanent()
    {
        // Even if (illegally) resolved against the controller's own permanent,
        // the "you don't control" clause is re-checked at resolution.
        var aliceCreature = NewControlledPermanent<Creature>(_alice, "Llanowar Elves", "{G}", 1, 1);

        Resolve(aliceCreature, wasOverloaded: false);

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Cyclonic Rift cannot bounce a permanent you control (CR 109.5)");
    }

    [Fact]
    public void TargetLand_DoesNothing()
    {
        var land = NewControlledPermanent<Land>(_bob, "Island", "");

        Resolve(land, wasOverloaded: false);

        land.Zone.Should().Be(ZoneType.Battlefield,
            because: "Cyclonic Rift returns NONLAND permanents only (CR 305 / CR 608.2b)");
    }

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature, wasOverloaded: false);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // -----------------------------------------------------------------------
    // Overloaded branch (structural — CR 702.96b)
    // -----------------------------------------------------------------------

    [Fact]
    public void Overloaded_BouncesEachNonlandPermanent_YouDontControl()
    {
        // Bob (opponent) nonland permanents — all bounced.
        var bobBear = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobSolRing = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        // Bob's land — spared (CR 305 — "each NONLAND permanent").
        var bobIsland = NewControlledPermanent<Land>(_bob, "Island", "");

        // Alice (controller) permanent — spared (CR 702.96b "each nonland
        // permanent you don't control"; the controller is the "you" per CR 109.5).
        var aliceElf = NewControlledPermanent<Creature>(_alice, "Llanowar Elves", "{G}", 1, 1);

        var def = CyclonicRiftFactory.BuildDefinition(
            controller: _alice,
            targetResolver: o => o,
            allPlayers: new[] { _alice, _bob },
            zoneService: null,
            wasOverloaded: true);

        // No targets — overloaded branch carries no TargetRequests
        // (CR 702.96b — "target" is rewritten to "each").
        def.TargetRequests.Count.Should().Be(0);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen)) fx.Execute();

        bobBear.Zone.Should().Be(ZoneType.Hand, "opponent nonland permanents are bounced");
        bobSolRing.Zone.Should().Be(ZoneType.Hand, "opponent nonland permanents are bounced");
        bobIsland.Zone.Should().Be(ZoneType.Battlefield,
            "lands are untouched (CR 305 — each NONLAND permanent)");
        aliceElf.Zone.Should().Be(ZoneType.Battlefield,
            "the controller's own permanents are spared (CR 109.5)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(ICard target, bool wasOverloaded)
    {
        var def = CyclonicRiftFactory.BuildDefinition(
            _alice, o => o, new[] { _alice, _bob }, zoneService: null, wasOverloaded);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static T NewControlledPermanent<T>(Player owner, string name, string cost,
        int power = 0, int toughness = 0)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Creature))
        {
            card = (T)(ICard)new Creature(name, cost, power, toughness);
        }
        else if (typeof(T) == typeof(Artifact))
        {
            card = (T)(ICard)new Artifact(name, cost);
        }
        else if (typeof(T) == typeof(Enchantment))
        {
            card = (T)(ICard)new Enchantment(name, cost);
        }
        else if (typeof(T) == typeof(Land))
        {
            card = (T)(ICard)new Land(name);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
