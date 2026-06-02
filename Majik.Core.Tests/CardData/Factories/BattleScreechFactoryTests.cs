using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Battle Screech (Judgment, {2}{W}{W}, Sorcery).
///
/// Oracle text (verified against Scryfall):
///   "Create two 1/1 white Bird creature tokens with flying.
///    Flashback—Tap three untapped white creatures you control."
///
/// Coverage:
/// - Identity (name / type / mana cost) + <see cref="NamedCardFactory"/> dispatch.
/// - Resolve effect creates two 1/1 white Bird creature tokens with Flying.
/// - Flashback: mana-zero alt-cost (castable only from graveyard) + the
///   tap-three-white-creatures rider; OnResolved exiles the card (CR 702.34b).
/// - The tap rider only counts white, untapped, controlled creatures.
/// </summary>
public class BattleScreechFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Creature WhiteCreature(string name = "Soldier")
    {
        var c = new Creature(name, "W", 1, 1);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BattleScreech_Identity()
    {
        var c = BattleScreechFactory.Create(_alice);

        c.Name.Should().Be("Battle Screech");
        c.ManaCost.Should().Be("{2}{W}{W}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BattleScreech_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Battle Screech", _alice);

        c.Should().BeOfType<Sorcery>();
        c.Name.Should().Be("Battle Screech");
    }

    // -----------------------------------------------------------------------
    // Resolve effect
    // -----------------------------------------------------------------------

    [Fact]
    public void BattleScreech_Resolve_CreatesTwoWhiteBirdTokensWithFlying()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var effects = BattleScreechFactory.BuildResolveEffects(_alice, zones);
        effects.Should().ContainSingle("Battle Screech resolves with one effect (create two tokens)");

        foreach (var effect in effects) effect.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .ToList();
        tokens.Should().HaveCount(BattleScreechFactory.TokensCreated,
            "Battle Screech creates exactly two Bird tokens on resolution");

        tokens.Should().AllSatisfy(t =>
        {
            t.Name.Should().Be("Bird");
            t.BasePower.Should().Be(BattleScreechFactory.TokenPower);
            t.BaseToughness.Should().Be(BattleScreechFactory.TokenToughness);
            t.HasSubtype(CardSubtype.Bird).Should().BeTrue();
            t.IsToken.Should().BeTrue();
            t.Abilities.OfType<KeywordAbility>()
                .Should().Contain(k => k.Keyword == "Flying",
                    "each Bird token has flying");
            t.TokenColorsOverride.Should().NotBeNull();
            t.TokenColorsOverride!.Should().Contain(ManaColor.White,
                "tokens are white per the printed clause (CR 105 / 111.4)");
        });
    }

    // -----------------------------------------------------------------------
    // Flashback: mana-zero alt-cost + tap-three-white-creatures rider
    // -----------------------------------------------------------------------

    [Fact]
    public void FlashbackCost_IsManaZero_AndCastableOnlyFromGraveyard()
    {
        var bs = BattleScreechFactory.Create(_alice);

        var fb = BattleScreechFactory.BuildFlashbackCost();
        fb.AlternativeManaCost.Should().Be(ManaCost.Zero);
        fb.Description.Should().Contain("Flashback");

        // CR 702.34 — flashback is only castable from graveyard.
        bs.SetZone(ZoneType.Hand);
        fb.CanCastFor(bs, _alice).Should().BeFalse();

        _alice.Zones.Graveyard.AddCard(bs);
        bs.SetZone(ZoneType.Graveyard);
        fb.CanCastFor(bs, _alice).Should().BeTrue();
    }

    [Fact]
    public void FlashbackTapRider_TapsThreeWhiteCreatures_AndExilesOnResolve()
    {
        var bs = BattleScreechFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(bs);
        bs.SetZone(ZoneType.Graveyard);

        var w1 = WhiteCreature("Bird");
        var w2 = WhiteCreature("Bird");
        var w3 = WhiteCreature("Soldier");

        var additional = BattleScreechFactory.BuildFlashbackAdditionalCosts();
        additional.Should().HaveCount(1);
        var rider = additional[0];
        rider.Should().BeOfType<TapWhiteCreaturesAdditionalCost>();

        rider.CanPay(_alice).Should().BeTrue();
        rider.Pay(_alice).Should().BeTrue();

        // CR 118.12 — the tap word taps the chosen creatures as the cost.
        new[] { w1, w2, w3 }.Should().AllSatisfy(c => c.IsTapped.Should().BeTrue());

        // Post-resolve hook exiles Battle Screech (CR 702.34b).
        var fb = BattleScreechFactory.BuildFlashbackCost();
        fb.OnResolved(bs, _alice);

        bs.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(bs);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(bs);
    }

    [Fact]
    public void FlashbackTapRider_TooFewWhiteCreatures_CannotPay()
    {
        // Only two white creatures — three required, so the rider can't be paid.
        WhiteCreature();
        WhiteCreature();

        var rider = BattleScreechFactory.BuildFlashbackAdditionalCosts()[0];
        rider.CanPay(_alice).Should().BeFalse();
        rider.Pay(_alice).Should().BeFalse();
    }

    [Fact]
    public void FlashbackTapRider_IgnoresNonWhiteAndTappedCreatures()
    {
        // Two white, plus a green creature and an already-tapped white one —
        // only two payable white creatures remain, so it can't be paid.
        WhiteCreature();
        WhiteCreature();

        var green = new Creature("Bear", "1G", 2, 2);
        green.SetOwner(_alice);
        green.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(green);
        green.SetZone(ZoneType.Battlefield);

        var tapped = WhiteCreature();
        tapped.Tap();

        var rider = BattleScreechFactory.BuildFlashbackAdditionalCosts()[0];
        rider.CanPay(_alice).Should().BeFalse(
            "a green creature and a tapped white creature are not eligible");
    }
}
