using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MwonvuliAcidMossFactory"/> — Time Spiral ({2}{G}{G})
/// Sorcery.
///
/// Oracle text (verified against Scryfall):
///   "Destroy target land. Search your library for a Forest card, put that
///    card onto the battlefield tapped, then shuffle."
///
/// Shares the "destroy target land" front half with
/// <see cref="CleansingWildfireFactory"/> / <see cref="StoneRainFactory"/>,
/// and the put-onto-battlefield-tapped tutor back half with Cleansing
/// Wildfire's compensation search — but here the searcher is the CASTER, the
/// search is MANDATORY (no "may"), and the predicate is the Forest land
/// subtype (basic Forest or a nonbasic Forest dual), not a basic-land name.
///
/// Covers (UNIQUE behaviour only — CardFactoryContractTests already asserts
/// dispatch + well-formedness):
/// - Identity ({2}{G}{G} mono-green Sorcery, mana value 4).
/// - Candidate gatherer: only land permanents (own + opponent) offered.
/// - Resolve destroys the target land (CR 701.7b) AND tutors a Forest onto
///   the battlefield tapped, then shuffles (CR 701.19a / 701.20a).
/// - Nonbasic Forest dual qualifies for the tutor (subtype, not name).
/// - Non-land target → no destroy (CR 608.2b), but the Forest tutor still
///   resolves (CR 608.2e — independent clauses).
/// - No Forest in library → no permanent enters, but the search still shuffles.
/// </summary>
[Trait("Color", "G")]
public class MwonvuliAcidMossFactoryTests : IDisposable
{
    public MwonvuliAcidMossFactoryTests() => AgentRegistry.Clear();

    public void Dispose() => AgentRegistry.Clear();

    private static Land BasicLand(string name, CardSubtype sub, Player p)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { sub })
        {
            Owner = p,
            Controller = p,
        };
        return land;
    }

    private static Land Forest(Player p) => BasicLand("Forest", CardSubtype.Forest, p);

    private static Land NonbasicForestDual(Player p)
    {
        // A nonbasic land carrying the Forest subtype (e.g. Stomping Ground).
        var land = new Land("Stomping Ground", Array.Empty<CardSupertype>(),
            new[] { CardSubtype.Mountain, CardSubtype.Forest })
        {
            Owner = p,
            Controller = p,
        };
        return land;
    }

    [Fact]
    public void MwonvuliAcidMoss_Identity_TwoGGSorcery_ManaValueFour_MonoGreen()
    {
        var alice = new Player("Alice", 20);
        var card = MwonvuliAcidMossFactory.Create(alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Mwonvuli Acid-Moss");
        card.ManaCost.Should().Be("{2}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        ManaCost.Parse(card.ManaCost).TotalValue.Should().Be(4,
            "{2}{G}{G} — generic 2 + two green = MV 4 (CR 202.3)");

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColorEnum.Green, "Mwonvuli Acid-Moss has {G} pips");
        colors.Should().NotContain(ManaColorEnum.Red);
        colors.Should().NotContain(ManaColorEnum.Blue);
        colors.Should().NotContain(ManaColorEnum.White);
        colors.Should().NotContain(ManaColorEnum.Black);
    }

    [Fact]
    public void MwonvuliAcidMoss_CandidateGatherer_OnlyLandPermanents()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bobIsland = BasicLand("Island", CardSubtype.Island, bob);
        bobIsland.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobIsland);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bobBear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bobBear);

        var aliceMountain = BasicLand("Mountain", CardSubtype.Mountain, alice);
        aliceMountain.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(aliceMountain);

        var stack = new Majik.Core.Stack.Stack();
        var def = MwonvuliAcidMossFactory.BuildDefinition(alice, o => o);
        var ctx = new GameContext(
            self: alice,
            allPlayers: new[] { alice, bob },
            activePlayer: alice,
            turnNumber: 1,
            currentPhase: Majik.Core.StateMachine.StepStateType.PreCombatMain,
            stack: stack);

        var candidates = def.TargetRequests[0].ResolveCandidates(ctx);

        candidates.Should().Contain(bobIsland);
        candidates.Should().Contain(aliceMountain, "no 'opponent' restriction — own lands are legal");
        candidates.Should().NotContain(bobBear, "creatures are not lands");
    }

    [Fact]
    public void MwonvuliAcidMoss_Resolve_DestroysTargetLand_AndTutorsForestOntoBattlefieldTapped()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = BasicLand("Island", CardSubtype.Island, bob);
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var forest = Forest(alice);
        alice.Zones.Library.AddCard(forest);

        var def = MwonvuliAcidMossFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard, "CR 701.7b — destroyed land → owner's graveyard");
        bob.Zones.Graveyard.GetCards().Should().Contain(island);

        forest.Zone.Should().Be(ZoneType.Battlefield, "the tutored Forest is put onto the battlefield");
        alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        forest.IsTapped.Should().BeTrue("the Forest enters tapped per oracle text");
        alice.Zones.Library.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void MwonvuliAcidMoss_Resolve_NonbasicForestDual_QualifiesForTutor()
    {
        // "Forest card" is a SUBTYPE match — a nonbasic land carrying the
        // Forest type (Stomping Ground) is a legal find, unlike a basic-NAME
        // tutor (CR 305.6 land types vs. card names).
        var alice = new Player("Alice", 20);
        var dual = NonbasicForestDual(alice);
        alice.Zones.Library.AddCard(dual);

        var def = MwonvuliAcidMossFactory.BuildDefinition(alice, o => o);
        // No target — exercises only the tutor clause (illegal/empty destroy).
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { alice } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        dual.Zone.Should().Be(ZoneType.Battlefield, "a nonbasic Forest dual carries the Forest subtype");
        dual.IsTapped.Should().BeTrue("enters tapped");
    }

    [Fact]
    public void MwonvuliAcidMoss_Resolve_NonLandTarget_NoDestroy_ButStillTutors()
    {
        // CR 608.2b — illegal (non-land) target → no destroy. The Forest
        // tutor clause still resolves (CR 608.2e — independent clauses).
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = bob, Controller = bob };
        bear.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(bear);

        var forest = Forest(alice);
        alice.Zones.Library.AddCard(forest);

        var def = MwonvuliAcidMossFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { bear } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield, "non-land target is illegal (CR 608.2b)");
        forest.Zone.Should().Be(ZoneType.Battlefield, "the Forest tutor clause still resolves");
        forest.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void MwonvuliAcidMoss_Resolve_NoForestInLibrary_NoPermanentEnters_LibraryUnchanged()
    {
        // CR 701.19a — a search may find nothing when no card qualifies; the
        // library is still shuffled (CR 701.20a), no permanent enters.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var island = BasicLand("Island", CardSubtype.Island, bob);
        island.SetZone(ZoneType.Battlefield);
        bob.Zones.Battlefield.AddCard(island);

        var swamp = BasicLand("Swamp", CardSubtype.Swamp, alice);
        alice.Zones.Library.AddCard(swamp);

        var def = MwonvuliAcidMossFactory.BuildDefinition(alice, o => o);
        var chosen = new ChosenSpellParams(null, null, new[] { new object[] { island } }, ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        island.Zone.Should().Be(ZoneType.Graveyard);
        swamp.Zone.Should().NotBe(ZoneType.Battlefield, "a Swamp is not a Forest card");
        alice.Zones.Library.GetCards().Should().Contain(swamp, "no Forest to find → library keeps the Swamp");
        alice.Zones.Battlefield.GetCards().Should().BeEmpty("nothing was tutored");
    }
}
