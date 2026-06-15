using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Gleeful Demolition (Phyrexia: All Will Be One, {R}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-06-14):
///   "Destroy target artifact. If you controlled that artifact, create three
///    1/1 red Phyrexian Goblin creature tokens."
///
/// Covers:
///   - Card identity ({R}, red, Sorcery, mana value 1).
///   - SpellDefinition shape: no modes, no X, one "target artifact" request.
///   - Destroy own artifact → graveyard + three 1/1 red Phyrexian Goblin
///     tokens for the caster (CR 701.7 / CR 111).
///   - Destroy an opponent-controlled artifact → graveyard, NO tokens
///     (CR 608.2 — "If you controlled that artifact").
///   - No-op against a non-artifact target (CR 608.2b).
///   - No-op if the target left the battlefield before resolution (CR 608.2b)
///     — and the rider does not fire.
/// </summary>
[Trait("Color", "R")]
public class GleefulDemolitionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GleefulDemolition_HasSorceryShape_Red_AtCostR()
    {
        var card = GleefulDemolitionFactory.Create(_alice);

        card.Name.Should().Be("Gleeful Demolition");
        card.ManaCost.Should().Be("{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(1, because: "{R} = mana value 1");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellDefinition_HasNoModes_NoX_OneArtifactTarget()
    {
        var def = GleefulDemolitionFactory.BuildSpellDefinition(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().ContainSingle();
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("artifact");
    }

    // -----------------------------------------------------------------------
    // Own artifact — destroy + three Goblins
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysOwnArtifact_AndCreatesThreeRedPhyrexianGoblinTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var artifact = NewArtifact(_alice, "Sol Ring", "{1}");

        ResolveAgainst(artifact, _alice, zones);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "the target artifact is destroyed (CR 701.7)");

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(GleefulDemolitionTemplateTokenCount,
            "the caster controlled the artifact → three Goblins (CR 111)");
        tokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Goblin");
            t.BasePower.Should().Be(1);
            t.BaseToughness.Should().Be(1);
            t.HasSubtype(CardSubtype.Goblin).Should().BeTrue("CR 205.3m — Goblin subtype");
            t.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue("CR 205.3m — Phyrexian subtype");
            t.HasType(CardType.Creature).Should().BeTrue();
            CardColors.GetColors(t).Should().Contain(ManaColor.Red,
                "the tokens are red (CR 111.4)");
            t.Controller.Should().BeSameAs(_alice, "the caster controls the tokens");
        });
    }

    // -----------------------------------------------------------------------
    // Opponent's artifact — destroy only, no tokens
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroysOpponentArtifact_ButCreatesNoTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var artifact = NewArtifact(_bob, "Sol Ring", "{1}");

        ResolveAgainst(artifact, _alice, zones);

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "the artifact is still destroyed (CR 701.7)");

        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Where(c => c.IsToken)
            .Should().BeEmpty(
            "CR 608.2 — the caster did NOT control that artifact, so no tokens");
    }

    // -----------------------------------------------------------------------
    // Wrong type at resolution — no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetCreature_DoesNothing_AndNoTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var creature = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        ResolveAgainst(creature, _alice, zones);

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "CR 608.2b — the target is not an artifact → no-op");
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Where(c => c.IsToken)
            .Should().BeEmpty("the destroy never happened, so the rider does not fire");
    }

    // -----------------------------------------------------------------------
    // Target gone before resolution — no-op, rider does not fire
    // -----------------------------------------------------------------------

    [Fact]
    public void TargetLeftBattlefield_DoesNothing_AndNoTokens()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var artifact = NewArtifact(_alice, "Sol Ring", "{1}");

        // Target leaves before resolution.
        _alice.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(artifact);

        ResolveAgainst(artifact, _alice, zones);

        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Where(c => c.IsToken)
            .Should().BeEmpty(
            "CR 608.2b — target not on battlefield at resolution → no destroy, no rider");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private const int GleefulDemolitionTemplateTokenCount = 3;

    private void ResolveAgainst(ICard target, Player caster, ZoneService zones)
    {
        var def = GleefulDemolitionFactory.BuildSpellDefinition(caster, o => o, zones);
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

    private static Artifact NewArtifact(Player owner, string name, string cost)
    {
        var card = new Artifact(name, cost);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }

    private static Creature NewCreature(Player owner, string name, string cost, int p, int t)
    {
        var card = new Creature(name, cost, p, t);
        card.SetOwner(owner);
        card.SetController(owner);
        card.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(card);
        return card;
    }
}
