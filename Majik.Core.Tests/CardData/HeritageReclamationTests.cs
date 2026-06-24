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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Heritage Reclamation (Modern Horizons 3, {1}{G}, Instant).
///
/// "Choose one —
///   • Destroy target artifact.
///   • Destroy target enchantment.
///   • Exile up to one target card from a graveyard. Draw a card."
///
/// Covers the card's UNIQUE behaviour (the three-mode "choose one" body):
///   - SpellDefinition shape (3 modes, 3 per-mode TargetRequests; the
///     graveyard mode's target is "up to one" → MinTargets 0).
///   - Mode 0 (destroy artifact) destroys the chosen artifact.
///   - Mode 1 (destroy enchantment) destroys the chosen enchantment.
///   - Mode 2 (exile + draw) exiles the chosen graveyard card and draws.
///   - Mode 2 with NO graveyard target ("up to one") still draws.
///   - Illegal target at resolution (wrong type / not on battlefield) → no-op
///     (CR 608.2b).
///
/// (Card-identity + NamedCardFactory dispatch + well-formedness are asserted
/// for every implemented card by CardFactoryContractTests — not re-tested here.)
/// </summary>
[Trait("Color", "G")]
public class HeritageReclamationTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Identity_Instant_1G()
    {
        var card = HeritageReclamationFactory.Create(_alice);

        card.Name.Should().Be("Heritage Reclamation");
        card.ManaCost.Should().Be("{1}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildSpellDefinition_ExposesThreeModes_WithPerModeUpToOneTargets()
    {
        var def = HeritageReclamationFactory.BuildSpellDefinition(_alice, t => t);

        def.Modes.Should().HaveCount(3);
        def.Modes[HeritageReclamationFactory.ModeDestroyArtifact].Should().Contain("artifact");
        def.Modes[HeritageReclamationFactory.ModeDestroyEnchantment].Should().Contain("enchantment");
        def.Modes[HeritageReclamationFactory.ModeExileDraw].Should().Contain("Draw a card");

        def.TargetRequests.Should().HaveCount(3);
        foreach (var tr in def.TargetRequests)
        {
            tr.MinTargets.Should().Be(0, "unchosen modes must not gate the cast, and mode 2 is 'up to one'");
            tr.MaxTargets.Should().Be(1);
        }
    }

    [Fact]
    public void Mode0_DestroyArtifact_DestroysChosenArtifact()
    {
        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.SetOwner(_bob);
        artifact.SetController(_bob);
        artifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(artifact);

        var def = HeritageReclamationFactory.BuildSpellDefinition(_alice, t => t);
        var chosen = ChosenForMode(HeritageReclamationFactory.ModeDestroyArtifact, artifact);

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        artifact.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(artifact);
        _bob.Zones.Graveyard.GetCards().Should().Contain(artifact);
    }

    [Fact]
    public void Mode1_DestroyEnchantment_DestroysChosenEnchantment()
    {
        var enchantment = new Enchantment("Rancor", "{G}");
        enchantment.SetOwner(_bob);
        enchantment.SetController(_bob);
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var def = HeritageReclamationFactory.BuildSpellDefinition(_alice, t => t);
        var chosen = ChosenForMode(HeritageReclamationFactory.ModeDestroyEnchantment, enchantment);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        enchantment.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(enchantment);
    }

    [Fact]
    public void Mode0_WrongType_IllegalTargetAtResolution_NoDestroy()
    {
        // An enchantment handed to the destroy-ARTIFACT mode: illegal at
        // resolution (CR 608.2b) → no-op.
        var enchantment = new Enchantment("Rancor", "{G}");
        enchantment.SetOwner(_bob);
        enchantment.SetController(_bob);
        enchantment.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(enchantment);

        var def = HeritageReclamationFactory.BuildSpellDefinition(_alice, t => t);
        var chosen = ChosenForMode(HeritageReclamationFactory.ModeDestroyArtifact, enchantment);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        enchantment.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Mode2_ExileDraw_ExilesChosenGraveyardCard_AndDraws()
    {
        var inGy = new Instant("Lightning Bolt", "{R}");
        inGy.SetOwner(_bob);
        inGy.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(inGy);

        var top = new Card("AliceTop", "");
        top.SetOwner(_alice);
        top.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(top);

        var def = HeritageReclamationFactory.BuildSpellDefinition(_alice, t => t);
        var handBefore = _alice.Zones.Hand.GetCards().Count();
        var chosen = ChosenForMode(HeritageReclamationFactory.ModeExileDraw, inGy);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        inGy.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(inGy);

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Mode2_UpToOne_NoGraveyardTarget_StillDraws()
    {
        // CR 115.1b — "up to one target": choosing zero graveyard cards is
        // legal, and the draw still happens.
        var top = new Card("AliceTop", "");
        top.SetOwner(_alice);
        top.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(top);

        var def = HeritageReclamationFactory.BuildSpellDefinition(_alice, t => t);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var chosen = new ChosenSpellParams(
            ModeIndex: HeritageReclamationFactory.ModeExileDraw,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),  // mode 0 unused
                Array.Empty<object>(),  // mode 1 unused
                Array.Empty<object>(),  // mode 2 — no graveyard card chosen
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Mode2_EmptyLibrary_DrawSkipped_MarksTriedToDrawFromEmpty()
    {
        var def = HeritageReclamationFactory.BuildSpellDefinition(_alice, t => t);

        var chosen = new ChosenSpellParams(
            ModeIndex: HeritageReclamationFactory.ModeExileDraw,
            X: null,
            Targets: new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                Array.Empty<object>(),
                Array.Empty<object>(),
            },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
    }

    /// <summary>Build a Choose-one selection for <paramref name="mode"/> with a
    /// single target placed in that mode's slot.</summary>
    private ChosenSpellParams ChosenForMode(int mode, object target)
    {
        var slots = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        slots[mode] = new[] { target };

        return new ChosenSpellParams(
            ModeIndex: mode,
            X: null,
            Targets: slots,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });
    }
}
