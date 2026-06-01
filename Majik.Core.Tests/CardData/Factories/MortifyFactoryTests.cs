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
/// Tests for Mortify (Guildpact, {1}{W}{B}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Destroy target creature or enchantment."
///
/// Card shape comes from the embedded JSON (<c>mortify.json</c>) via
/// <see cref="CardDefinitionLoader"/> + <see cref="CardDefinitionFactory"/>;
/// the resolve-time "destroy target creature or enchantment" body is built by
/// the factory (mirrors <see cref="PutrefyFactory"/> for the data-only shape +
/// destroy gather, narrowing the legal-target predicate to creature/enchantment
/// — the same shape as <see cref="HerosDownfallFactory"/> for a single-target
/// instant-speed destroy).
///
/// Covers:
///   - Card shape + dispatch ({1}{W}{B}, White+Black, Instant).
///   - SpellDefinition shape: one 1..1 "target creature or enchantment" request.
///   - Destroys a target creature → graveyard (CR 701.7).
///   - Destroys a target enchantment → graveyard (CR 701.7).
///   - No-op against a non-creature, non-enchantment target (CR 608.2b).
///   - No-op if target left the battlefield before resolution (CR 608.2b).
/// </summary>
public class MortifyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity + dispatch ───────────────────────────────────────────────────

    [Fact]
    public void Mortify_HasInstantShape_WhiteBlack_AtCost1WB()
    {
        var card = MortifyFactory.Create(_alice);

        card.Name.Should().Be("Mortify");
        card.ManaCost.Should().Be("{1}{W}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{1}{W}{B} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NameAndCost_AreScryfallExact()
    {
        MortifyFactory.CardName.Should().Be("Mortify");
        MortifyFactory.PrintedManaCost.Should().Be("{1}{W}{B}");
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsMortifyShape()
    {
        var dispatched = NamedCardFactory.Create("Mortify", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Mortify");
        dispatched.ManaCost.Should().Be("{1}{W}{B}");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_ExposesSingleCreatureOrEnchantmentTargetRequest()
    {
        var def = MortifyFactory.BuildDefinition(o => o);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("creature or enchantment");
    }

    // ── Destroy target ────────────────────────────────────────────────────────

    [Fact]
    public void DestroysTargetCreature_MovesToGraveyard()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}", 3, 4);

        ResolveAgainst(creature);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "Mortify destroys the target creature (CR 701.7)");
    }

    [Fact]
    public void DestroysTargetEnchantment_MovesToGraveyard()
    {
        var enchantment = NewControlledEnchantment(_bob, "Pacifism", "{1}{W}");

        ResolveAgainst(enchantment);

        enchantment.Zone.Should().Be(ZoneType.Graveyard,
            because: "Mortify destroys the target enchantment (CR 701.7)");
    }

    [Fact]
    public void TargetArtifact_DoesNothing()
    {
        var artifact = NewControlledArtifact(_bob, "Sol Ring", "{1}");

        ResolveAgainst(artifact);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "Mortify destroys creatures or enchantments only, not bare artifacts (CR 608.2b)");
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
        var def = MortifyFactory.BuildDefinition(o => o);

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
