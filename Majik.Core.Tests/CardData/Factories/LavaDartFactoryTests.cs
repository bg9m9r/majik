using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="LavaDartFactory"/> (Time Spiral).
///
/// Covers:
/// - Identity ({R} Instant).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "any target"; resolves to 1 damage via
///   <see cref="Primitives.Fx.DealDamageAny"/>.
/// - Flashback cost builders match Lava Dart's printed cost
///   ({0} mana + "Sacrifice a Mountain" non-mana rider).
/// - End-to-end flashback cast through <see cref="SpellCastFlow"/>: cost
///   sacrifices the Mountain, spell deals 1 damage, post-resolve exile.
/// </summary>
public class LavaDartFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LavaDart_Identity_InstantAtR()
    {
        var dart = LavaDartFactory.Create(_alice);

        dart.Name.Should().Be("Lava Dart");
        dart.HasType(CardType.Instant).Should().BeTrue();
        dart.ManaCost.ToString().Should().Be("{R}");
        dart.Owner.Should().BeSameAs(_alice);
        dart.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LavaDart_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Lava Dart", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Lava Dart");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Spell definition — single any-target request
    // -----------------------------------------------------------------------

    [Fact]
    public void LavaDart_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = LavaDartFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    /// <summary>
    /// Resolve body deals exactly 1 damage to a player target through
    /// <see cref="Primitives.Fx.DealDamageAny"/>. (Creature / Planeswalker
    /// damage routing is covered by the Fx primitive itself in
    /// <c>FxTests</c>; this test verifies the factory wires through.)
    /// </summary>
    [Fact]
    public void LavaDart_Resolve_DealsOneDamageToPlayer()
    {
        var def = LavaDartFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.LifeTotal.Should().Be(19, "Lava Dart deals 1 damage to any target");
    }

    // -----------------------------------------------------------------------
    // Flashback cost shape — CR 702.34
    // -----------------------------------------------------------------------

    [Fact]
    public void LavaDart_FlashbackCost_IsZeroMana()
    {
        var cost = LavaDartFactory.BuildFlashbackCost();

        cost.AlternativeManaCost.IsZero.Should().BeTrue(
            "printed flashback cost is 'Sacrifice a Mountain' — no mana portion");
    }

    [Fact]
    public void LavaDart_FlashbackAdditionalCosts_RequiresMountainSacrifice()
    {
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);

        var costs = LavaDartFactory.BuildFlashbackAdditionalCosts(mountain);

        costs.Should().HaveCount(1);
        var sac = costs[0].Should().BeOfType<SacrificeBasicLandCost>().Subject;
        sac.RequiredSubtype.Should().Be(CardSubtype.Mountain);
        sac.Target.Should().BeSameAs(mountain);
        sac.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void LavaDart_FlashbackAdditionalCosts_RejectsNonMountain()
    {
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var costs = LavaDartFactory.BuildFlashbackAdditionalCosts(forest);
        var sac = costs[0].Should().BeOfType<SacrificeBasicLandCost>().Subject;

        sac.CanPay(_alice).Should().BeFalse(
            "Forest is not a Mountain — flashback cost can't be paid");
    }

    // -----------------------------------------------------------------------
    // End-to-end flashback cast — full SpellCastFlow
    // -----------------------------------------------------------------------

    /// <summary>
    /// End-to-end: cast Lava Dart from graveyard via flashback, sacrifice
    /// a Mountain as the rider, the spell resolves dealing 1 damage to
    /// Bob, and Lava Dart is exiled post-resolution (CR 702.34b).
    /// </summary>
    [Fact]
    public async Task LavaDart_FlashbackCast_FullPath_SacrificesMountain_DealsOneDamage_ThenExiled()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        // Lava Dart in Alice's graveyard.
        var dart = LavaDartFactory.Create(_alice);
        dart.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(dart);

        // A Mountain for the sacrifice rider.
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);

        // Spell definition + costs straight from the factory.
        var def = LavaDartFactory.BuildSpellDefinition(resolver: x => x);
        var altCost = LavaDartFactory.BuildFlashbackCost();
        var additionalCosts = LavaDartFactory.BuildFlashbackAdditionalCosts(mountain);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, stack);

        var spell = await flow.CastAsync(
            _alice, dart, def, agent, ctx,
            additionalCosts: additionalCosts.ToArray(),
            alternativeCost: altCost);

        // Mountain sacrificed during cost payment (CR 601.2f).
        mountain.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mountain);

        // Dart on stack now (flashback move out of graveyard).
        dart.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();

        // Damage dealt.
        _bob.LifeTotal.Should().Be(19);

        // CR 702.34b — flashback exiles the card after resolution, NOT
        // graveyard.
        dart.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(dart);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(dart);
    }
}
