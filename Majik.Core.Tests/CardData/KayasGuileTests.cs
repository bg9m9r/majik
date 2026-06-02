using FluentAssertions;
using Majik.Core.Abilities;
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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="KayasGuileFactory"/>.
///
/// Kaya's Guile (Modern Horizons, {1}{W}{B}):
///   CR 700.2e — modal "Choose two —" instant with 4 modes.
///   Mode 0: Each opponent sacrifices a creature of their choice.
///   Mode 1: Exile all opponents' graveyards.
///   Mode 2: Create a 1/1 white and black Spirit creature token with flying.
///   Mode 3: You gain 4 life.
///   Entwine {3} (CR 702.41) — choose all if you pay the entwine cost.
///
/// Modal shape mirrors <see cref="KolaghansCommandFactory"/> (Choose two,
/// four modes). The "each opponent sacrifices … of their choice" body mirrors
/// <see cref="SheoldredsEdictFactory"/>; entwine mode-expansion mirrors
/// <see cref="ToothAndNailFactory"/> (both indices supplied via
/// <see cref="ChosenSpellParams.ModeIndexes"/>).
/// </summary>
public class KayasGuileTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private ChosenSpellParams ChooseModes(params int[] modes) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob },
            ModeIndexes: modes);

    // -----------------------------------------------------------------------
    // Identity + dispatcher
    // -----------------------------------------------------------------------

    [Fact]
    public void KayasGuile_Create_HasInstantShape_WhiteBlack()
    {
        var card = KayasGuileFactory.Create(_alice);

        card.Name.Should().Be("Kaya's Guile");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{1}{W}{B} = mana value 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KayasGuile_NamedCardFactory_Dispatch()
    {
        var dispatched = NamedCardFactory.Create("Kaya's Guile", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Kaya's Guile");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void KayasGuile_BuildDefinition_ExposesFourModes_PickTwo()
    {
        var def = KayasGuileFactory.BuildDefinition(_alice, new[] { _alice, _bob }, agent: null);

        def.Modes.Should().HaveCount(4);
        def.Modes[KayasGuileFactory.ModeEachOpponentSacrifices].Should().Contain("sacrifices");
        def.Modes[KayasGuileFactory.ModeExileGraveyards].Should().Contain("Exile");
        def.Modes[KayasGuileFactory.ModeCreateSpirit].Should().Contain("Spirit");
        def.Modes[KayasGuileFactory.ModeGainLife].Should().Contain("gain");

        KayasGuileFactory.PickCount.Should().Be(2, because: "CR 700.2e — Choose two —");
        // No printed mode takes a cast-time target.
        def.TargetRequests.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Mode 0 — each opponent sacrifices a creature of their choice.
    // -----------------------------------------------------------------------

    [Fact]
    public void KayasGuile_Mode0_EachOpponentSacrificesACreature()
    {
        var bobCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bobCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobCreature);

        // Alice's own creature must NOT be sacrificed ("each opponent").
        var aliceCreature = new Creature("Llanowar Elves", "{G}", 1, 1) { Owner = _alice, Controller = _alice };
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var def = KayasGuileFactory.BuildDefinition(_alice, new[] { _alice, _bob }, agent: null);

        foreach (var e in def.EffectFactory(
            ChooseModes(KayasGuileFactory.ModeEachOpponentSacrifices, KayasGuileFactory.ModeGainLife)))
            e.Execute();

        bobCreature.Zone.Should().Be(ZoneType.Graveyard,
            because: "each opponent sacrifices a creature");
        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            because: "the caster is not an opponent and sacrifices nothing");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — exile all opponents' graveyards.
    // -----------------------------------------------------------------------

    [Fact]
    public void KayasGuile_Mode1_ExilesAllOpponentsGraveyards()
    {
        var bobCard = new Instant("Lightning Bolt", "{R}") { Owner = _bob };
        bobCard.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobCard);

        // The caster's own graveyard is untouched ("opponents'").
        var aliceCard = new Instant("Opt", "{U}") { Owner = _alice };
        aliceCard.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(aliceCard);

        var def = KayasGuileFactory.BuildDefinition(_alice, new[] { _alice, _bob }, agent: null);

        foreach (var e in def.EffectFactory(
            ChooseModes(KayasGuileFactory.ModeExileGraveyards, KayasGuileFactory.ModeGainLife)))
            e.Execute();

        bobCard.Zone.Should().Be(ZoneType.Exile, because: "an opponent's graveyard is exiled");
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
        aliceCard.Zone.Should().Be(ZoneType.Graveyard,
            because: "only OPPONENTS' graveyards are exiled, not the caster's");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — create a 1/1 white and black Spirit token with flying.
    // -----------------------------------------------------------------------

    [Fact]
    public void KayasGuile_Mode2_CreatesWhiteBlackFlyingSpiritToken()
    {
        var def = KayasGuileFactory.BuildDefinition(_alice, new[] { _alice, _bob }, agent: null);

        foreach (var e in def.EffectFactory(
            ChooseModes(KayasGuileFactory.ModeCreateSpirit, KayasGuileFactory.ModeGainLife)))
            e.Execute();

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .SingleOrDefault(c => c.IsToken);
        token.Should().NotBeNull();
        token!.Power.Should().Be(1);
        token.Toughness.Should().Be(1);
        token.Subtypes.Should().Contain(CardSubtype.Spirit);
        token.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue();
        CardColors.GetColors(token).Should().BeEquivalentTo(new[] { ManaColor.White, ManaColor.Black });
    }

    // -----------------------------------------------------------------------
    // Mode 3 — you gain 4 life.
    // -----------------------------------------------------------------------

    [Fact]
    public void KayasGuile_Mode3_GainsFourLife()
    {
        var def = KayasGuileFactory.BuildDefinition(_alice, new[] { _alice, _bob }, agent: null);

        foreach (var e in def.EffectFactory(
            ChooseModes(KayasGuileFactory.ModeGainLife, KayasGuileFactory.ModeCreateSpirit)))
            e.Execute();

        _alice.LifeTotal.Should().Be(24, because: "mode 3 gains the caster 4 life");
    }

    // -----------------------------------------------------------------------
    // CR 700.2e — exactly two distinct modes resolve.
    // -----------------------------------------------------------------------

    [Fact]
    public void KayasGuile_ChooseTwo_OnlyTwoDistinctModesResolve()
    {
        var def = KayasGuileFactory.BuildDefinition(_alice, new[] { _alice, _bob }, agent: null);

        // Two distinct picks → two effects.
        def.EffectFactory(
            ChooseModes(KayasGuileFactory.ModeCreateSpirit, KayasGuileFactory.ModeGainLife))
            .Should().HaveCount(2);

        // A duplicate is ignored (CR 700.2e — distinct modes).
        def.EffectFactory(
            ChooseModes(KayasGuileFactory.ModeGainLife, KayasGuileFactory.ModeGainLife))
            .Should().HaveCount(1);

        // A third pick beyond the pick count is dropped (no entwine).
        def.EffectFactory(
            ChooseModes(
                KayasGuileFactory.ModeEachOpponentSacrifices,
                KayasGuileFactory.ModeExileGraveyards,
                KayasGuileFactory.ModeGainLife))
            .Should().HaveCount(KayasGuileFactory.PickCount);
    }

    // -----------------------------------------------------------------------
    // Entwine (CR 702.41 / 700.2e) — when the caller supplies all four mode
    // indices (the entwine path), all four modes resolve.
    // -----------------------------------------------------------------------

    [Fact]
    public void KayasGuile_Entwine_AllFourModesResolve()
    {
        var def = KayasGuileFactory.BuildDefinition(
            _alice, new[] { _alice, _bob }, agent: null, entwined: true);

        var effects = def.EffectFactory(
            ChooseModes(
                KayasGuileFactory.ModeEachOpponentSacrifices,
                KayasGuileFactory.ModeExileGraveyards,
                KayasGuileFactory.ModeCreateSpirit,
                KayasGuileFactory.ModeGainLife));

        effects.Should().HaveCount(KayasGuileFactory.TotalModes,
            because: "entwine (CR 702.41) lets all four modes resolve when paid");
    }
}
