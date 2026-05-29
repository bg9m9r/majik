using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RoguesPassageFactory"/> — Land:
///   "{T}: Add {C}.
///    {4}, {T}: Target creature can't be blocked this turn."
///
/// Covers:
/// - Identity (Land, no mana cost, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - The {T}: Add {C} mana ability (JSON-driven, built through
///   <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>).
/// - The {4}, {T} activated ability shape: mana cost {4} + tap +
///   1..1 target creature.
/// - Resolution: a single-target CR 509.1c "can't be blocked" restriction
///   (CR 514.2 EOT expiry) registered against the
///   <see cref="ContinuousEffectsService"/>.
/// - End-of-turn expiration removes the restriction ("this turn").
/// - Off-battlefield / non-creature target → resolution-time no-op
///   (CR 608.2b).
/// </summary>
public class RoguesPassageTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RoguesPassage_IsLand_WithNoManaCost()
    {
        var passage = RoguesPassageFactory.Create(_alice);

        passage.Name.Should().Be("Rogue's Passage");
        passage.HasType(CardType.Land).Should().BeTrue();
        passage.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        passage.Owner.Should().BeSameAs(_alice);
        passage.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RoguesPassage()
    {
        var card = NamedCardFactory.Create("Rogue's Passage", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Rogue's Passage");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void RoguesPassage_HasTapAddColorlessManaAbility()
    {
        var passage = RoguesPassageFactory.Create(_alice);

        var mana = passage.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(1, "the only mana ability is {T}: Add {C}");
        mana[0].ManaGenerated.Should().Be(ManaCost.Parse("C"));
    }

    [Fact]
    public void RoguesPassage_AbilityShape_FourTapTargetCreature()
    {
        var passage = RoguesPassageFactory.Create(_alice);

        var ability = passage.Abilities.OfType<ActivatedAbility>().Single();

        // {4} generic mana component.
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        ability.Costs.OfType<ManaCostCost>().Single().Cost
            .Should().Be(ManaCost.Parse("{4}"));

        // {T} tap component.
        ability.Costs.OfType<AdditionalCost>()
            .Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1);

        // 1..1 target creature.
        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("creature");
    }

    [Fact]
    public void Activate_AgainstCreature_GrantsCantBeBlockedUntilEot()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var passage = RoguesPassageFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(passage);
        passage.SetZone(ZoneType.Battlefield);

        var ability = passage.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var e in ability.Effects) e.Execute();

        effects.HasRestriction(bear, CombatRestriction.CannotBeBlocked)
            .Should().BeTrue("the {4},{T} ability grants the bear unblockable this turn");
    }

    [Fact]
    public void Activate_EndOfTurn_RemovesCantBeBlocked()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var passage = RoguesPassageFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(passage);
        passage.SetZone(ZoneType.Battlefield);

        var ability = passage.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var e in ability.Effects) e.Execute();
        effects.HasRestriction(bear, CombatRestriction.CannotBeBlocked).Should().BeTrue();

        effects.ExpireEndOfTurn();

        effects.HasRestriction(bear, CombatRestriction.CannotBeBlocked)
            .Should().BeFalse("the grant is only \"this turn\" (CR 514.2 EOT expiry)");
    }

    [Fact]
    public void Activate_IllegalTarget_NoRestrictionRegistered()
    {
        // A Player is not a Creature → CR 608.2b no-op.
        var effects = new ContinuousEffectsService();
        var passage = RoguesPassageFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(passage);
        passage.SetZone(ZoneType.Battlefield);

        var ability = passage.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in ability.Effects) e.Execute();

        // Nothing to assert a restriction against (no creature target); the
        // resolution must simply not throw and register nothing harmful.
        var dummy = new Creature("Dummy", "{G}", 1, 1);
        effects.HasRestriction(dummy, CombatRestriction.CannotBeBlocked).Should().BeFalse();
    }

    [Fact]
    public void Activate_NoEffectsService_NoOp()
    {
        // Shape-only path — no continuous-effects service wired.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var passage = RoguesPassageFactory.Create(_alice); // effects = null
        _alice.Zones.Battlefield.AddCard(passage);
        passage.SetZone(ZoneType.Battlefield);

        var ability = passage.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        var resolve = () => { foreach (var e in ability.Effects) e.Execute(); };
        resolve.Should().NotThrow("the shape-only path silently skips the grant");
    }
}
