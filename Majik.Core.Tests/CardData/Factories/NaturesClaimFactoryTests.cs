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
/// Tests for Nature's Claim ({G}, Instant).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Destroy target artifact or enchantment. Its controller gains 4 life."
///
/// Covers:
///   - Card shape + dispatch ({G}, Green, Instant).
///   - SpellDefinition shape: single 1..1 "target artifact or enchantment" request.
///   - Destroys a target artifact → graveyard; its controller gains 4 life.
///   - Destroys a target enchantment → graveyard; its controller gains 4 life.
///   - Life goes to the permanent's controller, not the caster (CR 119.3).
///   - No-op if target is a creature (wrong type — CR 608.2b illegal target).
///   - No-op (incl. no life gain) if target left the battlefield before resolution.
/// </summary>
public class NaturesClaimFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NaturesClaim_HasInstantShape_Green_AtCostG()
    {
        var card = NaturesClaimFactory.Create(_alice);

        card.Name.Should().Be("Nature's Claim");
        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsNaturesClaimShape()
    {
        var dispatched = NamedCardFactory.Create("Nature's Claim", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Nature's Claim");
        dispatched.ManaCost.Should().Be("{G}");
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetArtifactOrEnchantmentRequest()
    {
        var def = NaturesClaimFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().ContainAny("artifact", "enchantment");
    }

    // -----------------------------------------------------------------------
    // Destroy artifact → graveyard + controller gains 4 life
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysArtifact_MovesToGraveyard_ControllerGains4Life()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Nature's Claim destroys target artifact (CR 701.7)");
        _bob.LifeTotal.Should().Be(24,
            because: "the destroyed permanent's controller gains 4 life (CR 119.3)");
        _alice.LifeTotal.Should().Be(20,
            because: "the caster does not gain life — only the permanent's controller does");
    }

    // -----------------------------------------------------------------------
    // Destroy enchantment → graveyard + controller gains 4 life
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysEnchantment_MovesToGraveyard_ControllerGains4Life()
    {
        var enchantment = NewControlledPermanent<Enchantment>(_bob, "Sylvan Library", "{1}{G}");

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "Nature's Claim destroys target enchantment (CR 701.7)");
        _bob.LifeTotal.Should().Be(24,
            because: "the destroyed permanent's controller gains 4 life (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // Caster's own permanent: caster gains the life
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetingOwnPermanent_CasterGains4Life()
    {
        var artifact = NewControlledPermanent<Artifact>(_alice, "Sol Ring", "{1}");

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard);
        _alice.LifeTotal.Should().Be(24,
            because: "when the caster controls the destroyed permanent, the caster gains 4 life");
    }

    // -----------------------------------------------------------------------
    // No-op: wrong permanent type (creature) — no destroy, no life
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetCreature_DoesNothing_NoLifeGain()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Nature's Claim targets artifact or enchantment only (CR 608.2b)");
        _bob.LifeTotal.Should().Be(20,
            because: "an illegal target → the whole spell does nothing, no life gained");
    }

    // -----------------------------------------------------------------------
    // No-op: target left the battlefield before resolution (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing_NoLifeGain()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
        _bob.LifeTotal.Should().Be(20,
            because: "an illegal target → no life gained");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void Resolve(ICard target)
    {
        var def = NaturesClaimFactory.BuildDefinition(o => o);

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
