using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tibalt's Trickery (Kaldheim, {R}, Instant).
///
/// Oracle text:
///   "Counter target spell. Its controller mills three cards, then exiles
///    cards from the top of their library until they exile a nonland card
///    that shares a card type with it. They may cast that card without
///    paying its mana cost. Then they put all cards exiled this way that
///    weren't cast on the bottom of their library in a random order."
///
/// Covers:
///   - Card shape (Instant, {R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Counter target spell + mill 3 (CR 701.5 + CR 701.13).
///   - Exile-until-shares-type: walks library, exiles up to and including
///     the first nonland card matching one of the countered spell's card
///     types; remaining go to the bottom (CR 308.2).
///   - Mono-land library: walks the whole library, no eligible card, all
///     bottomed.
///   - onResolved callback fires with the eligible card; if the callback
///     moves the eligible card out of exile, it is NOT bottomed.
/// </summary>
public class TibaltsTrickeryFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public TibaltsTrickeryFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TibaltsTrickery_IsInstant_AtCostR()
    {
        var t = TibaltsTrickeryFactory.Create(_alice);

        t.Name.Should().Be("Tibalt's Trickery");
        t.ManaCost.Should().Be("{R}");
        t.HasType(CardType.Instant).Should().BeTrue();
        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(t).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TibaltsTrickery()
    {
        var card = NamedCardFactory.Create("Tibalt's Trickery", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Tibalt's Trickery");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — counter + mill 3 + exile-until-shares-type
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Resolve_CountersTargetSpell_AndMillsThree_AndExilesUntilSharedType()
    {
        // Bob casts Lightning Bolt (Instant). Tibalt's Trickery counters it,
        // Bob mills 3, then exiles from top of Bob's library until a nonland
        // instant/sorcery/creature/etc. shows up that shares a type. We seed
        // a library so the FIRST nonland Bob exiles is a Sorcery (shares
        // Instant with the bolt? No — Sorcery does NOT share with Instant).
        // Layout from top → bottom:
        //   [Mountain (Land), Forest (Land), Counterspell (Instant)]
        // First nonland matching "Instant" → Counterspell.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var mountainMill1 = new Card("MillMtn1", "0", new[] { CardType.Land });
        var mountainMill2 = new Card("MillMtn2", "0", new[] { CardType.Land });
        var mountainMill3 = new Card("MillMtn3", "0", new[] { CardType.Land });
        SeedLibrary(_bob, mountainMill1, mountainMill2, mountainMill3); // these 3 will be milled

        var topLand1 = new Card("Mountain", "0", new[] { CardType.Land });
        var topLand2 = new Card("Forest", "0", new[] { CardType.Land });
        var matchingInstant = new Card("Counterspell", "{U}{U}", new[] { CardType.Instant });
        matchingInstant.SetOwner(_bob);
        topLand1.SetOwner(_bob);
        topLand2.SetOwner(_bob);
        AppendLibrary(_bob, topLand1, topLand2, matchingInstant);

        await CastTrickery(target: bobSpell);
        _resolver.ResolveTop(_stack);

        // Bolt countered: in graveyard, off the stack.
        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Tibalt's Trickery counters the target spell (CR 701.5)");
        _stack.GetAll().Should().NotContain(s => ReferenceEquals(s, bobSpell));

        // Mill 3: the three Mountain mill seeds are in Bob's graveyard.
        _bob.Zones.Graveyard.GetCards().Should().Contain(new[] { mountainMill1, mountainMill2, mountainMill3 });

        // Exile walk: topLand1 + topLand2 + matchingInstant were exiled in
        // that order. matchingInstant is the eligible card. Since no
        // onResolved hook is wired, all three are bottomed (random order),
        // so they end up in the library (not exile).
        topLand1.Zone.Should().Be(ZoneType.Library);
        topLand2.Zone.Should().Be(ZoneType.Library);
        matchingInstant.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_MonolandLibrary_NoEligible_AllBottomed()
    {
        // Library = only Forests. Tibalt's Trickery counters bolt + mills
        // 3, then exiles ALL remaining lands (no eligible nonland found)
        // and bottoms them.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // 3 to be milled, 2 to be exile-walked.
        var mill1 = new Card("Mill1", "0", new[] { CardType.Land });
        var mill2 = new Card("Mill2", "0", new[] { CardType.Land });
        var mill3 = new Card("Mill3", "0", new[] { CardType.Land });
        var walk1 = new Card("Walk1", "0", new[] { CardType.Land });
        var walk2 = new Card("Walk2", "0", new[] { CardType.Land });
        mill1.SetOwner(_bob); mill2.SetOwner(_bob); mill3.SetOwner(_bob);
        walk1.SetOwner(_bob); walk2.SetOwner(_bob);
        SeedLibrary(_bob, mill1, mill2, mill3);
        AppendLibrary(_bob, walk1, walk2);

        await CastTrickery(target: bobSpell);
        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard);

        // Milled.
        _bob.Zones.Graveyard.GetCards().Should().Contain(new[] { mill1, mill2, mill3 });
        // Walk targets ended up bottom-of-library (no eligible found).
        walk1.Zone.Should().Be(ZoneType.Library);
        walk2.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_OnResolvedCallback_ReceivesEligible_AndCanKeepInExile()
    {
        // Wire onResolved to remove the eligible card from exile (simulating
        // a free cast). The bottom step must then NOT move that card back
        // to the library — only the non-eligible exiled cards (and only
        // those that remain in exile) get bottomed.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var mill1 = new Card("M1", "0", new[] { CardType.Land });
        var mill2 = new Card("M2", "0", new[] { CardType.Land });
        var mill3 = new Card("M3", "0", new[] { CardType.Land });
        mill1.SetOwner(_bob); mill2.SetOwner(_bob); mill3.SetOwner(_bob);
        SeedLibrary(_bob, mill1, mill2, mill3);

        var dud = new Card("DudLand", "0", new[] { CardType.Land });
        var eligible = new Card("OtherInstant", "{1}{U}", new[] { CardType.Instant });
        dud.SetOwner(_bob);
        eligible.SetOwner(_bob);
        AppendLibrary(_bob, dud, eligible);

        TibaltsTrickeryFactory.TrickeryResolution? observed = null;
        Action<TibaltsTrickeryFactory.TrickeryResolution> onResolved = res =>
        {
            observed = res;
            // Simulate a free cast: move eligible card out of exile (to a
            // notional "stack" — here we just send it to hand for assertion
            // simplicity). The factory checks zone == Exile before bottoming.
            if (res.Eligible != null)
            {
                _bob.Zones.Exile.RemoveCard(res.Eligible);
                _bob.Zones.Hand.AddCard(res.Eligible);
                res.Eligible.SetZone(ZoneType.Hand);
            }
        };

        await CastTrickery(target: bobSpell, onResolved: onResolved);
        _resolver.ResolveTop(_stack);

        observed.Should().NotBeNull();
        observed!.CounteredSpell.Should().BeSameAs(bobSpell);
        observed.Eligible.Should().BeSameAs(eligible);
        observed.Exiled.Should().Equal(new[] { dud, eligible });

        // Eligible was relocated to hand by the callback — and stays there.
        eligible.Zone.Should().Be(ZoneType.Hand);
        // dud was bottomed.
        dud.Zone.Should().Be(ZoneType.Library);
        // Nothing left in exile.
        _bob.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_SharedTypeBindsToFirstMatching_LaterCardsNotExiled()
    {
        // Sanity that the exile walk STOPS at the first eligible card.
        // Bolt is an Instant. Library walk order: [Land, Creature, Land,
        // Instant]. Creature does NOT share Instant → keep going. Next is
        // Land (skip nonland check), then we'd reach Instant — but wait,
        // Creature does not share with bolt (Instant), so we keep exiling.
        // Next is Land — nonland predicate excludes Land — keep going.
        // Then Instant → eligible.
        //
        // The Instant after the second Land should be eligible; the [Land]
        // after Instant in the list should NEVER be exiled.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var millA = new Card("MA", "0", new[] { CardType.Land });
        var millB = new Card("MB", "0", new[] { CardType.Land });
        var millC = new Card("MC", "0", new[] { CardType.Land });
        millA.SetOwner(_bob); millB.SetOwner(_bob); millC.SetOwner(_bob);
        SeedLibrary(_bob, millA, millB, millC);

        var walkLand1 = new Card("WL1", "0", new[] { CardType.Land });
        var walkCreature = new Card("Bear", "{1}{G}", new[] { CardType.Creature });
        var walkLand2 = new Card("WL2", "0", new[] { CardType.Land });
        var walkInstant = new Card("Bolt2", "{R}", new[] { CardType.Instant });
        var untouchedLand = new Card("Untouched", "0", new[] { CardType.Land });
        foreach (var c in new[] { walkLand1, walkCreature, walkLand2, walkInstant, untouchedLand }) c.SetOwner(_bob);
        AppendLibrary(_bob, walkLand1, walkCreature, walkLand2, walkInstant, untouchedLand);

        TibaltsTrickeryFactory.TrickeryResolution? observed = null;
        await CastTrickery(target: bobSpell, onResolved: r => observed = r);
        _resolver.ResolveTop(_stack);

        observed.Should().NotBeNull();
        observed!.Eligible.Should().BeSameAs(walkInstant);
        observed.Exiled.Should().Equal(new[] { walkLand1, walkCreature, walkLand2, walkInstant });

        // untouchedLand was never exiled — still in library (somewhere).
        untouchedLand.Zone.Should().Be(ZoneType.Library);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Tibalt's Trickery from Alice's hand at <paramref name="target"/>
    /// (a stacked spell). Uses a seeded GameRandom so bottom order is
    /// deterministic across test runs.
    /// </summary>
    private async Task CastTrickery(
        object target,
        Action<TibaltsTrickeryFactory.TrickeryResolution>? onResolved = null)
    {
        var t = TibaltsTrickeryFactory.Create(_alice);
        t.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(t);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.PreCombatMain, _stack);

        var random = new GameRandom(seed: 42);

        await _flow.CastAsync(
            _alice, t,
            TibaltsTrickeryFactory.BuildSpellDefinition(
                resolver: o => o,
                stack: _stack,
                onResolved: onResolved,
                random: random),
            agent, ctx);
    }

    private static void SeedLibrary(Player player, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            c.SetOwner(player);
            player.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }

    /// <summary>Append in oracle order (later cards sit lower in library).</summary>
    private static void AppendLibrary(Player player, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            c.SetOwner(player);
            player.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }
    }
}
