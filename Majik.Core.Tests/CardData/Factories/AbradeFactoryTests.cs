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
/// Tests for Abrade (Hour of Devastation, {1}{R}, Instant).
///
/// Oracle text:
///   "Choose one —
///     • Abrade deals 3 damage to target creature.
///     • Destroy target artifact."
///
/// CR 700.2d — modal "Choose one —" spell with 2 modes.
///   Mode 0: 3 damage to target creature.
///   Mode 1: destroy target artifact.
///
/// Covers:
///   - Card shape + dispatch ({1}{R}, Red, Instant).
///   - SpellDefinition shape: 2 modes, 2 MinTargets=0 target requests.
///   - Mode 0 deals 3 damage to a target creature.
///   - Mode 0 no-op against a non-creature target (CR 608.2b).
///   - Mode 1 destroys a target artifact → graveyard (CR 701.7).
///   - Mode 1 no-op against a creature (wrong type — CR 608.2b).
///   - Mode 1 no-op if target left the battlefield before resolution (CR 608.2b).
/// </summary>
public class AbradeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Abrade_HasInstantShape_Red_AtCost1R()
    {
        var card = AbradeFactory.Create(_alice);

        card.Name.Should().Be("Abrade");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{R} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsAbradeShape()
    {
        var dispatched = NamedCardFactory.Create("Abrade", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Abrade");
        dispatched.ManaCost.Should().Be("{1}{R}");
    }

    [Fact]
    public void SpellDefinition_ExposesTwoModes_AndTwoOptionalTargetRequests()
    {
        var def = AbradeFactory.BuildDefinition(o => o);

        def.Modes.Should().HaveCount(2);
        def.Modes[AbradeFactory.ModeDamage].Should().Contain("damage");
        def.Modes[AbradeFactory.ModeDestroy].Should().Contain("Destroy");

        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[AbradeFactory.ModeDamage].MinTargets.Should().Be(0);
        def.TargetRequests[AbradeFactory.ModeDamage].Description.Should().Contain("creature");
        def.TargetRequests[AbradeFactory.ModeDestroy].MinTargets.Should().Be(0);
        def.TargetRequests[AbradeFactory.ModeDestroy].Description.Should().Contain("artifact");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — 3 damage to target creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_Deals3DamageToTargetCreature()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = ResolveMode(AbradeFactory.ModeDamage,
            new IReadOnlyList<object>[]
            {
                new object[] { creature }, // mode 0 target
                Array.Empty<object>(),     // mode 1 (unused)
            });

        effects.Should().HaveCount(1);
        creature.Damage.Should().Be(3,
            because: "mode 0 deals 3 damage to the target creature");
    }

    [Fact]
    public void Mode0_TargetArtifact_DoesNothing()
    {
        // An artifact is not a legal target for the damage mode. If somehow
        // resolved against one, CR 608.2b → no-op.
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        ResolveMode(AbradeFactory.ModeDamage,
            new IReadOnlyList<object>[]
            {
                new object[] { artifact },
                Array.Empty<object>(),
            });

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 0 only damages creatures (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy target artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DestroysArtifact_MovesToGraveyard()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        var effects = ResolveMode(AbradeFactory.ModeDestroy,
            new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),     // mode 0 (unused)
                new object[] { artifact }, // mode 1 target
            });

        effects.Should().HaveCount(1);
        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 1 destroys the target artifact (CR 701.7)");
    }

    [Fact]
    public void Mode1_TargetCreature_DoesNothing()
    {
        var creature = NewControlledPermanent<Creature>(_bob, "Grizzly Bears", "{1}{G}", 2, 2);

        ResolveMode(AbradeFactory.ModeDestroy,
            new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                new object[] { creature },
            });

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "mode 1 destroys artifacts only, not creatures (CR 608.2b)");
    }

    [Fact]
    public void Mode1_TargetNotOnBattlefield_DoesNothing()
    {
        var artifact = NewControlledPermanent<Artifact>(_bob, "Sol Ring", "{1}");

        // Target leaves before resolution.
        _bob.Zones.Battlefield.RemoveCard(artifact);
        artifact.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(artifact);

        ResolveMode(AbradeFactory.ModeDestroy,
            new IReadOnlyList<object>[]
            {
                Array.Empty<object>(),
                new object[] { artifact },
            });

        artifact.Zone.Should().Be(ZoneType.Graveyard,
            because: "CR 608.2b — target not on battlefield at resolution → no-op");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private IReadOnlyList<Majik.Core.Abilities.IEffect> ResolveMode(
        int mode, IReadOnlyList<object>[] targets)
    {
        var def = AbradeFactory.BuildDefinition(o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: mode,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        foreach (var fx in effects)
        {
            fx.Execute();
        }
        return effects;
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
