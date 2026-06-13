using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Coverage for routing factory-backed NON-LAND deck cards through their
/// <c>[CardName]</c> factory in the PRODUCTION build path
/// (<see cref="GameFacade.Create"/>).
///
/// Background: per-card <c>*Factory</c> classes were historically TEST-ONLY —
/// only <c>NamedCardFactory.Create</c> dispatched to them, and prod never
/// called it. Prod cards arrived as ability-less typed shells from
/// <c>RealDeckLoader</c> and ran only the binder chain in
/// <c>GameFacade.BindCardAbilities</c>. So bespoke factory-only abilities
/// (Agatha's Soul Cauldron's {T}, Dredger's Insight's JSON ETB triggers, …)
/// did NOTHING in prod even though tests passed.
///
/// These tests drive the FIX through <see cref="GameFacade.Create"/> (the
/// prod entry point), NOT <c>NamedCardFactory</c> directly.
/// </summary>
public class FactoryRoutingTests
{
    private static ICardRepository Repo() => new EmbeddedCardRepository();

    private static ICard LibraryCardNamed(GameFacade facade, Player owner, string name) =>
        owner.Zones.GetZone(ZoneType.Library).GetCards().Single(c => c.Name == name);

    // -----------------------------------------------------------------------
    // Agatha's Soul Cauldron — bespoke {T} activated ability now lives in prod
    // -----------------------------------------------------------------------

    [Fact]
    public void Agatha_RoutedThroughFactory_HasTapActivatedAbility_AndNoCastTarget()
    {
        var deck = new List<ICard>
        {
            new Artifact("Agatha's Soul Cauldron", "{2}"),
        };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());

        var cauldron = LibraryCardNamed(facade, facade.Alice, "Agatha's Soul Cauldron");

        cauldron.Should().BeOfType<Artifact>("Agatha is an Artifact");
        cauldron.Owner.Should().BeSameAs(facade.Alice);
        cauldron.Controller.Should().BeSameAs(facade.Alice);

        cauldron.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the factory's {T}: exile-from-graveyard ability must reach prod via routing");

