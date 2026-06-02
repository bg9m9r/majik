using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MortuaryMireFactory"/> (Battle for Zendikar).
///
/// Land. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you may put target creature card from your
///    graveyard on top of your library.
///    {T}: Add {B}."
///
/// Mechanically the graveyard-recursion sibling of
/// <see cref="MysticSanctuaryFactory"/> — an ETB triggered ability that puts a
/// targeted card from the controller's graveyard on top of their library —
/// except the target is a <b>creature</b> card, the action is unconditional
/// and optional ("you may", CR 603.5), and the body is a single {B} mana
/// ability (no land subtype).
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - One single-colour mana ability — {B} (CR 605.1a).
/// - One battlefield-active ETB triggered ability with a 0..1 target request.
/// - ETB resolution moves the chosen creature card graveyard → top of library.
/// - "You may" with no target chosen is a no-op (CR 603.5).
/// - Illegal-on-resolution recheck: a noncreature target is ignored (CR 608.2b).
///
/// Unconditional enters-tapped (CR 614.1c) is registered on the wired path
/// via <see cref="Majik.Core.Effects.EntersTappedReplacement"/>, same posture
/// as <see cref="BloodfellCavesFactory"/>; the shape-only dispatcher path
/// omits it.
/// </summary>
[Trait("Color", "B")]
public class MortuaryMireTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MortuaryMire_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Mortuary Mire", _alice);

        land.Name.Should().Be("Mortuary Mire");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Mortuary Mire is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MortuaryMire_HasManaAbility_ForBlack()
    {
        var land = (Land)NamedCardFactory.Create("Mortuary Mire", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void MortuaryMire_EtbTrigger_IsBattlefieldActive_WithOptionalCreatureTarget()
    {
        var land = (Land)NamedCardFactory.Create("Mortuary Mire", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);

        // "You may … target creature card" → a single 0..1 target request.
        trigger.TargetRequests.Should().ContainSingle();
        var req = trigger.TargetRequests.Single();
        req.MinTargets.Should().Be(0, "the ETB is optional — \"you may\" (CR 603.5)");
        req.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void MortuaryMire_EtbResolution_PutsCreatureFromGraveyard_OnTopOfLibrary()
    {
        var alice = new Player("Alice", 20);
        var land = MortuaryMireFactory.Create(alice, triggers: null, replacements: null);

        // Creature card in the graveyard — the recur target.
        var bear = new Creature("Grizzly Bears", "1G", power: 2, toughness: 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        bear.SetZone(ZoneType.Graveyard);
        alice.Zones.Graveyard.AddCard(bear);

        // Pre-seed library with a filler so we can verify bear lands at index 0.
        var filler = new Creature("Filler", "", power: 1, toughness: 1);
        filler.SetOwner(alice);
        alice.Zones.Library.AddCard(filler);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Library, "the chosen creature is put on top of the library");
        alice.Zones.Library.GetCards().First().Should().BeSameAs(bear,
            "the recurred creature goes on TOP of the library (index 0)");
        alice.Zones.Graveyard.GetCards().Should().NotContain(bear,
            "the creature left the graveyard");
    }

    [Fact]
    public void MortuaryMire_EtbResolution_NoTargetChosen_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var land = MortuaryMireFactory.Create(alice, triggers: null, replacements: null);

        var bear = new Creature("Grizzly Bears", "1G", power: 2, toughness: 2);
        bear.SetOwner(alice);
        bear.SetController(alice);
        bear.SetZone(ZoneType.Graveyard);
        alice.Zones.Graveyard.AddCard(bear);

        // No SetChosenTargets call → optional "you may" declined.
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Graveyard, "no target chosen — nothing moves (CR 603.5)");
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void MortuaryMire_EtbResolution_NoncreatureTarget_IsIgnored()
    {
        var alice = new Player("Alice", 20);
        var land = MortuaryMireFactory.Create(alice, triggers: null, replacements: null);

        // A noncreature card in the graveyard — illegal at resolution (CR 608.2b).
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(alice);
        bolt.SetController(alice);
        bolt.SetZone(ZoneType.Graveyard);
        alice.Zones.Graveyard.AddCard(bolt);

        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bolt } });
        foreach (var effect in etb.Effects) effect.Execute();

        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "a noncreature card is not a legal target — the effect does nothing (CR 608.2b)");
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }
}
