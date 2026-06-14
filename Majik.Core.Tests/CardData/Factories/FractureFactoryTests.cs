using FluentAssertions;
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
/// Tests for Fracture ({W}{B}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Destroy target artifact, enchantment, or planeswalker."
///
/// Covers:
///   - Card identity: {W}{B}, white+black multicolour, Instant.
///   - SpellDefinition shape: single 1..1 "artifact, enchantment, or
///     planeswalker" request.
///   - Destroys a target artifact → graveyard (CR 701.7).
///   - Destroys a target enchantment → graveyard (CR 701.7).
///   - Destroys a target planeswalker → graveyard (CR 701.7) — the unique
///     extension over Disenchant / Naturalize.
///   - No-op if target is a creature (wrong type — CR 608.2b illegal target).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
/// </summary>
[Trait("Color", "M")]
public class FractureFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Fracture_HasInstantShape_WhiteBlack_AtCostWB()
    {
        var card = FractureFactory.Create(_alice);

        card.Name.Should().Be("Fracture");
        card.ManaCost.Should().Be("{W}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleArtifactEnchantmentOrPlaneswalkerRequest()
    {
        var def = FractureFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should()
            .ContainAll("artifact", "enchantment", "planeswalker");
    }

    // -----------------------------------------------------------------------
    // Destroy artifact / enchantment / planeswalker → graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysArtifact_MovesToGraveyard()
    {
        var artifact = NewArtifact(_bob, "Sol Ring", "{1}");

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Fracture destroys target artifact (CR 701.7)");
    }

    [Fact]
    public void DestroysEnchantment_MovesToGraveyard()
    {
        var enchantment = NewEnchantment(_bob, "Sylvan Library", "{1}{G}");

        Resolve(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "Fracture destroys target enchantment (CR 701.7)");
    }

    [Fact]
    public void DestroysPlaneswalker_MovesToGraveyard()
    {
        var planeswalker = NewPlaneswalker(_bob, "Liliana of the Veil", "{1}{B}{B}", 3);

        Resolve(planeswalker);

        planeswalker.Zone.Should().Be(ZoneType.Graveyard,
            because: "Fracture destroys target planeswalker (CR 701.7)");
    }

    // -----------------------------------------------------------------------
    // No-op: wrong permanent type (creature)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetCreature_DoesNothing()
    {
        // A creature is not a legal target for Fracture. If somehow resolved
        // against one (type changed after targeting), CR 608.2b → no-op.
        var creature = NewCreature(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        Resolve(creature);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "Fracture targets artifact/enchantment/planeswalker only (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // No-op: target left the battlefield before resolution (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewArtifact(_bob, "Sol Ring", "{1}");

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
        var def = FractureFactory.BuildDefinition(o => o);

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

    private Artifact NewArtifact(Player owner, string name, string cost)
    {
        var card = new Artifact(name, cost);
        return PlaceOnBattlefield(card, owner);
    }

    private Enchantment NewEnchantment(Player owner, string name, string cost)
    {
        var card = new Enchantment(name, cost);
        return PlaceOnBattlefield(card, owner);
    }

    private Creature NewCreature(Player owner, string name, string cost, int power, int toughness)
    {
        var card = new Creature(name, cost, power, toughness);
        return PlaceOnBattlefield(card, owner);
    }

    private Planeswalker NewPlaneswalker(Player owner, string name, string cost, int loyalty)
    {
        var card = new Planeswalker(name, cost, loyalty);
        return PlaceOnBattlefield(card, owner);
    }

    private static T PlaceOnBattlefield<T>(T card, Player owner) where T : Card
    {
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
