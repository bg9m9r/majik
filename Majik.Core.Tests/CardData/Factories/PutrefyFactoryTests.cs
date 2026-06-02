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
/// Tests for Putrefy (Ravnica: City of Guilds, {1}{B}{G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Destroy target artifact or creature. It can't be regenerated."
///
/// Card shape comes from the embedded JSON (<c>putrefy.json</c>) via
/// <see cref="CardDefinitionLoader"/> + <see cref="CardDefinitionFactory"/>;
/// the resolve-time "destroy target artifact or creature" body is built by
/// the factory (mirrors <see cref="AncientGrudgeFactory"/> for the
/// data-only shape + destroy gather, and <see cref="TerminateFactory"/> for
/// the "can't be regenerated" rider).
///
/// Covers:
///   - Card shape + dispatch ({1}{B}{G}, Black+Green, Instant).
///   - SpellDefinition shape: one 1..1 "target artifact or creature" request.
///   - Destroys a target artifact → graveyard (CR 701.7).
///   - Destroys a target creature → graveyard (CR 701.7).
///   - No-op against a non-artifact, non-creature target (CR 608.2b).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
/// </summary>
[Trait("Color", "M")]
public class PutrefyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity + dispatch ───────────────────────────────────────────────────

    [Fact]
    public void Putrefy_HasInstantShape_BlackGreen_AtCost1BG()
    {
        var card = PutrefyFactory.Create(_alice);

        card.Name.Should().Be("Putrefy");
        card.ManaCost.Should().Be("{1}{B}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{1}{B}{G} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NameAndCost_AreScryfallExact()
    {
        PutrefyFactory.CardName.Should().Be("Putrefy");
        PutrefyFactory.PrintedManaCost.Should().Be("{1}{B}{G}");
    }
    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_ExposesSingleArtifactOrCreatureTargetRequest()
    {
        var def = PutrefyFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact or creature");
    }

    // ── Destroy target ────────────────────────────────────────────────────────

    [Fact]
    public void DestroysTargetArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        ResolveAgainst(artifact);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "Putrefy destroys the target artifact (CR 701.7)");
    }

    [Fact]
    public void DestroysTargetCreature_MovesToGraveyard()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}", 3, 4);

        ResolveAgainst(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "Putrefy destroys the target creature (CR 701.7)");
    }

    [Fact]
    public void TargetEnchantment_DoesNothing()
    {
        var enchantment = NewControlledEnchantment(_bob, "Pacifism", "{1}{W}");

        ResolveAgainst(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Battlefield,
            because: "Putrefy destroys artifacts or creatures only, not enchantments (CR 608.2b)");
    }

    [Fact]
    public void TargetNotOnBattlefield_DoesNothing()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}", 3, 4);

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        ResolveAgainst(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ResolveAgainst(Permanent target)
    {
        var def = PutrefyFactory.BuildDefinition(o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private Artifact NewControlledArtifact(Player owner, string name, string cost)
    {
        var card = new Artifact(name, cost);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }

    private Creature NewControlledCreature(Player owner, string name, string cost, int p, int t)
    {
        var card = new Creature(name, cost, p, t);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }

    private Enchantment NewControlledEnchantment(Player owner, string name, string cost)
    {
        var card = new Enchantment(name, cost);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