        // No cast-time target: the card is an artifact with NO triggered
        // ability that demands a target on entry, and the factory does not add
        // any targeted ETB. Routing must not add OracleTriggeredAbility either.
        cauldron.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "routing must NOT run the OracleTriggeredAbilityBinder for factory-backed cards");
    }

    [Fact]
    public void Agatha_RoutedThroughFactory_TapEffect_ExilesGraveyardCard_AndCounters()
    {
        var deck = new List<ICard>
        {
            new Artifact("Agatha's Soul Cauldron", "{2}"),
        };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var alice = facade.Alice;

        var cauldron = LibraryCardNamed(facade, alice, "Agatha's Soul Cauldron");

        // Move it to the battlefield (simulate it having resolved).
        alice.Zones.GetZone(ZoneType.Library).RemoveCard(cauldron);
        alice.Zones.Battlefield.AddCard(cauldron);
        cauldron.SetZone(ZoneType.Battlefield);

        // A creature card in the graveyard + a creature on the battlefield.
        var deadBear = new Creature("Dead Bear", "1G", 2, 2);
        deadBear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(deadBear);
        deadBear.SetZone(ZoneType.Graveyard);

        var liveBear = new Creature("Live Bear", "1G", 2, 2);
        liveBear.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(liveBear);
        liveBear.SetZone(ZoneType.Battlefield);

        var tap = cauldron.Abilities.OfType<ActivatedAbility>().Single();
        // Real targeting: request 0 = card to exile, request 1 = counter recipient.
        tap.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { deadBear },
            new object[] { liveBear },
        });
        foreach (var e in tap.Effects) e.Execute();

        alice.Zones.Exile.GetCards().Should().Contain(deadBear, "the {T} ability exiles a graveyard card");
        alice.Zones.Graveyard.GetCards().Should().NotContain(deadBear);
        liveBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "exiling a creature card places a +1/+1 counter");
    }

    [Fact]
    public void Agatha_FlagOff_FallsBackToBinderChain_NoTapAbility()
    {
        // Use the PER-FACADE kill-switch (routeThroughNamedFactories: false)
        // instead of mutating the process-wide static. Toggling the global here
        // used to perturb the per-game deterministic id sequence of any
        // CONCURRENTLY-building game (the cross-game id-divergence the fuzz
        // harness surfaced); the per-facade override is concurrency-safe.
        var deck = new List<ICard>
        {
            new Artifact("Agatha's Soul Cauldron", "{2}"),
        };

        var facade = GameFacade.Create(
            "Alice", "Bob", deck, Array.Empty<ICard>(),
            cardRepo: Repo(), routeThroughNamedFactories: false);
        var cauldron = LibraryCardNamed(facade, facade.Alice, "Agatha's Soul Cauldron");

        cauldron.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "with routing off, the binder chain does not synthesize Agatha's bespoke {T} ability — proving the kill-switch");
    }

    // -----------------------------------------------------------------------
    // Dredger's Insight — JSON-backed factory; both triggers reach prod
    // -----------------------------------------------------------------------

    [Fact]
    public void Dredger_RoutedThroughFactory_HasBothTriggeredAbilities()
    {
        var deck = new List<ICard>
        {
            new Enchantment("Dredger's Insight", "1G"),
        };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var dredger = LibraryCardNamed(facade, facade.Alice, "Dredger's Insight");

        dredger.Should().BeOfType<Enchantment>();
        dredger.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "the JSON-backed factory wires the ETB mill-pick trigger AND the leaves-graveyard lifegain trigger; both must reach prod");
    }

    // -----------------------------------------------------------------------
    // Regression spot-checks via GameFacade.Create (must stay green)
    // -----------------------------------------------------------------------

    [Fact]
    public void SurveilLand_NotRouted_StillEntersTapped_AndHasMana()
    {
        // Underground Mortuary is factory-backed BUT a Land — must fall
        // through to the binder chain so its enters-tapped replacement fires
        // (the land factory deliberately omits it on the dispatcher path).
        var deck = new List<ICard>();
        var land = new Land("Underground Mortuary", supertypes: null,
            subtypes: new[] { CardSubtype.Swamp, CardSubtype.Forest });
        deck.Add(land);

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var built = LibraryCardNamed(facade, facade.Alice, "Underground Mortuary");

        built.Abilities.OfType<ManaAbility>().Should().NotBeEmpty("surveil land taps for mana");

        // EntersTapped replacement is registered on the facade's ReplacementBus.
        // Run the ETB intent through it and confirm the surveil land is tapped.
        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            built, Majik.Core.Zones.ZoneType.Hand, Majik.Core.Zones.ZoneType.Battlefield,
            Controller: facade.Alice);
        var replaced = facade.Replacements.Apply(intent);
        replaced!.EntersTapped.Should().BeTrue(
            "surveil lands always enter tapped (CR 614.1c) via the binder chain");
    }

    [Fact]
    public void ShockLand_NotRouted_OffersPayTwoLifeOrTapped()
    {
        // Watery Grave is factory-backed BUT a Land — falls through to the
        // binder chain; ShockLandBinder registers the pay-2-life-or-tapped
        // replacement on the facade ReplacementBus.
        var deck = new List<ICard>();
        var land = new Land("Watery Grave", supertypes: null,
            subtypes: new[] { CardSubtype.Island, CardSubtype.Swamp });
        deck.Add(land);

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var built = LibraryCardNamed(facade, facade.Alice, "Watery Grave");

        built.Abilities.OfType<ManaAbility>().Should().NotBeEmpty("shock land taps for mana");

        // The shock-land replacement is registered on the facade bus. Drop the
        // controller to 2 life so the CR 119.4 deferral branch fires
        // deterministically (enter tapped, no agent prompt) — proving the
        // pay-2-life-or-enter-tapped replacement is live without blocking on a
        // sync-over-async agent prompt.
        facade.Alice.LoseLife(facade.Alice.LifeTotal - 2);
        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            built, Majik.Core.Zones.ZoneType.Hand, Majik.Core.Zones.ZoneType.Battlefield,
            Controller: facade.Alice);
        var replaced = facade.Replacements.Apply(intent);
        replaced!.EntersTapped.Should().BeTrue(
            "the shock land's pay-2-life-or-enter-tapped replacement must be registered (CR 614); " +
            "at 2 life the deferral makes it enter tapped");
    }

    [Fact]
    public void WallOfSwords_Routed_HasEachKeywordExactlyOnce()
    {
        // Wall of Swords is a factory-backed creature with Defender + Flying.
        // Routing builds it via the factory (which adds both keyword markers),
        // then overlays KeywordBinder. The dedup guard must prevent doubling.
        var deck = new List<ICard>
        {
            new Creature("Wall of Swords", "3W", 3, 5),
        };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var wall = LibraryCardNamed(facade, facade.Alice, "Wall of Swords");

        var keywords = wall.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Count(k => k.Keyword.Equals("Defender", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "Defender must appear exactly once (no double-add from the binder overlay)");
        keywords.Count(k => k.Keyword.Equals("Flying", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "Flying must appear exactly once (no double-add from the binder overlay)");
    }

    [Fact]
    public void ElvishMystic_Routed_HasExactlyOneManaAbility()
    {
        // Elvish Mystic is a factory-backed creature with {T}: Add {G}.
        // The factory adds the mana ability; OracleManaBinder would add a
        // second from oracle text. The dedup guard must prevent doubling.
        var deck = new List<ICard>
        {
            new Creature("Elvish Mystic", "G", 1, 1),
        };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var mystic = LibraryCardNamed(facade, facade.Alice, "Elvish Mystic");

        mystic.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "exactly one {T}: Add {G} — the binder overlay must not double the factory's mana ability");
    }

    // -----------------------------------------------------------------------
    // Walking Ballista — enters with X +1/+1 counters (CR 614.1d / 202.3b).
    // The prod routed build (NamedCardFactory.Create → WalkingBallistaFactory)
    // then OverlayAdditiveBinders must register the variable-X ETB-counter
    // replacement reading PendingCastX, so a cast Ballista enters with the
    // chosen X counters instead of always 0/0.
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_RoutedThroughFactory_EntersWithXCounters()
    {
        var deck = new List<ICard>
        {
            new Creature("Walking Ballista", "{X}{X}", 0, 0),
        };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var ballista = (Creature)LibraryCardNamed(facade, facade.Alice, "Walking Ballista");

        // Cast-time X = 3 (paid {3}{3}); SpellCastFlow stamps PendingCastX.
        ((Card)ballista).SetPendingCastX(3);

        // Run the ETB intent through the facade ReplacementBus (the same bus
        // OverlayAdditiveBinders registered the variable-X counter replacement
        // on), then place the counters as the permanent enters.
        ballista.SetController(facade.Alice);

        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            ballista, ZoneType.Hand, ZoneType.Battlefield, Controller: facade.Alice);
        var replaced = facade.Replacements.Apply(intent);

        replaced.Should().NotBeNull();
        replaced!.PlusOneCountersOnEnter.Should().Be(3,
            "Walking Ballista enters with X (=3) +1/+1 counters (CR 614.1d), not 0/0");
    }

    [Fact]
    public void WalkingBallista_RoutedThroughFactory_ZeroX_EntersWithNoCounters()
    {
        var deck = new List<ICard>
        {
            new Creature("Walking Ballista", "{X}{X}", 0, 0),
        };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var ballista = (Creature)LibraryCardNamed(facade, facade.Alice, "Walking Ballista");
        // X = 0: no PendingCastX stamp.

        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            ballista, ZoneType.Hand, ZoneType.Battlefield, Controller: facade.Alice);
        var replaced = facade.Replacements.Apply(intent);

        replaced.Should().NotBeNull();
        replaced!.PlusOneCountersOnEnter.Should().Be(0,
            "X = 0 → enters as a 0/0 (which the SBA layer then sends to the graveyard)");
    }

    // -----------------------------------------------------------------------
    // CR 712.3 — MDFC cast-either-face routing (deferral #3). The seed stores
    // "Sink into Stupor // Soporific Springs" under the composite name; the
    // production deck-build must route the FRONT face through its factory so
    // the card carries the castable back-face descriptor.
    // -----------------------------------------------------------------------

    [Fact]
    public void Mdfc_SinkIntoStupor_RoutedThroughFrontFactory_CarriesCastableBackFace()
    {
        var deck = new List<ICard>
        {
            // Composite name exactly as a real decklist / seed row carries it.
            new Instant("Sink into Stupor // Soporific Springs", "{1}{U}{U}"),
        };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());

        // The routed card's Name is the FRONT face (the front factory sets it),
        // NOT the composite — GetByName resolves it back to the composite row.
        var lib = facade.Alice.Zones.GetZone(ZoneType.Library).GetCards();
        lib.Should().ContainSingle().Which.Name.Should().Be("Sink into Stupor",
            "the MDFC front factory names the card after the front face");
        var sink = lib.Single();

        sink.Should().BeOfType<Instant>("the front face is the instant");
        var mdfc = ((Card)sink).MdfcState;
        mdfc.Should().NotBeNull("a routed MDFC front carries the face tracker");
        mdfc!.CanCastEitherFace.Should().BeTrue(
            "production deck-build must attach the castable back-face descriptor (CR 712.3)");
        mdfc.CastableBackFace.Should().NotBeNull();
        mdfc.CastableBackFace!.IsLand.Should().BeTrue("Soporific Springs is a land back face");
        mdfc.CastableBackFace!.Name.Should().Be("Soporific Springs");
    }

    // -----------------------------------------------------------------------
    // ETB-X +1/+1 counters cohort — the same prod-routing bug Walking Ballista
    // had (#2635). A card whose factory wired its X +1/+1 counters as a
    // self-managed ETB TriggeredAbility + MarkSelfManagesEntersWithCounters()
    // enters with ZERO counters on the PROD Approach-B route: that route builds
    // via NamedCardFactory.Create with no TriggerManager (the ETB trigger never
    // fires) AND the self-manage flag suppresses the one mechanism the route
    // DOES run (EntersWithCountersBinder). The fix defers entirely to the
    // binder, which reads PendingCastX and stamps
    // ZoneMoveIntent.PlusOneCountersOnEnter (CR 614.1d).
    //
    // These tests drive the prod ReplacementBus the way the Walking Ballista
    // routing tests do: build through GameFacade.Create, stamp PendingCastX,
    // then Apply the ETB ZoneMoveIntent and assert PlusOneCountersOnEnter.
    // -----------------------------------------------------------------------

    private static int EntersWithCountersOnRoute(string name, string manaCost, int? castX)
    {
        var deck = new List<ICard> { new Creature(name, manaCost, 0, 0) };

        var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
        var card = (Creature)LibraryCardNamed(facade, facade.Alice, name);

        if (castX is int x) ((Card)card).SetPendingCastX(x);
        card.SetController(facade.Alice);

        var intent = new Majik.Core.Effects.ZoneMoveIntent(
            card, ZoneType.Hand, ZoneType.Battlefield, Controller: facade.Alice);
        var replaced = facade.Replacements.Apply(intent);

        replaced.Should().NotBeNull();
        return replaced!.PlusOneCountersOnEnter;
    }

    [Fact]
    public void EndlessOne_RoutedThroughFactory_EntersWithXCounters() =>
        EntersWithCountersOnRoute("Endless One", "{X}", castX: 3).Should().Be(3,
            "Endless One enters with X (=3) +1/+1 counters (CR 614.1d), not 0/0");

    [Fact]
    public void EndlessOne_RoutedThroughFactory_ZeroX_EntersWithNoCounters() =>
        EntersWithCountersOnRoute("Endless One", "{X}", castX: null).Should().Be(0,
            "X = 0 → enters as a 0/0 (which the SBA layer then sends to the graveyard)");

    [Fact]
    public void HangarbackWalker_RoutedThroughFactory_EntersWithXCounters() =>
        EntersWithCountersOnRoute("Hangarback Walker", "{X}{X}", castX: 3).Should().Be(3,
            "Hangarback Walker enters with X (=3) +1/+1 counters (CR 614.1d), not 0/0");

    [Fact]
    public void HangarbackWalker_RoutedThroughFactory_ZeroX_EntersWithNoCounters() =>
        EntersWithCountersOnRoute("Hangarback Walker", "{X}{X}", castX: null).Should().Be(0,
            "X = 0 → enters as a 0/0 (which the SBA layer then sends to the graveyard)");

    [Fact]
    public void StonecoilSerpent_RoutedThroughFactory_EntersWithXCounters() =>
        EntersWithCountersOnRoute("Stonecoil Serpent", "{X}", castX: 3).Should().Be(3,
            "Stonecoil Serpent enters with X (=3) +1/+1 counters (CR 614.1d), not 0/0");

    [Fact]
    public void StonecoilSerpent_RoutedThroughFactory_ZeroX_EntersWithNoCounters() =>
        EntersWithCountersOnRoute("Stonecoil Serpent", "{X}", castX: null).Should().Be(0,
            "X = 0 → enters as a 0/0 (which the SBA layer then sends to the graveyard)");

    [Fact]
    public void TheGooseMother_RoutedThroughFactory_EntersWithXCounters() =>
        EntersWithCountersOnRoute("The Goose Mother", "{X}{G}{U}", castX: 3).Should().Be(3,
            "The Goose Mother enters with X (=3) +1/+1 counters (CR 614.1d), not just base 2/2");

    [Fact]
    public void TheGooseMother_RoutedThroughFactory_ZeroX_EntersWithNoCounters() =>
        EntersWithCountersOnRoute("The Goose Mother", "{X}{G}{U}", castX: null).Should().Be(0,
            "X = 0 → enters with no +1/+1 counters (base 2/2, CR 614.1d)");
}
