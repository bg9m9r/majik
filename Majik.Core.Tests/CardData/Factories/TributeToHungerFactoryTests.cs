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
/// Unit tests for <see cref="TributeToHungerFactory"/> (Innistrad, {2}{B}).
///
/// Instant. Oracle text:
///   "Target opponent sacrifices a creature of their choice.
///    You gain life equal to that creature's toughness."
///
/// Covers:
/// - Identity ({2}{B} black Instant, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Opponent with a 2/3 creature — creature is sacrificed (moved to
///   graveyard via CR 701.16) AND caster gains 3 life (the toughness).
/// - Opponent with no creatures — no sacrifice, no lifegain (no-op).
/// </summary>
public class TributeToHungerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeToHunger_IsInstant_AtCost2B()
    {
        var card = TributeToHungerFactory.Create(_alice);

        card.Name.Should().Be("Tribute to Hunger");
        card.ManaCost.Should().Be("{2}{B}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TributeToHunger()
    {
        var card = NamedCardFactory.Create("Tribute to Hunger", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Tribute to Hunger");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — opponent has a 2/3 creature
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeToHunger_OpponentHasCreature_SacrificesIt_AndCasterGainsLifeEqualToToughness()
    {
        // Bob controls a 2/3 creature (toughness 3 → Alice should gain 3 life).
        var creature = NewCreature(_bob, "Wall of Roots", "{G}", power: 2, toughness: 3);
        var aliceStarting = _alice.LifeTotal;

        Resolve(_bob);

        creature.Zone.Should().Be(ZoneType.Graveyard,
            "edict — opponent sacrifices the creature (CR 701.16)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
        _alice.LifeTotal.Should().Be(aliceStarting + 3,
            "caster gains life equal to the sacrificed creature's toughness");
    }

    // -----------------------------------------------------------------------
    // Resolution — opponent has no creatures → no-op
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeToHunger_OpponentHasNoCreatures_IsNoOp()
    {
        // Bob controls nothing — neither a sacrifice nor lifegain should occur.
        var aliceStarting = _alice.LifeTotal;

        Resolve(_bob);

        _alice.LifeTotal.Should().Be(aliceStarting,
            "no creature to sacrifice → no lifegain");
        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TributeToHunger_BuildSpellDefinition_DeclaresSingleTargetOpponentRequest()
    {
        var def = TributeToHungerFactory.BuildSpellDefinition(_alice, t => t);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target opponent");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Resolve Tribute to Hunger targeting <paramref name="opponent"/> without
    /// a full cast harness — exercises the EffectFactory directly.
    /// </summary>
    private void Resolve(Player opponent)
    {
        var def = TributeToHungerFactory.BuildSpellDefinition(_alice, t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { opponent } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewCreature(Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
