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
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Embereth Shieldbreaker // Battle Display (Throne of Eldraine,
/// {1}{R}).
///
/// Covers:
///   - Card identity (name, type, subtypes, P/T, mana cost).
///   - NamedCardFactory dispatch.
///   - Battle Display helper structural shape — one "target artifact"
///     TargetRequest, fixed-cost (no X), no modes.
///   - Battle Display resolve: destroys a target artifact (CR 701.7).
///   - Battle Display resolve on an illegal at-resolution target
///     (creature) is a clean no-op (CR 608.2b).
///
/// Adventure cast-from-hand-to-exile (CR 715) is deferred — see
/// <see cref="EmberethShieldbreakerFactory"/> XML doc.
/// </summary>
public class EmberethShieldbreakerTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EmberethShieldbreaker_IsCreature_HumanKnight_2_1_AtCost1R()
    {
        var card = EmberethShieldbreakerFactory.Create(_alice);

        card.Name.Should().Be("Embereth Shieldbreaker");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_EmberethShieldbreaker()
    {
        var card = NamedCardFactory.Create("Embereth Shieldbreaker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Embereth Shieldbreaker");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Knight).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
        card.Owner.Should().Be(_alice);
    }

    // -----------------------------------------------------------------------
    // Battle Display helper — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BattleDisplay_Helper_HasSingleArtifactTarget()
    {
        var def = EmberethShieldbreakerFactory.BuildAdventureSpell(_alice, o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("artifact");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Battle Display helper — resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void BattleDisplay_Resolve_DestroysArtifact()
    {
        // Bob has an artifact on the battlefield — legal target.
        var solRing = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(solRing);
        solRing.SetZone(ZoneType.Battlefield);

        var def = EmberethShieldbreakerFactory.BuildAdventureSpell(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { solRing } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // CR 701.7 — destroyed artifact is in its owner's graveyard.
        solRing.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(solRing);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(solRing);
    }

    [Fact]
    public void BattleDisplay_Resolve_IllegalTarget_IsNoOp()
    {
        // A creature (not an artifact) is an illegal target at resolution
        // per CR 608.2b — the spell does nothing. Unlike Swift End, Battle
        // Display has no companion clause, so the whole resolution
        // collapses to a no-op.
        var goblin = new Creature("Goblin Guide", "{R}", power: 2, toughness: 2)
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(goblin);
        goblin.SetZone(ZoneType.Battlefield);

        var def = EmberethShieldbreakerFactory.BuildAdventureSpell(_alice, o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { goblin } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // Creature untouched.
        goblin.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(goblin);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(goblin);
    }
}
