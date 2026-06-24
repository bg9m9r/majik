using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AbueloAncestralEchoFactory"/>.
///
/// Abuelo, Ancestral Echo — {1}{W}{U} Legendary Creature — Spirit 2/2.
///   "Flying, ward {2}
///    {1}{W}{U}: Exile another target creature or artifact you control. Return
///    it to the battlefield under its owner's control at the beginning of the
///    next end step."
///
/// Covers (the card's UNIQUE behaviour + a single identity assert):
/// - Identity ({1}{W}{U} Legendary Creature — Spirit, 2/2, W/U, mana value 3).
/// - Flying + Ward {2} keyword markers.
/// - The activated ability's mana cost ({1}{W}{U}) + 1-of target request.
/// - Target gatherer: creatures and artifacts you control are legal; lands /
///   enchantments and opponent permanents are NOT; Abuelo itself is excluded.
/// - Resolution exiles the chosen permanent immediately.
/// - Delayed return: at the next end step it returns to its OWNER's battlefield.
/// </summary>
[Trait("Color", "M")]
public class AbueloAncestralEchoFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Abuelo_Identity()
    {
        var c = AbueloAncestralEchoFactory.Create(_alice);

        c.Name.Should().Be("Abuelo, Ancestral Echo");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasEffectiveSupertype(CardSupertype.Legendary).Should().BeTrue("Abuelo is Legendary");
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue("Abuelo is a Spirit");
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.ManaCost.Should().Be("{1}{W}{U}");
        c.ManaCostValue.TotalValue.Should().Be(3, "CR 202.3 — {1}{W}{U} has mana value 3");

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White).And.Contain(ManaColor.Blue,
            "Abuelo has {W}{U} pips in its mana cost");
        colors.Should().HaveCount(2, "white/blue color identity");
    }

    // -----------------------------------------------------------------------
    // Keyword markers — Flying + Ward {2}
    // -----------------------------------------------------------------------

    [Fact]
    public void Abuelo_HasFlyingAndWard()
    {
        var c = AbueloAncestralEchoFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying", "CR 702.9 — Abuelo has Flying");
        keywords.Should().Contain("Ward", "CR 702.21 — Abuelo has ward {2}");
    }

    // -----------------------------------------------------------------------
    // Activated ability shape — {1}{W}{U} cost + single target request
    // -----------------------------------------------------------------------

    [Fact]
    public void Abuelo_ActivatedAbility_HasManaCostAndSingleTarget()
    {
        var c = AbueloAncestralEchoFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        var manaCost = ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "CR 602.1 — the blink activation has a single mana cost").Subject.Cost;
        manaCost.Generic.Should().Be(1, "{1} generic pip");
        manaCost.White.Should().Be(1, "{W} pip");
        manaCost.Blue.Should().Be(1, "{U} pip");
        manaCost.TotalValue.Should().Be(3, "{1}{W}{U} = mana value 3");

        var request = ability.TargetRequests.Single();
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Target gatherer — creatures/artifacts you control, not lands/opponents/self
    // -----------------------------------------------------------------------

    [Fact]
    public void Abuelo_TargetGatherer_OnlyOwnCreaturesAndArtifacts()
    {
        var myCreature = MakeBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);
        var myArtifact = MakeBattlefield(new Artifact("Sol Ring", "{1}"), _alice);
        var myLand = MakeBattlefield(new Land("Forest"), _alice);
        var myEnchant = MakeBattlefield(new Enchantment("Pacifism", "{1}{W}"), _alice);
        var oppCreature = MakeBattlefield(new Creature("Bob's Bear", "{1}{G}", 2, 2), _bob);

        var abuelo = AbueloAncestralEchoFactory.Create(_alice);
        abuelo.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(abuelo);

        var ability = abuelo.Abilities.OfType<ActivatedAbility>().Single();
        var request = ability.TargetRequests.Single();

        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, stack);
        var candidates = request.CandidateGatherer!(ctx).OfType<Permanent>().ToList();

        candidates.Should().Contain(myCreature, "your creatures are legal");
        candidates.Should().Contain(myArtifact, "your artifacts are legal");
        candidates.Should().NotContain(myLand, "lands are not creatures or artifacts");
        candidates.Should().NotContain(myEnchant, "enchantments are not creatures or artifacts");
        candidates.Should().NotContain(oppCreature, "'you control' excludes opponent permanents");
        candidates.Should().NotContain(abuelo, "CR 115.5b — 'another' excludes Abuelo itself");
    }

    // -----------------------------------------------------------------------
    // Resolution exiles the chosen permanent
    // -----------------------------------------------------------------------

    [Fact]
    public void Abuelo_Blink_ExilesChosenPermanent()
    {
        var grizzly = MakeBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);

        var abuelo = AbueloAncestralEchoFactory.Create(_alice);
        var ability = abuelo.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { grizzly } });

        foreach (var effect in ability.Effects) effect.Execute();

        grizzly.Zone.Should().Be(ZoneType.Exile, "the activated ability exiles the target");
        _alice.Zones.Exile.GetCards().Should().Contain(grizzly);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(grizzly);
    }

    // -----------------------------------------------------------------------
    // Delayed return — at the next end step, returns to OWNER's battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Abuelo_Blink_ReturnsToOwnersBattlefield_AtNextEndStep()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var grizzly = MakeBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _alice);

        var abuelo = AbueloAncestralEchoFactory.Create(_alice, eventBus: bus, triggers: triggers);
        var ability = abuelo.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { grizzly } });

        foreach (var effect in ability.Effects) effect.Execute();

        grizzly.Zone.Should().Be(ZoneType.Exile, "exiled immediately on resolution");

        // Fire the next end step — delayed return rider should enqueue + resolve.
        bus.Publish(new StepStartedEvent(StepStateType.End, _alice));
        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "delayed return fires on the first end step after the activation (CR 603.7)");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        grizzly.Zone.Should().Be(ZoneType.Battlefield,
            "the exiled permanent returns at the beginning of the next end step (CR 603.7)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(grizzly);
        grizzly.Controller.Should().BeSameAs(_alice,
            "returns under its owner's control (CR 108.3 / CR 614)");
    }

    private T MakeBattlefield<T>(T perm, Player owner) where T : Permanent
    {
        perm.SetOwner(owner);
        perm.SetController(owner);
        perm.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(perm);
        return perm;
    }
}
