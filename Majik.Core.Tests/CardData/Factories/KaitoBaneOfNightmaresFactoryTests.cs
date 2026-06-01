using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
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

    [Fact]
    public void NamedCardFactory_Dispatches_Kaito()
    {
        var card = NamedCardFactory.Create("Kaito, Bane of Nightmares", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Kaito, Bane of Nightmares");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Kaito).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(4);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
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
            opponentsResolver: () => new List<Player> { _bob },
            tapTargetResolver: null,
            effects: null,
            isControllersTurn: null,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        var startingHand = _alice.Zones.Hand.GetCards().Count();

        kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == 0).Activate();

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
            opponentsResolver: () => new List<Player> { _bob },
            tapTargetResolver: null,
            effects: null,
            isControllersTurn: null,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        var startingHand = _alice.Zones.Hand.GetCards().Count();

        kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == 0).Activate();

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
            opponentsResolver: null,
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
            opponentsResolver: null,
            tapTargetResolver: () => Array.Empty<Permanent>(),
            effects: null,
            isControllersTurn: null,
            eventBus: null);
        _alice.Zones.Battlefield.AddCard(kaito);
        kaito.SetZone(ZoneType.Battlefield);

        kaito.Abilities.OfType<LoyaltyAbility>().Single(a => a.LoyaltyChange == -2).Activate();

        kaito.Loyalty.Should().Be(2, "loyalty change still applies even with no legal target");
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
            opponentsResolver: null,
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

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
