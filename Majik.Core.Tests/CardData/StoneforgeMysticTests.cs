using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="StoneforgeMysticFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Activated ability shape: {1}{W} mana cost + tap cost.
/// - Activated ability resolve: picks an Equipment from hand → battlefield,
///   attaches to a controller creature.
/// - Activated ability no-op when no Equipment is in hand.
/// - ETB tutor: pulls an Equipment from library → hand.
/// </summary>
public class StoneforgeMysticTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMystic_Identity()
    {
        var c = StoneforgeMysticFactory.Create(_alice);

        c.Name.Should().Be("Stoneforge Mystic");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue("Stoneforge Mystic is a Kor");
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue("Stoneforge Mystic is an Artificer");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void StoneforgeMystic_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Stoneforge Mystic", _alice);

        c.Should().BeOfType<Creature>("Stoneforge Mystic is a Creature");
        c.Name.Should().Be("Stoneforge Mystic");
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        c.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMystic_ActivatedAbility_HasManaAndTapCost()
    {
        var c = StoneforgeMysticFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the activated ability requires {1}{W} mana");
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(cost => cost.CostType == AdditionalCostType.Tap,
                "the activated ability includes a {T} cost");
    }

    // -----------------------------------------------------------------------
    // Activated ability resolution — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMystic_ActivatedAbility_PutsEquipmentFromHand_AndAttaches()
    {
        var alice = new Player("Alice", 20);

        // A creature on the battlefield to receive the Equipment.
        var bearer = new Creature("Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.SetController(alice);
        alice.Zones.Battlefield.AddCard(bearer);
        bearer.SetZone(ZoneType.Battlefield);

        // A test Equipment in hand.
        var sword = new Artifact("Sword of Test", "2",
            subtypes: new[] { CardSubtype.Equipment });
        sword.SetOwner(alice);
        alice.Zones.Hand.AddCard(sword);
        sword.SetZone(ZoneType.Hand);

        var mystic = StoneforgeMysticFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(mystic);
        mystic.SetZone(ZoneType.Battlefield);

        var ability = mystic.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        sword.Zone.Should().Be(ZoneType.Battlefield,
            "the Equipment moved from hand to battlefield (CR 113.6c / 117.1a)");
        alice.Zones.Hand.GetCards().Should().NotContain(sword,
            "the Equipment was removed from hand");
        alice.Zones.Battlefield.GetCards().Should().Contain(sword,
            "the Equipment was added to the battlefield");
        sword.AttachedTo.Should().BeSameAs(bearer,
            "the Equipment was attached to the controller's creature (CR 701.3a)");
        sword.Controller.Should().BeSameAs(alice,
            "the Equipment is controlled by the activating player");
    }

    // -----------------------------------------------------------------------
    // Activated ability resolution — regression: no-op when hand empty of Equipment
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMystic_ActivatedAbility_NoEquipmentInHand_ResolvesAsNoOp()
    {
        var alice = new Player("Alice", 20);

        // A non-Equipment artifact in hand should NOT be moved.
        var randomArtifact = new Artifact("Random Doodad", "2");
        randomArtifact.SetOwner(alice);
        alice.Zones.Hand.AddCard(randomArtifact);
        randomArtifact.SetZone(ZoneType.Hand);

        var bearer = new Creature("Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.SetController(alice);
        alice.Zones.Battlefield.AddCard(bearer);
        bearer.SetZone(ZoneType.Battlefield);

        var mystic = StoneforgeMysticFactory.Create(alice);
        var ability = mystic.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("no Equipment in hand = no-op (CR 'you may' clause)");
        randomArtifact.Zone.Should().Be(ZoneType.Hand,
            "a non-Equipment artifact must not be put onto the battlefield");
        alice.Zones.Battlefield.GetCards().Should().NotContain(randomArtifact);
        bearer.Attachments.Should().BeEmpty(
            "no Equipment was put onto the battlefield so the bearer has no new attachments");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMystic_EtbTrigger_PullsEquipmentFromLibraryToHand()
    {
        var alice = new Player("Alice", 20);

        // Seed library: a non-Equipment first, then an Equipment, so we
        // can verify the predicate filters correctly.
        var bait = new Card("Random Card", "");
        bait.SetOwner(alice);
        alice.Zones.Library.AddCard(bait);
        bait.SetZone(ZoneType.Library);

        var sword = new Artifact("Sword of Test", "2",
            subtypes: new[] { CardSubtype.Equipment });
        sword.SetOwner(alice);
        alice.Zones.Library.AddCard(sword);
        sword.SetZone(ZoneType.Library);

        var mystic = StoneforgeMysticFactory.Create(alice);
        var etb = mystic.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        sword.Zone.Should().Be(ZoneType.Hand,
            "ETB tutor pulled the Equipment to hand (CR 701.19a)");
        alice.Zones.Hand.GetCards().Should().Contain(sword);
        alice.Zones.Library.GetCards().Should().NotContain(sword);
        bait.Zone.Should().Be(ZoneType.Library,
            "the non-Equipment card stays in the library (predicate-filtered)");
    }

    // -----------------------------------------------------------------------
    // ETB tutor — no Equipment in library
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMystic_EtbTrigger_NoEquipmentInLibrary_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var unrelated = new Card("Random Card", "");
        unrelated.SetOwner(alice);
        alice.Zones.Library.AddCard(unrelated);
        unrelated.SetZone(ZoneType.Library);

        var mystic = StoneforgeMysticFactory.Create(alice);
        var etb = mystic.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no Equipment in library = CR 701.19a decline / no-op");
        unrelated.Zone.Should().Be(ZoneType.Library);
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Activated ability — ZoneService overload moves via the service so
    // ETB triggers / replacements on the Equipment can fire (CR 603.6a).
    // -----------------------------------------------------------------------

    [Fact]
    public void StoneforgeMystic_ActivatedAbility_UsesZoneService_ForHandToBattlefield()
    {
        var alice = new Player("Alice", 20);
        var zoneService = new ZoneService();

        var bearer = new Creature("Bear", "1G", 2, 2);
        bearer.SetOwner(alice);
        bearer.SetController(alice);
        alice.Zones.Battlefield.AddCard(bearer);
        bearer.SetZone(ZoneType.Battlefield);

        var sword = new Artifact("Sword of Test", "2",
            subtypes: new[] { CardSubtype.Equipment });
        sword.SetOwner(alice);
        alice.Zones.Hand.AddCard(sword);
        sword.SetZone(ZoneType.Hand);

        var mystic = StoneforgeMysticFactory.Create(
            alice, zoneService: zoneService, eventBus: null, triggers: null);
        alice.Zones.Battlefield.AddCard(mystic);
        mystic.SetZone(ZoneType.Battlefield);

        var ability = mystic.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        sword.Zone.Should().Be(ZoneType.Battlefield);
        sword.AttachedTo.Should().BeSameAs(bearer);
        // ZoneService.MoveCard sets controller on entry to Battlefield.
        sword.Controller.Should().BeSameAs(alice);
    }
}
