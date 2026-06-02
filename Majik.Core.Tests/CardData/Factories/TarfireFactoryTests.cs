using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TarfireFactory"/> (Lorwyn / Modern Masters,
/// {R}).
///
/// Tarfire is a Kindred Instant — Goblin (CR 312 — Kindred is the modern
/// name for the legacy Tribal card type, modelled by
/// <see cref="CardType.Tribal"/>). Its oracle text is the vanilla Shock
/// shape:
///   "Tarfire deals 2 damage to any target."
///
/// Covers:
/// - Identity ({R} Instant + Tribal type + Goblin subtype, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "any target".
/// - Resolve body deals 2 damage to a player target.
/// - Resolve body routes creature damage through
///   <see cref="Primitives.Fx.DealDamageAny"/>.
/// </summary>
[Trait("Color", "R")]
public class TarfireFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Tarfire_Identity_KindredInstantGoblinAtR()
    {
        var tarfire = TarfireFactory.Create(_alice);

        tarfire.Name.Should().Be("Tarfire");
        tarfire.HasType(CardType.Instant).Should().BeTrue();
        // CR 312 — Kindred (printed) == the engine's legacy Tribal type.
        tarfire.HasType(CardType.Tribal).Should().BeTrue();
        tarfire.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        tarfire.ManaCost.ToString().Should().Be("{R}");
        tarfire.Owner.Should().BeSameAs(_alice);
        tarfire.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Tarfire_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = TarfireFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void Tarfire_Resolve_DealsTwoDamageToPlayer()
    {
        var def = TarfireFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(18, "Tarfire deals 2 damage to any target");
    }

    [Fact]
    public void Tarfire_Resolve_DealsTwoDamageToCreature()
    {
        // Use a 0/3 creature so 2 damage is not lethal — verifies damage
        // marker is applied without an SBA wipe interfering.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 0, 3,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = TarfireFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bear },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        bear.Damage.Should().Be(2, "Tarfire deals 2 damage to target creature");
    }
}
