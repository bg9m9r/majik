using FluentAssertions;
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
/// Tests for Shatter ({1}{R}, Instant).
///
/// Oracle text:
///   "Destroy target artifact."
///
/// Covers:
///   - Card shape + dispatch ({1}{R}, Red, Instant).
///   - SpellDefinition shape: single 1..1 "target artifact" request.
///   - Destroys a target artifact → graveyard (CR 701.7).
///   - No-op if target is a creature (wrong type — CR 608.2b illegal target).
///   - No-op if target is an enchantment (wrong type — CR 608.2b illegal target).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
/// </summary>
public class ShatterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Shatter_HasInstantShape_Red_AtCost1R()
    {
        var card = ShatterFactory.Create(_alice);

        card.Name.Should().Be("Shatter");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsShatterShape()
    {
        var dispatched = NamedCardFactory.Create("Shatter", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Shatter");
        dispatched.ManaCost.Should().Be("{1}{R}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetArtifactRequest()
    {
        var def = ShatterFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
    }

    // -----------------------------------------------------------------------
    // Destroy artifact → graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Shatter destroys target artifact (CR 701.7)");
    }

    // -----------------------------------------------------------------------
    // No-op: wrong permanent type (creature)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetCreature_DoesNothing()
    {
        // A creature is not a legal target for Shatter. If somehow resolved
        // against one (e.g. type changed after targeting), CR 608.2b → no-op.
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Shatter targets artifact only (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // No-op: wrong permanent type (enchantment)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetEnchantment_DoesNothing()
    {
        // An enchantment is not a legal target for Shatter (unlike Disenchant).
        // CR 608.2b → no-op.
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Battlefield,
            because: "Shatter targets artifacts only, not enchantments (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // No-op: target left the battlefield before resolution (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        // Target leaves before resolution.
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
        var def = ShatterFactory.BuildDefinition(o => o);

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
