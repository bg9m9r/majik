using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 700.2d — modal "Choose one —" spell. Get Out, Duskmourn: House of
/// Horror, {U}{U}, two modes:
///   Mode 0: Counter target creature or enchantment spell.
///   Mode 1: Return one or two target creatures and/or enchantments you own
///           to your hand.
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/>, mirroring BantCharmTests / IzzetCharmTests.
/// </summary>
[Trait("Color", "U")]
public class GetOutTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public GetOutTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_MonoBlue()
    {
        var card = GetOutFactory.Create(_alice);

        card.Name.Should().Be("Get Out");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{U}{U} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Get Out", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Get Out");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void BuildDefinition_ExposesTwoModes_WithPerModeIntents()
    {
        var def = GetOutFactory.BuildDefinition(_alice, o => o, _stack);

        def.Modes.Should().HaveCount(2);
        def.Modes[GetOutFactory.ModeCounter].Should().Contain("Counter");
        def.Modes[GetOutFactory.ModeReturn].Should().Contain("Return");

        def.ModeIntentsOrEmpty.Should().HaveCount(2);
        def.ModeIntentsOrEmpty[GetOutFactory.ModeCounter].Should().Be(BotIntent.Counter);
        def.ModeIntentsOrEmpty[GetOutFactory.ModeReturn].Should().Be(BotIntent.Bounce);

        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[GetOutFactory.ModeCounter].MinTargets.Should().Be(0);
        def.TargetRequests[GetOutFactory.ModeCounter].MaxTargets.Should().Be(1);
        // CR — "one or two target creatures and/or enchantments"; MinTargets=0
        // so the unchosen mode doesn't gate the cast, but the chosen mode can
        // legally take up to two targets.
        def.TargetRequests[GetOutFactory.ModeReturn].MinTargets.Should().Be(0);
        def.TargetRequests[GetOutFactory.ModeReturn].MaxTargets.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — counter target creature or enchantment spell
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_CountersCreatureSpell()
    {
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        _stack.Push(bobSpell);

        var def = GetOutFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: GetOutFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 counters the creature spell and sends it to the graveyard");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Mode0_CountersEnchantmentSpell()
    {
        var bobAura = new Enchantment("Oblivion Ring", "{2}{W}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobAura, _bob);
        _stack.Push(bobSpell);

        var def = GetOutFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: GetOutFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobAura.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 counters the enchantment spell");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Mode0_IgnoresNoncreatureNonenchantmentSpell()
    {
        // An instant on the stack — Get Out can only counter creature or
        // enchantment spells (CR 608.2b).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = GetOutFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            new object[] { bobSpell },
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: GetOutFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "the counter mode no-ops on an instant spell (CR 608.2b)");
        _stack.IsEmpty.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mode 1 — return one or two creatures/enchantments you own to your hand
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_ReturnsOneOwnedCreatureToHand()
    {
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        aliceBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceBear);

        var def = GetOutFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { aliceBear },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: GetOutFactory.ModeReturn,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        aliceBear.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(aliceBear);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(aliceBear);
    }

    [Fact]
    public void Mode1_ReturnsTwoOwnedPermanentsToHand()
    {
        var aliceBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Controller = _alice };
        aliceBear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceBear);

        var aliceAura = new Enchantment("Wild Growth", "{G}") { Owner = _alice, Controller = _alice };
        aliceAura.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceAura);

        var def = GetOutFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { aliceBear, aliceAura },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: GetOutFactory.ModeReturn,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        aliceBear.Zone.Should().Be(ZoneType.Hand);
        aliceAura.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { aliceBear, aliceAura });
    }

    [Fact]
    public void Mode1_DoesNotReturnPermanentYouDoNotOwn()
    {
        // "you own" — Bob's creature cannot be returned by Alice's Get Out
        // even if it somehow lands in the target slot (CR 608.2b safety net).
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = GetOutFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            new object[] { bobBear },
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: GetOutFactory.ModeReturn,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            because: "Get Out only returns permanents you OWN to your hand");
    }

    // -----------------------------------------------------------------------
    // Choose-one pick-count cap
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseOne_RespectsPickCount_ExtraModesIgnored()
    {
        var def = GetOutFactory.BuildDefinition(_alice, o => o, _stack);

        var targets = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
        };

        var chosen = new ChosenSpellParams(
            ModeIndex: GetOutFactory.ModeCounter,
            X: null,
            Targets: targets,
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                GetOutFactory.ModeCounter,
                GetOutFactory.ModeReturn, // overflow — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(GetOutFactory.PickCount,
            because: "Choose-one caps at 1 effect regardless of how many indices the caller submits");
    }
}
