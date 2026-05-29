using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WarpingWailFactory"/>.
///
/// Warping Wail (Oath of the Gatewatch, {1}{C}):
///   CR 700.2d — modal "Choose one —" instant with 3 modes.
///   Mode 0: Exile target creature with power or toughness 1 or less.
///   Mode 1: Counter target sorcery spell.
///   Mode 2: Create a 1/1 colorless Eldrazi Scion creature token with
///           "Sacrifice this token: Add {C}."
/// </summary>
public class WarpingWailTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public WarpingWailTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void WarpingWail_Create_HasInstantShape_Colorless()
    {
        var card = WarpingWailFactory.Create(_alice);

        card.Name.Should().Be("Warping Wail");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{C} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WarpingWail_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Warping Wail", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Warping Wail");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void WarpingWail_BuildDefinition_ExposesModes_AndTargetRequests()
    {
        var def = WarpingWailFactory.BuildDefinition(
            _alice, o => o, new[] { _alice, _bob }, _stack);

        def.Modes.Should().HaveCount(3);
        def.Modes[WarpingWailFactory.ModeExileCreature].Should().Contain("Exile");
        def.Modes[WarpingWailFactory.ModeCounterSorcery].Should().Contain("Counter");
        def.Modes[WarpingWailFactory.ModeCreateScion].Should().Contain("Scion");

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[WarpingWailFactory.ModeExileCreature].MinTargets.Should().Be(0);
        def.TargetRequests[WarpingWailFactory.ModeCounterSorcery].MinTargets.Should().Be(0);
        def.TargetRequests[WarpingWailFactory.ModeCreateScion].MinTargets.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — exile target creature with power or toughness 1 or less
    // -----------------------------------------------------------------------

    [Fact]
    public void WarpingWail_Mode0_ExilesCreatureWithToughness1OrLess()
    {
        var weenie = new Creature("Goblin Guide", "{R}", 2, 1) { Owner = _bob, Controller = _bob };
        weenie.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(weenie);

        var def = WarpingWailFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, _stack);
        var targets = new IReadOnlyList<object>[]
        {
            new object[] { weenie },
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WarpingWailFactory.ModeExileCreature, X: null, Targets: targets,
            Mana: ManaPayment.Empty, AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        weenie.Zone.Should().Be(ZoneType.Exile,
            because: "toughness 1 qualifies the creature for exile");
    }

    [Fact]
    public void WarpingWail_Mode0_DoesNotExileBigCreature()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = WarpingWailFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, _stack);
        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bear },
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WarpingWailFactory.ModeExileCreature, X: null, Targets: targets,
            Mana: ManaPayment.Empty, AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            because: "power 2 and toughness 2 — neither stat is 1 or less");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — counter target sorcery spell
    // -----------------------------------------------------------------------

    [Fact]
    public void WarpingWail_Mode1_CountersSorcerySpell()
    {
        var bobCard = new Sorcery("Lava Spike", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCard, _bob);
        _stack.Push(bobSpell);

        var def = WarpingWailFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, _stack);
        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { bobSpell },
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WarpingWailFactory.ModeCounterSorcery, X: null, Targets: targets,
            Mana: ManaPayment.Empty, AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "the sorcery is countered and put into its owner's graveyard");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void WarpingWail_Mode1_DoesNotCounterInstantSpell()
    {
        var bobCard = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobCard, _bob);
        _stack.Push(bobSpell);

        var def = WarpingWailFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, _stack);
        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { bobSpell },
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WarpingWailFactory.ModeCounterSorcery, X: null, Targets: targets,
            Mana: ManaPayment.Empty, AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobCard.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "mode 1 only counters sorcery spells, not instants");
        _stack.IsEmpty.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mode 2 — create a 1/1 colorless Eldrazi Scion token
    // -----------------------------------------------------------------------

    [Fact]
    public void WarpingWail_Mode2_CreatesEldraziScionToken()
    {
        var def = WarpingWailFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, _stack);
        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: WarpingWailFactory.ModeCreateScion, X: null, Targets: targets,
            Mana: ManaPayment.Empty, AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        var scion = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.Name == "Eldrazi Scion");

        scion.Should().NotBeNull(because: "mode 2 creates a 1/1 Eldrazi Scion under the caster");
        scion!.Power.Should().Be(1);
        scion.Toughness.Should().Be(1);
        scion.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        scion.HasSubtype(CardSubtype.Scion).Should().BeTrue();
        scion.IsToken.Should().BeTrue();
        scion.Abilities.OfType<ManaAbility>().Should().NotBeEmpty(
            because: "the Scion has \"Sacrifice this token: Add {C}.\" wired as a mana ability");
    }

    // -----------------------------------------------------------------------
    // CR 700.2d — choose-one pick-count cap
    // -----------------------------------------------------------------------

    [Fact]
    public void WarpingWail_ChooseOne_CapsAtSingleMode()
    {
        var def = WarpingWailFactory.BuildDefinition(_alice, o => o, new[] { _alice, _bob }, _stack);
        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null, Targets: targets,
            Mana: ManaPayment.Empty, AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                WarpingWailFactory.ModeCreateScion,
                WarpingWailFactory.ModeExileCreature,
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1, because: "CR 700.2d — Choose one — picks exactly one mode");
    }
}
