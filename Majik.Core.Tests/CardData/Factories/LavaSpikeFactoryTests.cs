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
/// Unit tests for <see cref="LavaSpikeFactory"/> (Champions of Kamigawa).
///
/// Covers:
/// - Identity ({R} Sorcery — Arcane).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "target player or planeswalker".
/// - Resolve body deals 3 damage to a player target.
/// - Resolve body routes planeswalker damage through
///   <see cref="Primitives.Fx.DealDamageAny"/> (loyalty removal — CR 306.7).
/// </summary>
[Trait("Color", "R")]
public class LavaSpikeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LavaSpike_Identity_SorceryAtR_WithArcaneSubtype()
    {
        var spike = LavaSpikeFactory.Create(_alice);

        spike.Name.Should().Be("Lava Spike");
        spike.HasType(CardType.Sorcery).Should().BeTrue();
        spike.ManaCost.ToString().Should().Be("{R}");
        spike.Owner.Should().BeSameAs(_alice);
        spike.Controller.Should().BeSameAs(_alice);
        spike.HasSubtype(CardSubtype.Arcane).Should().BeTrue(
            "Lava Spike is an Arcane sorcery — splice fodder (CR 205.3k)");
    }
    [Fact]
    public void LavaSpike_SpellDefinition_HasPlayerOrPlaneswalkerRequest()
    {
        var def = LavaSpikeFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target player or planeswalker");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void LavaSpike_Resolve_DealsThreeDamageToPlayer()
    {
        var def = LavaSpikeFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(17, "Lava Spike deals 3 damage to target player");
    }

    [Fact]
    public void LavaSpike_Resolve_DealsThreeDamageToPlaneswalker_ViaLoyaltyRemoval()
    {
        var pw = new Planeswalker("Jace Beleren", "{1}{U}{U}", 3,
            Array.Empty<CardSupertype>(),
            new[] { CardSubtype.Jace });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = LavaSpikeFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { pw },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        // CR 306.7 — damage to a planeswalker is dealt as loyalty removal.
        // 3 loyalty − 3 = 0.
        pw.Loyalty.Should().Be(0,
            "Lava Spike deals 3 damage to target planeswalker (CR 306.7)");
        _bob.LifeTotal.Should().Be(20,
            "damage to a planeswalker does not reduce its controller's life total");
    }
}
