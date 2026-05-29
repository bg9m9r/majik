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
        foreach (var e in tap.Effects) e.Execute();

        alice.Zones.Exile.GetCards().Should().Contain(deadBear, "the {T} ability exiles a graveyard card");
        alice.Zones.Graveyard.GetCards().Should().NotContain(deadBear);
        liveBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "exiling a creature card places a +1/+1 counter");
    }

    [Fact]
    public void Agatha_FlagOff_FallsBackToBinderChain_NoTapAbility()
    {
        var prev = GameFacade.RouteThroughNamedFactories;
        try
        {
            GameFacade.RouteThroughNamedFactories = false;

            var deck = new List<ICard>
            {
                new Artifact("Agatha's Soul Cauldron", "{2}"),
            };

            var facade = GameFacade.Create("Alice", "Bob", deck, Array.Empty<ICard>(), cardRepo: Repo());
            var cauldron = LibraryCardNamed(facade, facade.Alice, "Agatha's Soul Cauldron");

            cauldron.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
                "with routing off, the binder chain does not synthesize Agatha's bespoke {T} ability — proving the kill-switch");
        }
        finally
        {
            GameFacade.RouteThroughNamedFactories = prev;
        }
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
}
