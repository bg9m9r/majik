using Majik.Core.CardData;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for the JSON-defined Eiganjo Castle card.
///
/// Eiganjo Castle (Champions of Kamigawa) — Legendary Land.
/// Oracle text:
///   "{T}: Add {W}.
///    {W}, {T}: Prevent the next 2 damage that would be dealt to target
///     legendary creature this turn."
///
/// Structural twin of Minamo, School at Water's Edge — Legendary Land with a
/// mana ability plus a "{cost}, {T}: do-thing-to-target-legendary" activated
/// ability. PLAN 01 (Slice F) — the damage-prevention effect is now a REAL
/// targeted effect (<c>prevent_damage_target</c>): it declares a
/// <see cref="ActivatedAbility.TargetRequests"/> and, on resolution, registers
/// a CR 615 prevention shield bound to the chosen legendary creature. (It was
/// previously a no-op <c>prevent_damage_target_stub</c> that Slice F missed.)
///
/// Covers:
/// - Card identity (name, Legendary supertype, Land type)
/// - {T}: Add {W} mana ability (presence + white output)
/// - {W}, {T} activated ability cost composition (ManaCostCost({W}) + Tap)
/// - The prevent-damage effect declares a target + prevents up to 2 damage to
///   the chosen legendary creature this turn (no longer a no-op).
/// </summary>
public class EiganjoCastleTests
{
    private readonly Majik.Core.Events.EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext(Majik.Core.Stack.Stack stack) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);

    /// <summary>
    /// Build the JSON Eiganjo Castle with an explicit <see cref="ReplacementBus"/>
    /// (the prod game wires one per player), so its prevent-damage effect can
    /// register its shield. <see cref="NamedCardFactory.Create"/> uses a null
    /// bus, which is fine for the structural tests but not the live-prevention
    /// ones.
    /// </summary>
    private Land BuildEiganjoWithBus(ReplacementBus bus) =>
        (Land)CardDefinitionFactory.Build(
            CardDefinitionLoader.FromEmbeddedResource("eiganjo-castle"), _alice, bus);

    private static Creature OnBattlefield(Creature creature, Player owner)
    {
        creature.SetOwner(owner);
        creature.SetController(owner);
        owner.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);
        return creature;
    }

    private async Task ActivateAndResolve(ActivatedAbility ability, object? chosen)
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new AbilityActivationFlow(stack, _bus);
        var ctx = NewContext(stack);

        var agent = new ScriptedAgent();
        agent.QueueTargets(chosen != null ? new[] { chosen } : System.Array.Empty<object>());

        await flow.ActivateAsync(
            _alice, ability, targetRequests: ability.TargetRequests,
            cost: null, agent: agent, ctx: ctx);
        await ability.ResolveAsync(agent, ctx);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_IsLegendary()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);

        eiganjo.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Eiganjo_IsLand()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);

        eiganjo.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void Eiganjo_OwnerAndControllerAreSet()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);

        eiganjo.Owner.Should().BeSameAs(_alice);
        eiganjo.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {W} mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_HasExactlyOneManaAbility()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);

        eiganjo.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Eiganjo_ManaAbility_ProducesWhite()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);
        var mana = eiganjo.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.White.Should().Be(1, "Eiganjo Castle taps for exactly one {W}");
        mana.ManaGenerated.Generic.Should().Be(0, "no colorless component");
    }

    // -----------------------------------------------------------------------
    // {W}, {T}: Prevent the next 2 damage to target legendary creature
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_HasExactlyOneActivatedAbility()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);

        eiganjo.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "only the prevent ability; the mana ability is a ManaAbility, not ActivatedAbility");
    }

    [Fact]
    public void Eiganjo_PreventAbility_HasManaCostCost()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Eiganjo_PreventAbility_ManaCostIsW()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        manaCost.White.Should().Be(1, "the {W} component");
        manaCost.Generic.Should().Be(0, "no generic component");
    }

    [Fact]
    public void Eiganjo_PreventAbility_HasTapSelfCost()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        // The {T} symbol is built as an AdditionalCost.Tap on the source.
        ability.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle("the {T} symbol composes a tap-self additional cost");
    }

    [Fact]
    public void Eiganjo_PreventAbility_HasExactlyTwoCosts()
    {
        var eiganjo = (Land)NamedCardFactory.Create("Eiganjo Castle", _alice);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(2, "ManaCostCost({W}) + tap-self");
    }

    // -----------------------------------------------------------------------
    // Prevent-damage effect — real targeted prevention (PLAN 01 Slice F)
    // -----------------------------------------------------------------------

    [Fact]
    public void Eiganjo_PreventAbility_DeclaresATargetRequest()
    {
        var eiganjo = BuildEiganjoWithBus(new ReplacementBus());
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        ability.TargetRequests.Should().HaveCount(1,
            "the prevent effect targets a legendary creature");
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task Eiganjo_PreventsNext2Damage_ToChosenLegendaryCreature()
    {
        var bus = new ReplacementBus();
        var eiganjo = BuildEiganjoWithBus(bus);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        var legend = OnBattlefield(
            new Creature("Legendary Bear", "{1}{G}", 2, 2, new[] { CardSupertype.Legendary }), _alice);

        await ActivateAndResolve(ability, legend);

        // 3 incoming damage to the chosen creature → 2 prevented, 1 passes.
        var src = new Creature("Attacker", "{2}{R}", 3, 3);
        bus.Apply(new DamageIntent(src, 3, TargetCreature: legend))!
            .Amount.Should().Be(3 - 2, "Eiganjo's 2-point pool is prevented from the chosen creature");
    }

    [Fact]
    public async Task Eiganjo_DoesNotPrevent_DamageToOtherCreatures()
    {
        var bus = new ReplacementBus();
        var eiganjo = BuildEiganjoWithBus(bus);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        var legend = OnBattlefield(
            new Creature("Legendary Bear", "{1}{G}", 2, 2, new[] { CardSupertype.Legendary }), _alice);
        var bystander = OnBattlefield(new Creature("Plain Bear", "{1}{G}", 2, 2), _bob);

        await ActivateAndResolve(ability, legend);

        var src = new Creature("Attacker", "{2}{R}", 3, 3);
        bus.Apply(new DamageIntent(src, 2, TargetCreature: bystander))!
            .Amount.Should().Be(2, "the shield is bound to the chosen creature only");
    }

    [Fact]
    public async Task Eiganjo_NoTargetChosen_FizzlesCleanly_NoShieldRegistered()
    {
        var bus = new ReplacementBus();
        var eiganjo = BuildEiganjoWithBus(bus);
        var ability = eiganjo.Abilities.OfType<ActivatedAbility>().Single();

        var legend = OnBattlefield(
            new Creature("Legendary Bear", "{1}{G}", 2, 2, new[] { CardSupertype.Legendary }), _alice);

        // CR 608.2b — no legal target supplied → no shield registered.
        await ActivateAndResolve(ability, chosen: null);

        var src = new Creature("Attacker", "{2}{R}", 3, 3);
        bus.Apply(new DamageIntent(src, 3, TargetCreature: legend))!
            .Amount.Should().Be(3, "no target was chosen, so no damage is prevented");
    }
}
