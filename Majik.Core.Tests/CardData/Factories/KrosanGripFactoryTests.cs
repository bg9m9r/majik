using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KrosanGripFactory"/>.
///
/// Card: Krosan Grip — Instant {2}{G} (Dissension).
///   "Split second (As long as this spell is on the stack, players can't cast
///    spells or activate abilities that aren't mana abilities.)
///    Destroy target artifact or enchantment."
///
/// CR 702.61 — Split second declared as a keyword marker (mirrors
/// <see cref="ExtirpateFactory"/> / <see cref="SuddenEdictFactory"/>).
/// CR 701.7  — "destroy" honours Indestructible / regeneration; the resolve
/// body mirrors <see cref="DisenchantFactory"/> (single artifact/enchantment
/// target).
///
/// Covers:
///   - Identity: {2}{G} green Instant carrying the Split second marker.
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single "target artifact or enchantment" request in the SpellDefinition.
///   - Destroys a target artifact → graveyard (CR 701.7).
///   - Destroys a target enchantment → graveyard (CR 701.7).
///   - No-op if target is a creature (wrong type — CR 608.2b).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
/// </summary>
[Trait("Color", "G")]
public class KrosanGripFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void KrosanGrip_HasInstantShape_Green_AtCost2G()
    {
        var card = KrosanGripFactory.Create(_alice);

        card.Name.Should().Be("Krosan Grip");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KrosanGrip_CarriesSplitSecondMarker()
    {
        var card = KrosanGripFactory.Create(_alice);

        // CR 702.61 — Split second declared as a keyword marker, exactly as
        // ExtirpateFactory / SuddenEdictFactory do.
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Split second");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsKrosanGripShape()
    {
        var dispatched = NamedCardFactory.Create("Krosan Grip", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Krosan Grip");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetArtifactOrEnchantmentRequest()
    {
        var def = KrosanGripFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().ContainAny("artifact", "enchantment");
    }

    // -----------------------------------------------------------------------
    // Destroy artifact / enchantment → graveyard (CR 701.7)
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Krosan Grip destroys target artifact (CR 701.7)");
    }

    [Fact]
    public void DestroysEnchantment_MovesToGraveyard()
    {
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "Krosan Grip destroys target enchantment (CR 701.7)");
    }

    // -----------------------------------------------------------------------
    // No-op: wrong permanent type (creature) — CR 608.2b
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetCreature_DoesNothing()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Krosan Grip targets artifact or enchantment only (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // No-op: target left the battlefield before resolution (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(ICard target)
    {
        var def = KrosanGripFactory.BuildDefinition(o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static T NewControlledPermanent<T>(Player owner, string name, string cost,
        int power = 0, int toughness = 0)
        where T : ICard
    {
        T card;
        if (typeof(T) == typeof(Creature))
        {
            card = (T)(ICard)new Creature(name, cost, power, toughness);
        }
        else if (typeof(T) == typeof(Artifact))
        {
            card = (T)(ICard)new Artifact(name, cost);
        }
        else if (typeof(T) == typeof(Enchantment))
        {
            card = (T)(ICard)new Enchantment(name, cost);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported type {typeof(T)}");
        }

        ((Card)(ICard)card).SetOwner(owner);
        ((Card)(ICard)card).SetController(owner);
        ((Card)(ICard)card).SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
