using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Master of Etherium (Shards of Alara, {2}{U}).
///
/// Oracle text (Modern seed):
///   "Master of Etherium's power and toughness are each equal to the
///    number of artifacts you control.
///    Other artifact creatures you control get +1/+1."
///
/// Covers:
///   - Card shape (name, types, subtypes, mana cost, owner/controller).
///   - CDA Layer 7a: P/T = # artifacts the controller controls.
///   - Self-counting: the master IS one of its own artifacts (printed
///     wording).
///   - LTB: leaving the battlefield lifts the CDA (Power back to base).
///   - Lord Layer 7c: other artifact creatures you control get +1/+1.
///   - Master does NOT buff itself (Other clause).
///   - Master does NOT buff non-artifact creatures.
///   - Master does NOT buff opponent's artifact creatures.
///   - NamedCardFactory dispatch.
/// </summary>
public class MasterOfEtheriumFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void MasterOfEtherium_IsArtifactCreature_VedalkenWizard_AtCost2U()
    {
        var c = MasterOfEtheriumFactory.Create(_alice);

        c.Name.Should().Be("Master of Etherium");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vedalken).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MasterOfEtherium_CountArtifactsControlled_CountsArtifactCardsOnBattlefield()
    {
        var memnite = new Artifact("Memnite", "0") { Owner = _alice };
        memnite.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(memnite);

        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);

        MasterOfEtheriumFactory.CountArtifactsControlled(_alice).Should().Be(1,
            "only Memnite is an artifact; the bear isn't");
    }

    [Fact]
    public void MasterOfEtherium_CdaPT_TracksArtifactCount_IncludingSelf()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var master = MasterOfEtheriumFactory.Create(_alice, svc, bus);
        master.ActiveEffects = svc;
        _alice.Zones.Library.AddCard(master);

        // Drop the master onto the battlefield. Master is an artifact, so
        // the CDA should count it: P/T = 1.
        zones.MoveCard(master, ZoneType.Library, ZoneType.Battlefield, _alice);

        master.GetPower().Should().Be(1,
            "the master itself is an artifact you control");
        master.GetToughness().Should().Be(1);

        // Add two more artifacts.
        var memnite = new Artifact("Memnite", "0") { Owner = _alice };
        memnite.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(memnite);

        var orni = new Artifact("Ornithopter", "0",
            subtypes: new[] { CardSubtype.Thopter }) { Owner = _alice };
        orni.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(orni);

        // Master + Memnite + Ornithopter = 3 artifacts.
        master.GetPower().Should().Be(3);
        master.GetToughness().Should().Be(3);
    }

    [Fact]
    public void MasterOfEtherium_LeavingBattlefield_LiftsCda()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var master = MasterOfEtheriumFactory.Create(_alice, svc, bus);
        master.ActiveEffects = svc;
        _alice.Zones.Library.AddCard(master);

        var memnite = new Artifact("Memnite", "0") { Owner = _alice };
        memnite.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(memnite);

        zones.MoveCard(master, ZoneType.Library, ZoneType.Battlefield, _alice);
        master.GetPower().Should().Be(2);

        // Master dies — Sync should unregister the CDA.
        zones.MoveCard(master, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        // Off-battlefield: CdaPowerToughnessEffect.IsActive returns false,
        // so the layers service stops applying it. The base printed P/T
        // is 0/0 (the */* placeholder).
        master.GetPower().Should().Be(0);
        master.GetToughness().Should().Be(0);
    }

    [Fact]
    public void MasterOfEtherium_BuffsOtherArtifactCreaturesControllerControls()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        // Another artifact creature the master controls.
        var memnite = new Creature("Memnite", "0", 1, 1,
            subtypes: new[] { CardSubtype.Construct }) { Owner = _alice };
        memnite.AddCardType(CardType.Artifact);
        memnite.SetController(_alice);
        memnite.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(memnite);
        memnite.ActiveEffects = svc;

        var master = MasterOfEtheriumFactory.Create(_alice, svc, bus);
        master.ActiveEffects = svc;
        _alice.Zones.Library.AddCard(master);
        zones.MoveCard(master, ZoneType.Library, ZoneType.Battlefield, _alice);

        memnite.GetPower().Should().Be(2,
            "Master of Etherium grants +1/+1 to other artifact creatures you control");
        memnite.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MasterOfEtherium_DoesNotBuffSelf_OnlyOthers()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var master = MasterOfEtheriumFactory.Create(_alice, svc, bus);
        master.ActiveEffects = svc;
        _alice.Zones.Library.AddCard(master);
        zones.MoveCard(master, ZoneType.Library, ZoneType.Battlefield, _alice);

        // With only the master on the field: CDA = 1 (counts self). The
        // anthem is "Other", so it must NOT add another +1 on top.
        master.GetPower().Should().Be(1,
            "Other clause excludes self — the +1/+1 anthem does not stack on the master");
        master.GetToughness().Should().Be(1);
    }

    [Fact]
    public void MasterOfEtherium_DoesNotBuffNonArtifactCreatures()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        // A vanilla (non-artifact) creature.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.ActiveEffects = svc;

        var master = MasterOfEtheriumFactory.Create(_alice, svc, bus);
        master.ActiveEffects = svc;
        _alice.Zones.Library.AddCard(master);
        zones.MoveCard(master, ZoneType.Library, ZoneType.Battlefield, _alice);

        bear.GetPower().Should().Be(2,
            "the bear is not an artifact creature; the anthem doesn't apply");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MasterOfEtherium_DoesNotBuffOpponentsArtifactCreatures()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        // Bob's artifact creature.
        var bobMemnite = new Creature("Memnite (Bob's)", "0", 1, 1,
            subtypes: new[] { CardSubtype.Construct }) { Owner = _bob };
        bobMemnite.AddCardType(CardType.Artifact);
        bobMemnite.SetController(_bob);
        bobMemnite.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobMemnite);
        bobMemnite.ActiveEffects = svc;

        var master = MasterOfEtheriumFactory.Create(_alice, svc, bus);
        master.ActiveEffects = svc;
        _alice.Zones.Library.AddCard(master);
        zones.MoveCard(master, ZoneType.Library, ZoneType.Battlefield, _alice);

        bobMemnite.GetPower().Should().Be(1,
            "anthem is controller-scoped ('you control'); Bob's Memnite is unaffected");
        bobMemnite.GetToughness().Should().Be(1);
    }

    [Fact]
    public void MasterOfEtherium_LeavingBattlefield_LiftsAnthem()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var memnite = new Creature("Memnite", "0", 1, 1,
            subtypes: new[] { CardSubtype.Construct }) { Owner = _alice };
        memnite.AddCardType(CardType.Artifact);
        memnite.SetController(_alice);
        memnite.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(memnite);
        memnite.ActiveEffects = svc;

        var master = MasterOfEtheriumFactory.Create(_alice, svc, bus);
        master.ActiveEffects = svc;
        _alice.Zones.Library.AddCard(master);
        zones.MoveCard(master, ZoneType.Library, ZoneType.Battlefield, _alice);

        memnite.GetPower().Should().Be(2);

        zones.MoveCard(master, ZoneType.Battlefield, ZoneType.Graveyard, _alice);

        memnite.GetPower().Should().Be(1,
            "Master leaving the battlefield deactivates the +1/+1 anthem");
        memnite.GetToughness().Should().Be(1);
    }

    [Fact]
    public void MasterOfEtherium_NamedCardFactory_Dispatch()
    {
        var card = NamedCardFactory.Create("Master of Etherium", _alice);

        card.Should().NotBeNull();
        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Master of Etherium");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSubtype(CardSubtype.Vedalken).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }
}
