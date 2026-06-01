using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TezzeretsTouchFactory"/>.
///
/// Tezzeret's Touch (Aether Revolt, {1}{U}{B}) — Enchantment — Aura.
///   "Enchant artifact
///    Enchanted artifact is a creature with base power and toughness 5/5
///    in addition to its other types.
///    When enchanted artifact is put into a graveyard, return that card
///    to its owner's hand."
///
/// Covers:
/// - Card identity (Enchantment + Aura subtype, {1}{U}{B}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Cast-time targeting: only artifacts on the battlefield are legal targets.
/// - Layer 4 + Layer 7b: enchanted artifact becomes a 5/5 creature
///   (in addition to its other types) while the aura is attached.
/// - LTB return: when the enchanted artifact is put into a graveyard, the
///   card is returned to its owner's hand.
/// </summary>
public class TezzeretsTouchFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TezzeretsTouch_Identity()
    {
        var c = TezzeretsTouchFactory.Create(_alice);

        c.Name.Should().Be("Tezzeret's Touch");
        c.Should().BeOfType<Enchantment>();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.IsAura.Should().BeTrue();
        c.ManaCost.Should().Be("{1}{U}{B}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TezzeretsTouch_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Tezzeret's Touch", _alice);

        c.Should().BeOfType<Enchantment>("Tezzeret's Touch is an Enchantment");
        c.Name.Should().Be("Tezzeret's Touch");
        c.ManaCost.Should().Be("{1}{U}{B}");
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void TezzeretsTouch_BuildSpellDefinition_ListsOnlyArtifactsAsTargets()
    {
        var alice = new Player("Alice", 20);

        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(alice);
        rock.SetController(alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);

        var aura = TezzeretsTouchFactory.Create(alice);

        var def = TezzeretsTouchFactory.BuildSpellDefinition(
            aura, new Permanent[] { rock, bear });

        def.TargetRequests.Should().HaveCount(1);
        var request = def.TargetRequests[0];
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
        request.LegalCandidates.Should().Contain(rock,
            "Enchant artifact — artifacts are legal targets (CR 702.5b)");
        request.LegalCandidates.Should().NotContain(bear,
            "Grizzly Bears is a creature, not an artifact");
    }

    [Fact]
    public void TezzeretsTouch_EnchantedArtifactBecomesFiveFiveCreature()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();

        // A non-creature artifact on the battlefield.
        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(alice);
        rock.SetController(alice);
        alice.Zones.Battlefield.AddCard(rock);
        rock.SetZone(ZoneType.Battlefield);

        var aura = TezzeretsTouchFactory.Create(
            alice, effects: effects, eventBus: bus, zoneService: null, triggers: null);

        // Put the aura onto the battlefield and attach to the artifact.
        alice.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
        aura.AttachTo(rock);

        var chars = effects.Compute(rock);

        // CR 613.1c — Creature type added in addition to its other types.
        chars.Types.Should().Contain(CardType.Creature,
            "Layer 4 adds Creature type");
        chars.Types.Should().Contain(CardType.Artifact,
            "CR 613.1c — types are added; the artifact is still an artifact");

        // CR 613.7b — base power and toughness become 5/5.
        chars.Should().BeOfType<CreatureCharacteristics>(
            "the Layer-4 Creature grant upgrades the Artifact's row to a creature row");
        var cc = (CreatureCharacteristics)chars;
        cc.Power.Should().Be(5, "Tezzeret's Touch sets base power 5");
        cc.Toughness.Should().Be(5, "Tezzeret's Touch sets base toughness 5");
    }

    [Fact]
    public void TezzeretsTouch_NoBodyGrantWhenAuraNotAttached()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var effects = new ContinuousEffectsService();

        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(alice);
        rock.SetController(alice);
        alice.Zones.Battlefield.AddCard(rock);
        rock.SetZone(ZoneType.Battlefield);

        // Aura exists but is NOT on the battlefield / not attached.
        TezzeretsTouchFactory.Create(
            alice, effects: effects, eventBus: bus, zoneService: null, triggers: null);

        var chars = effects.Compute(rock);

        chars.Types.Should().NotContain(CardType.Creature,
            "with no aura attached, the artifact is not a creature");
    }

    [Fact]
    public void TezzeretsTouch_LTB_ReturnsArtifactToOwnersHand_WhenPutIntoGraveyard()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(alice);
        rock.SetController(alice);
        alice.Zones.Graveyard.AddCard(rock);
        rock.SetZone(ZoneType.Graveyard);

        var aura = TezzeretsTouchFactory.Create(
            alice, effects: null, eventBus: bus, zoneService: zones, triggers: null);

        // The LTB-return trigger watches the enchanted artifact entering the
        // graveyard. Resolve its effect directly: the card is returned to its
        // owner's hand.
        var ltb = aura.Abilities.OfType<TriggeredAbility>().Single();

        // The trigger must be able to resolve against the artifact that was
        // just put into the graveyard; the factory threads the bearer in.
        aura.AttachTo(rock);

        foreach (var effect in ltb.Effects) effect.Execute();

        rock.Zone.Should().Be(ZoneType.Hand,
            "when the enchanted artifact is put into a graveyard, it returns to its owner's hand");
        alice.Zones.Hand.GetCards().Should().Contain(rock);
        alice.Zones.Graveyard.GetCards().Should().NotContain(rock);
    }
}
