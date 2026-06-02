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
/// Unit tests for <see cref="RedElementalBlastFactory"/> (Alpha, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose one —
///     • Counter target blue spell.
///     • Destroy target blue permanent."
///
/// CR 700.2d — modal "Choose one —" with per-mode targeting; the modal shape
/// mirrors <see cref="RipApartFactory"/>. The counter mode mirrors
/// <see cref="CounterspellFactory"/> / Izzet Charm mode 0; the destroy mode
/// mirrors <see cref="RipApartFactory"/>'s destroy clause. The "blue"
/// restriction is on the TARGET (target blue spell / target blue permanent),
/// enforced at gather time and re-checked at resolution (CR 608.2b).
/// </summary>
[Trait("Color", "R")]
public class RedElementalBlastFactoryTests
{
    private readonly Majik.Core.Events.EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public RedElementalBlastFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    private static IReadOnlyList<object>[] Slots(int modeIndex, params object[] targets)
    {
        var slots = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        slots[modeIndex] = targets;
        return slots;
    }

    private ChosenSpellParams Chosen(int modeIndex, params object[] targets) =>
        new(
            ModeIndex: modeIndex,
            X: null,
            Targets: Slots(modeIndex, targets),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void RedElementalBlast_Create_HasInstantShape_Red()
    {
        var card = RedElementalBlastFactory.Create(_alice);

        card.Name.Should().Be("Red Elemental Blast");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.ManaCostValue.TotalValue.Should().Be(1, because: "{R} = mana value 1");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RedElementalBlast_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Red Elemental Blast", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Red Elemental Blast");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void RedElementalBlast_BuildDefinition_ExposesModes_AndPerModeTargets()
    {
        var def = RedElementalBlastFactory.BuildDefinition(o => o, _stack);

        def.Modes.Should().HaveCount(2);
        def.Modes[RedElementalBlastFactory.ModeCounter].Should().Contain("Counter");
        def.Modes[RedElementalBlastFactory.ModeDestroy].Should().Contain("Destroy");

        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[RedElementalBlastFactory.ModeCounter].MinTargets.Should().Be(0);
        def.TargetRequests[RedElementalBlastFactory.ModeCounter].MaxTargets.Should().Be(1);
        def.TargetRequests[RedElementalBlastFactory.ModeCounter].Description.Should().Contain("blue spell");
        def.TargetRequests[RedElementalBlastFactory.ModeDestroy].MinTargets.Should().Be(0);
        def.TargetRequests[RedElementalBlastFactory.ModeDestroy].MaxTargets.Should().Be(1);
        def.TargetRequests[RedElementalBlastFactory.ModeDestroy].Description.Should().Contain("blue permanent");
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mode 0 — counter target blue spell.
    // -----------------------------------------------------------------------

    [Fact]
    public void RedElementalBlast_Mode0_CountersBlueSpell()
    {
        var bobCard = new Instant("Counterspell", "{U}{U}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCard, _bob);
        _stack.Push(bobSpell);

        var def = RedElementalBlastFactory.BuildDefinition(o => o, _stack);
        foreach (var e in def.EffectFactory(Chosen(RedElementalBlastFactory.ModeCounter, bobSpell)))
            e.Execute();

        bobCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 counters the blue spell and sends it to the graveyard (CR 701.5)");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void RedElementalBlast_Mode0_DoesNotCounterNonBlueSpell()
    {
        // CR 608.2b — "target blue spell" re-checked at resolution: a red spell
        // is not a legal target, so the counter is a no-op.
        var bobCard = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCard, _bob);
        _stack.Push(bobSpell);

        var def = RedElementalBlastFactory.BuildDefinition(o => o, _stack);
        foreach (var e in def.EffectFactory(Chosen(RedElementalBlastFactory.ModeCounter, bobSpell)))
            e.Execute();

        _stack.IsEmpty.Should().BeFalse(
            because: "a non-blue spell is not a legal target, so it is not countered");
        bobCard.Zone.Should().NotBe(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Mode 1 — destroy target blue permanent.
    // -----------------------------------------------------------------------

    [Fact]
    public void RedElementalBlast_Mode1_DestroysBluePermanent()
    {
        var creature = new Creature("Snapcaster Mage", "{1}{U}", 2, 1) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = RedElementalBlastFactory.BuildDefinition(o => o, _stack);
        foreach (var e in def.EffectFactory(Chosen(RedElementalBlastFactory.ModeDestroy, creature)))
            e.Execute();

        creature.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 1 destroys the targeted blue permanent (CR 701.7)");
    }

    [Fact]
    public void RedElementalBlast_Mode1_DoesNotDestroyNonBluePermanent()
    {
        // CR 608.2b — "target blue permanent" re-checked at resolution: a green
        // creature is not a legal target, so the destroy is a no-op.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = RedElementalBlastFactory.BuildDefinition(o => o, _stack);
        foreach (var e in def.EffectFactory(Chosen(RedElementalBlastFactory.ModeDestroy, creature)))
            e.Execute();

        creature.Zone.Should().Be(ZoneType.Battlefield,
            because: "a non-blue permanent is not a legal target, so it is not destroyed");
    }
}
