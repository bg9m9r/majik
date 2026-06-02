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
/// CR 700.2d — modal "Choose one —" spell. Esper Charm, Shards of Alara,
/// {W}{U}{B}, three modes:
///   Mode 0: Destroy target enchantment.
///   Mode 1: Draw two cards.
///   Mode 2: Target player discards two cards.
///
/// Tests exercise the EffectFactory directly with crafted
/// <see cref="ChosenSpellParams"/>, mirroring BantCharmTests / IzzetCharmTests.
/// </summary>
[Trait("Color", "M")]
public class EsperCharmTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public EsperCharmTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
    }

    private static IReadOnlyList<object>[] Slots(int mode, params object[] targets)
    {
        var slots = new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
            Array.Empty<object>(),
            Array.Empty<object>(),
        };
        slots[mode] = targets;
        return slots;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_EsperColors()
    {
        var card = EsperCharmFactory.Create(_alice);

        card.Name.Should().Be("Esper Charm");
        card.HasType(CardType.Instant).Should().BeTrue();
        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.White);
        colors.Should().Contain(ManaColor.Blue);
        colors.Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{W}{U}{B} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BuildDefinition_ExposesThreeModes_WithPerModeIntents()
    {
        var def = EsperCharmFactory.BuildDefinition(_alice, o => o);

        def.Modes.Should().HaveCount(3);
        def.Modes[EsperCharmFactory.ModeDestroyEnchantment].Should().Contain("Destroy");
        def.Modes[EsperCharmFactory.ModeDrawTwo].Should().Contain("Draw");
        def.Modes[EsperCharmFactory.ModeTargetDiscardTwo].Should().Contain("discards");

        def.ModeIntentsOrEmpty.Should().HaveCount(3);
        def.ModeIntentsOrEmpty[EsperCharmFactory.ModeDestroyEnchantment].Should().Be(BotIntent.Removal);
        def.ModeIntentsOrEmpty[EsperCharmFactory.ModeDrawTwo].Should().Be(BotIntent.Draw);
        def.ModeIntentsOrEmpty[EsperCharmFactory.ModeTargetDiscardTwo].Should().Be(BotIntent.Discard);

        def.TargetRequests.Should().HaveCount(3);
        def.TargetRequests[EsperCharmFactory.ModeDestroyEnchantment].MinTargets.Should().Be(0);
        def.TargetRequests[EsperCharmFactory.ModeDestroyEnchantment].MaxTargets.Should().Be(1);
        def.TargetRequests[EsperCharmFactory.ModeDrawTwo].MaxTargets.Should().Be(0);
        def.TargetRequests[EsperCharmFactory.ModeTargetDiscardTwo].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — destroy target enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode0_DestroyEnchantment_MovesEnchantmentToGraveyard()
    {
        var bobAura = new Enchantment("Pacifism", "{1}{W}") { Owner = _bob, Controller = _bob };
        bobAura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobAura);

        var def = EsperCharmFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: EsperCharmFactory.ModeDestroyEnchantment,
            X: null,
            Targets: Slots(EsperCharmFactory.ModeDestroyEnchantment, bobAura),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        bobAura.Zone.Should().Be(ZoneType.Graveyard,
            because: "mode 0 destroys the target enchantment");
    }

    [Fact]
    public void Mode0_DestroyEnchantment_IgnoresNonEnchantmentTarget()
    {
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobBear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobBear);

        var def = EsperCharmFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: EsperCharmFactory.ModeDestroyEnchantment,
            X: null,
            Targets: Slots(EsperCharmFactory.ModeDestroyEnchantment, bobBear), // not an enchantment — CR 608.2b
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            because: "the destroy-enchantment mode no-ops on a non-enchantment target");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — draw two cards
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode1_DrawTwo_MovesTwoCardsFromLibraryToCasterHand()
    {
        var top = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var next = new Instant("Opt", "{U}") { Owner = _alice };
        var bottom = new Instant("Negate", "{1}{U}") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        _alice.Zones.Library.AddCard(next);
        _alice.Zones.Library.AddCard(bottom);

        var def = EsperCharmFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: EsperCharmFactory.ModeDrawTwo,
            X: null,
            Targets: Slots(EsperCharmFactory.ModeDrawTwo),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { top, next });
        _alice.Zones.Library.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(bottom);
    }

    [Fact]
    public void Mode1_DrawTwo_EmptyLibrary_FlagsTriedToDrawFromEmpty()
    {
        // Library has only one card — the second draw hits an empty library.
        var only = new Instant("Opt", "{U}") { Owner = _alice };
        _alice.Zones.Library.AddCard(only);

        var def = EsperCharmFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: EsperCharmFactory.ModeDrawTwo,
            X: null,
            Targets: Slots(EsperCharmFactory.ModeDrawTwo),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(only);
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            because: "the second draw from an empty library flags the CR 704.5c loss SBA");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — target player discards two cards
    // -----------------------------------------------------------------------

    [Fact]
    public void Mode2_TargetDiscard_TargetPlayerDiscardsTwo()
    {
        var c1 = new Instant("Opt", "{U}") { Owner = _bob };
        var c2 = new Instant("Negate", "{1}{U}") { Owner = _bob };
        var c3 = new Instant("Counterspell", "{U}{U}") { Owner = _bob };
        _bob.Zones.Hand.AddCard(c1);
        _bob.Zones.Hand.AddCard(c2);
        _bob.Zones.Hand.AddCard(c3);

        var def = EsperCharmFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: EsperCharmFactory.ModeTargetDiscardTwo,
            X: null,
            Targets: Slots(EsperCharmFactory.ModeTargetDiscardTwo, _bob),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(1,
            because: "the targeted player discards two of their three cards");
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(2);
    }

    [Fact]
    public void Mode2_TargetDiscard_FewerThanTwoInHand_DiscardsWhatItCan()
    {
        var only = new Instant("Opt", "{U}") { Owner = _bob };
        _bob.Zones.Hand.AddCard(only);

        var def = EsperCharmFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: EsperCharmFactory.ModeTargetDiscardTwo,
            X: null,
            Targets: Slots(EsperCharmFactory.ModeTargetDiscardTwo, _bob),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.Zones.Hand.GetCards().Should().BeEmpty();
        _bob.Zones.Graveyard.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(only, because: "CR 701.7c — discard as many as possible");
    }

    // -----------------------------------------------------------------------
    // Choose-one pick-count cap
    // -----------------------------------------------------------------------

    [Fact]
    public void ChooseOne_RespectsPickCount_ExtraModesIgnored()
    {
        var def = EsperCharmFactory.BuildDefinition(_alice, o => o);

        var chosen = new ChosenSpellParams(
            ModeIndex: EsperCharmFactory.ModeDrawTwo,
            X: null,
            Targets: Slots(EsperCharmFactory.ModeDrawTwo),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: new[]
            {
                EsperCharmFactory.ModeDrawTwo,
                EsperCharmFactory.ModeDestroyEnchantment, // overflow — should be dropped
            });

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(EsperCharmFactory.PickCount,
            because: "Choose-one caps at 1 effect regardless of how many indices the caller submits");
    }
}
