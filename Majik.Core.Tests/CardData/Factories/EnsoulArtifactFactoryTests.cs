using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EnsoulArtifactFactory"/>.
///
/// Ensoul Artifact (Magic 2015, {1}{U}) — Enchantment — Aura.
///   "Enchant artifact
///    Enchanted artifact is a creature with base power and toughness 5/5
///    in addition to its other types."
///
/// Covers:
/// - Card identity (Enchantment + Aura subtype, {1}{U}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Cast-time targeting: only artifacts on the battlefield are legal targets.
/// - Layer 4 + Layer 7b: enchanted artifact becomes a 5/5 creature
///   (in addition to its other types) while the aura is attached.
/// - No body grant when the aura is not attached.
/// </summary>
public class EnsoulArtifactFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void EnsoulArtifact_Identity()
    {
        var c = EnsoulArtifactFactory.Create(_alice);

        c.Name.Should().Be("Ensoul Artifact");
        c.Should().BeOfType<Enchantment>();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.IsAura.Should().BeTrue();
        c.ManaCost.Should().Be("{1}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EnsoulArtifact_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ensoul Artifact", _alice);

        c.Should().BeOfType<Enchantment>("Ensoul Artifact is an Enchantment");
        c.Name.Should().Be("Ensoul Artifact");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void EnsoulArtifact_BuildSpellDefinition_ListsOnlyArtifactsAsTargets()
    {
        var alice = new Player("Alice", 20);

        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(alice);
        rock.SetController(alice);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(alice);
        bear.SetController(alice);

        var aura = EnsoulArtifactFactory.Create(alice);

        var def = EnsoulArtifactFactory.BuildSpellDefinition(
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
    public void EnsoulArtifact_EnchantedArtifactBecomesFiveFiveCreature()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        // A non-creature artifact on the battlefield.
        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(alice);
        rock.SetController(alice);
        alice.Zones.Battlefield.AddCard(rock);
        rock.SetZone(ZoneType.Battlefield);

        var aura = EnsoulArtifactFactory.Create(alice, effects);

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
        cc.Power.Should().Be(5, "Ensoul Artifact sets base power 5");
        cc.Toughness.Should().Be(5, "Ensoul Artifact sets base toughness 5");
    }

    [Fact]
    public void EnsoulArtifact_NoBodyGrantWhenAuraNotAttached()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        var rock = new Artifact("Mind Stone", "{2}");
        rock.SetOwner(alice);
        rock.SetController(alice);
        alice.Zones.Battlefield.AddCard(rock);
        rock.SetZone(ZoneType.Battlefield);

        // Aura exists but is NOT on the battlefield / not attached.
        EnsoulArtifactFactory.Create(alice, effects);

        var chars = effects.Compute(rock);

        chars.Types.Should().NotContain(CardType.Creature,
            "with no aura attached, the artifact is not a creature");
    }
}
