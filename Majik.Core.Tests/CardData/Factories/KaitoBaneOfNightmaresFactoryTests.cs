using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Tests.Helpers;
using Majik.Core.Primitives;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Kaito, Bane of Nightmares ({2}{U}{B}).
///
/// Oracle text (Scryfall, verified):
///   "Ninjutsu {1}{U}{B}
///    During your turn, as long as Kaito has one or more loyalty counters on
///    him, he's a 3/4 Ninja creature and has hexproof.
///    +1: You get an emblem with 'Ninjas you control get +1/+1.'
///    0: Surveil 2. Then draw a card for each opponent who lost life this turn.
///    −2: Tap target creature. Put two stun counters on it."
///
/// Covers:
///   - Card identity (Legendary Planeswalker — Kaito, starting loyalty 4,
///     mana cost {2}{U}{B}), materialised from the embedded JSON definition.
///   - The three loyalty abilities (+1 / 0 / −2).
///   - +1: a "Ninjas you control get +1/+1" emblem enters the command zone.
///   - 0: surveil 2, then draw one card per opponent who lost life this turn.
///   - −2: tap target creature and put two stun counters on it.
///   - The "becomes a 3/4 Ninja creature during your turn while it has
///     loyalty" animation (Layer 4 type-grant + Layer 7b P/T + hexproof),
///     registered against the continuous-effects service, mirroring the
///     Mutavault manland posture.
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "M")]
public class KaitoBaneOfNightmaresFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Kaito_IsLegendaryPlaneswalker_Kaito_4Loyalty_AtCost2UB()
    {
        var kaito = KaitoBaneOfNightmaresFactory.Create(_alice);

        kaito.Name.Should().Be("Kaito, Bane of Nightmares");
        kaito.ManaCost.Should().Be("{2}{U}{B}");
        kaito.HasType(CardType.Planeswalker).Should().BeTrue();
        kaito.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        kaito.HasSubtype(CardSubtype.Kaito).Should().BeTrue();
        kaito.Loyalty.Should().Be(4);
        kaito.StartingLoyalty.Should().Be(4);
        kaito.Owner.Should().BeSameAs(_alice);
        kaito.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Kaito_HasThreeLoyaltyAbilities_Plus1_Zero_Minus2()
    {
        var kaito = KaitoBaneOfNightmaresFactory.Create(_alice);

        var loyalty = kaito.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalty.Should().HaveCount(3);
        loyalty.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, 0, -2 });
    }

    [Fact]
    public void Kaito_HasNinjutsuMarker_At1UB()
    {
        var kaito = KaitoBaneOfNightmaresFactory.Create(_alice);

        // CR 702.49 — Kaito carries a Ninjutsu {1}{U}{B} marker.
        var ninjutsu = kaito.Abilities
            .OfType<Majik.Core.Keywords.NinjutsuAbility>()
            .SingleOrDefault();
        ninjutsu.Should().NotBeNull("Kaito has Ninjutsu");
        ninjutsu!.ManaCost.Should().Be(
            Majik.Core.ValueObjects.ManaCost.Parse("{1}{U}{B}"),
            "Kaito's printed ninjutsu cost is {1}{U}{B}");
    }
    // -----------------------------------------------------------------------
    // +1: emblem "Ninjas you control get +1/+1"
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_CreatesNinjaAnthemEmblem_AndAddsLoyalty()
    {
        var kaito = KaitoBaneOfNightmaresFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        _alice.Emblems.Should().BeEmpty();

        kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        kaito.Loyalty.Should().Be(5, "4 + 1 = 5");
        _alice.Emblems.Should().HaveCount(1, "the +1 gives you an emblem");
        _alice.Emblems[0].SourceName.Should().Contain("Ninja");
    }

    // -----------------------------------------------------------------------
    // 0: Surveil 2. Then draw a card for each opponent who lost life this turn.
    // -----------------------------------------------------------------------

    [Fact]
    public void Zero_DrawsOneCard_WhenOneOpponentLostLifeThisTurn()
    {
        // Bob (an opponent) lost life this turn.
        _bob.LoseLife(3);
        _bob.LifeLostThisTurn.Should().BeGreaterThan(0);

        // Two cards in Alice's library: top for surveil (kept), then a draw.
        var top = new Card("Top", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);
        var drawTarget = new Card("Draw Me", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(drawTarget);
        drawTarget.SetZone(ZoneType.Library);

        var kaito = KaitoBaneOfNightmaresFactory.Create(
            _alice,
            tapTargetResolver: null,
            effects: null,
            isControllersTurn: null,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        var startingHand = _alice.Zones.Hand.GetCards().Count();

        // Resolve the 0 ability through a live game context (resolver-null
        // bug-class fix — the draw-for-each-opponent clause reads rc.Game).
        Majik.Core.Tests.Helpers.ContextResolve.Resolve(
            kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == 0),
            _alice, _alice, _bob);

        kaito.Loyalty.Should().Be(4, "0: leaves loyalty unchanged");
        _alice.Zones.Hand.GetCards().Count().Should().Be(startingHand + 1,
            "one opponent lost life this turn ⇒ draw one card");
    }

    [Fact]
    public void Zero_DrawsNoCards_WhenNoOpponentLostLifeThisTurn()
    {
        var top = new Card("Top", "{1}") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var kaito = KaitoBaneOfNightmaresFactory.Create(
            _alice,
            tapTargetResolver: null,
            effects: null,
            isControllersTurn: null,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        var startingHand = _alice.Zones.Hand.GetCards().Count();

        Majik.Core.Tests.Helpers.ContextResolve.Resolve(
            kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == 0),
            _alice, _alice, _bob);

        _alice.Zones.Hand.GetCards().Count().Should().Be(startingHand,
            "no opponent lost life ⇒ no card drawn");
    }

    // -----------------------------------------------------------------------
    // −2: Tap target creature. Put two stun counters on it.
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus2_TapsTargetCreature_AndPutsTwoStunCounters()
    {
        var bear = new Creature("Untapped Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob); bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);
        bear.IsTapped.Should().BeFalse();

        var kaito = KaitoBaneOfNightmaresFactory.Create(
            _alice,
            tapTargetResolver: () => new Permanent[] { bear },
            effects: null,
            isControllersTurn: null,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        kaito.Loyalty.Should().Be(2, "4 - 2 = 2");
        bear.IsTapped.Should().BeTrue("−2 taps the target creature");
        bear.Counters.Count(CounterType.Stun).Should().Be(2,
            "−2 puts two stun counters on the target");
    }

    [Fact]
    public void Minus2_NoTarget_NoOp_ButStillSpendsLoyalty()
    {
        var kaito = KaitoBaneOfNightmaresFactory.Create(
            _alice,
            tapTargetResolver: () => Array.Empty<Permanent>(),
            effects: null,
            isControllersTurn: null,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        kaito.Loyalty.Should().Be(2, "loyalty change still applies even with no legal target");
    }

    [Fact]
    public void Minus2_DeclaresTargetCreatureRequest()
    {
        // CR 602.2b — the −2 is a TARGETED loyalty ability ("Tap target
        // creature"). It must declare a real TargetRequest so the loyalty
        // dispatch path prompts the activating player's agent (mirroring
        // Grist's −2 destroy-target / Teferi's −3 nonland-permanent target),
        // not the captured tapTargetResolver (null on the routed prod build).
        var kaito = KaitoBaneOfNightmaresFactory.Create(_alice);

        var minus2 = kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2);
        minus2.TargetRequests.Should().ContainSingle(
            "the −2 taps target creature — a single real target");
        var req = minus2.TargetRequests[0];
        req.MinTargets.Should().Be(1, "\"target creature\" is mandatory (CR 602.2b)");
        req.MaxTargets.Should().Be(1, "a single target creature");
    }

    [Fact]
    public void Minus2_GathererOffersOnlyBattlefieldCreatures()
    {
        // The −2 candidate gatherer offers EVERY battlefield creature (any
        // controller — "target creature" is not restricted to opponents'),
        // and nothing that is not a creature.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob); bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear); bear.SetZone(ZoneType.Battlefield);

        var myBear = new Creature("Watchwolf", "{G}{W}", 3, 3);
        myBear.SetOwner(_alice); myBear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(myBear); myBear.SetZone(ZoneType.Battlefield);

        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(_alice); rock.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(rock); rock.SetZone(ZoneType.Battlefield);

        var kaito = KaitoBaneOfNightmaresFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kaito); kaito.SetZone(ZoneType.Battlefield);

        var minus2 = kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2);
        var game = ContextResolve.Game(_alice, _alice, _bob);
        var candidates = minus2.TargetRequests[0].CandidateGatherer!(game);

        candidates.Should().Contain(bear, "an opponent's creature is a legal target");
        candidates.Should().Contain(myBear, "\"target creature\" can be your own creature");
        candidates.Should().NotContain(rock, "an artifact is not a creature");
    }

    [Fact]
    public void Minus2_TapsChosenTargetCreature_OnProdBuild()
    {
        // PROD-PATH guard (the resolver-null bug class). The routed prod build
        // resolves the loyalty ability through the stack
        // (TurnDriver.DispatchLoyalty → ActivatedAbility.ResolveAsync) with the
        // agent-chosen target threaded via SetChosenTargets — NOT the captured
        // tapTargetResolver, which is null on the NamedCardFactory build and
        // made the −2 INERT in real games. Kaito built via NamedCardFactory
        // must tap + stun the CHOSEN creature.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob); bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear); bear.SetZone(ZoneType.Battlefield);
        bear.IsTapped.Should().BeFalse();

        var built = NamedCardFactory.Create("Kaito, Bane of Nightmares", _alice);
        built.Should().BeOfType<Planeswalker>();
        var kaito = (Planeswalker)built;
        _alice.Zones.Battlefield.AddCard(kaito); kaito.SetZone(ZoneType.Battlefield);

        var minus2 = kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2);
        minus2.PayLoyaltyCost();
        ResolveLoyaltyWithChosenTarget(minus2, _alice, bear, _alice, _bob);

        kaito.Loyalty.Should().Be(2, "4 - 2 = 2");
        bear.IsTapped.Should().BeTrue("the prod-built −2 taps the CHOSEN creature (not inert)");
        bear.Counters.Count(CounterType.Stun).Should().Be(2,
            "the −2 puts two stun counters on the chosen creature");
    }

    [Fact]
    public void Minus2_Fizzles_WhenChosenTargetLeftBattlefield_OnProdBuild()
    {
        // CR 608.2b — re-check the target's legality on resolution. A creature
        // that has left the battlefield (or is no longer a creature) before the
        // −2 resolves is no longer tapped/stunned, but the loyalty cost is
        // already paid (CR 606.3).
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob); bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear); bear.SetZone(ZoneType.Battlefield);

        var built = NamedCardFactory.Create("Kaito, Bane of Nightmares", _alice);
        var kaito = (Planeswalker)built;
        _alice.Zones.Battlefield.AddCard(kaito); kaito.SetZone(ZoneType.Battlefield);

        // Target leaves the battlefield in response (e.g. bounced/killed).
        _bob.Zones.Battlefield.RemoveCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var minus2 = kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2);
        minus2.PayLoyaltyCost();
        ResolveLoyaltyWithChosenTarget(minus2, _alice, bear, _alice, _bob);

        kaito.Loyalty.Should().Be(2, "loyalty cost is paid even though the target is gone");
        bear.Counters.Count(CounterType.Stun).Should().Be(0,
            "a target that left the battlefield is not stunned (CR 608.2b)");
    }

    /// <summary>
    /// Resolve a loyalty ability's effects through the async resolution path
    /// with the agent-chosen target threaded onto the stack object's
    /// <c>ChosenTargets</c> (slot 0), mirroring how
    /// <c>TurnDriver.DispatchLoyalty</c> collects the loyalty ability's
    /// <see cref="TargetRequest"/>, prompts the agent, and calls
    /// <c>SetChosenTargets</c> before resolving.
    /// </summary>
    private static void ResolveLoyaltyWithChosenTarget(
        LoyaltyAbility loyalty, Player controller, Permanent chosen, params Player[] players)
    {
        var game = new Majik.Core.Game.GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var stackObject = new ActivatedAbility(
            source: loyalty.Source,
            controller: controller,
            costs: null,
            effects: loyalty.Effects);
        stackObject.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { chosen } });
        stackObject.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }

    // -----------------------------------------------------------------------
    // Static: becomes a 3/4 Ninja creature during your turn while it has loyalty
    // -----------------------------------------------------------------------

    [Fact]
    public void Animation_RegistersAnimateAndPTEffects_WhenServiceWired()
    {
        var effects = new ContinuousEffectsService();
        var controllersTurn = true;

        var kaito = KaitoBaneOfNightmaresFactory.Create(
            _alice,
            tapTargetResolver: null,
            effects: effects,
            isControllersTurn: () => controllersTurn,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        // The two continuous effects (Layer 4 type-grant + Layer 7b 3/4) are
        // registered up-front; their IsActive() gates the animation on
        // controller's-turn + loyalty > 0.
        var animate = GetRegisteredEffects(effects)
            .OfType<KaitoAnimateEffect>().SingleOrDefault();
        animate.Should().NotBeNull("the Layer-4 Ninja-creature grant is registered");

        // During your turn with loyalty > 0 ⇒ active.
        animate!.IsActive().Should().BeTrue(
            "during the controller's turn while Kaito has loyalty, he is a creature");

        // Not your turn ⇒ inactive.
        controllersTurn = false;
        animate.IsActive().Should().BeFalse("only a creature during your turn");

        // Your turn but no loyalty ⇒ inactive.
        controllersTurn = true;
        kaito.RemoveLoyalty(kaito.Loyalty);
        animate.IsActive().Should().BeFalse("only a creature while it has loyalty counters");
    }

    // -----------------------------------------------------------------------
    // +1 emblem anthem registers LIVE into the per-game service (CR 114 / 613.7c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_RegistersLiveNinjaAnthem_WhenServiceWired()
    {
        var effects = new ContinuousEffectsService();

        var kaito = KaitoBaneOfNightmaresFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        // A Ninja Alice controls, before the +1.
        var ninja = new Creature("Ninja", "{1}{U}", 2, 2,
            subtypes: new[] { CardSubtype.Ninja });
        ninja.SetOwner(_alice); ninja.SetController(_alice);
        ninja.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(ninja);
        ninja.ActiveEffects = effects;

        ninja.GetPower().Should().Be(2, "before the +1 there is no Ninja anthem");

        kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        // The emblem exists AND its anthem is live in the service.
        _alice.Emblems.Should().HaveCount(1);
        GetRegisteredEffects(effects).OfType<EmblemAnthemEffect>().Should().ContainSingle(
            "the +1 emblem's \"Ninjas you control get +1/+1\" anthem auto-registers live");

        ninja.GetPower().Should().Be(3, "the live emblem anthem now boosts Alice's Ninja");
        ninja.GetToughness().Should().Be(3);
    }

    [Fact]
    public void Plus1_AnthemDoesNotRegister_WhenNoServiceWired()
    {
        var kaito = KaitoBaneOfNightmaresFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == +1).Activate();

        // Without a service the emblem is still minted (structural), but no
        // continuous effect to register — and no NRE.
        _alice.Emblems.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Animated Kaito's 3/4 surfaces through Compute (CR 613.1c/613.7b, #1720)
    // -----------------------------------------------------------------------

    [Fact]
    public void Animation_AnimatedKaito_ComputesAs3_4NinjaCreature()
    {
        var effects = new ContinuousEffectsService();

        var kaito = KaitoBaneOfNightmaresFactory.Create(
            _alice,
            tapTargetResolver: null,
            effects: effects,
            isControllersTurn: () => true,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        // During Alice's turn with loyalty > 0 ⇒ the Layer-4 Creature grant +
        // Layer-7b 3/4 set-base are active; the #1720 creature-row upgrade
        // re-seeds this Planeswalker instance as a creature row.
        var chars = effects.Compute(kaito);

        chars.Should().BeOfType<CreatureCharacteristics>(
            "the Layer-4 Ninja-creature grant upgrades the row (CR 613.1c)");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Planeswalker, "still a planeswalker (additive)");
        chars.Subtypes.Should().Contain(CardSubtype.Ninja);

        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(3, "Kaito's 3/4 set-base surfaces through Compute");
        cc.Toughness.Should().Be(4);
    }

    [Fact]
    public void Animation_KaitoNotAnimated_WhenNotControllersTurn_ComputesAsNonCreatureRow()
    {
        var effects = new ContinuousEffectsService();

        var kaito = KaitoBaneOfNightmaresFactory.Create(
            _alice,
            tapTargetResolver: null,
            effects: effects,
            isControllersTurn: () => false, // opponent's turn ⇒ not animated,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        var chars = effects.Compute(kaito);

        chars.Should().NotBeOfType<CreatureCharacteristics>(
            "outside your turn Kaito is not a creature (no P/T row)");
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Planeswalker);
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
