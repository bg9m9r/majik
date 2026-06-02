using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Tokens;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FalkenrathPitFighterFactory"/> (Innistrad:
/// Crimson Vow, {R}).
///
/// Creature — Vampire Berserker 1/1. Oracle text:
///   "Trample
///    Haste
///    {R}, Sacrifice another creature or Blood token: Draw a card.
///    Activate only as a sorcery."
///
/// Covers:
///   - Identity (Vampire Berserker 1/1 at {R}).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trample + Haste keyword markers + <see cref="CombatAbilities"/> reads.
///   - One activated ability with the sorcery-speed rider.
///   - Cost pair: mana {R} + custom sacrifice cost.
///   - Activation resolves: sacrifice + draw.
///   - Sacrifice-creature alternative path.
///   - Sacrifice-Blood-token alternative path (preferred when both options
///     are available).
/// </summary>
[Trait("Color", "R")]
public class FalkenrathPitFighterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FalkenrathPitFighter_Identity()
    {
        var c = FalkenrathPitFighterFactory.Create(_alice);

        c.Name.Should().Be("Falkenrath Pit Fighter");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Vampire).Should().BeTrue();
        c.HasSubtype(CardSubtype.Berserker).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
    }
    [Fact]
    public void FalkenrathPitFighter_HasTrampleAndHaste()
    {
        var c = FalkenrathPitFighterFactory.Create(_alice);

        CombatAbilities.HasTrample(c).Should().BeTrue("printed Trample keyword");
        CombatAbilities.HasHaste(c).Should().BeTrue("printed Haste keyword");

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Trample");
        keywords.Should().Contain("Haste");
    }

    [Fact]
    public void FalkenrathPitFighter_HasOneSorcerySpeedActivatedAbility()
    {
        var c = FalkenrathPitFighterFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1, "the printed {R} + sac → draw activation");
        activated[0].IsSorcerySpeed.Should().BeTrue(
            "CR 117.1a — 'Activate only as a sorcery' rider on the printed ability");
        activated[0].Costs.OfType<ManaCostCost>().Should().HaveCount(1,
            "mana cost {R}");
        activated[0].Costs.OfType<SacrificeAnotherCreatureOrBloodTokenCost>()
            .Should().HaveCount(1,
                "sacrifice another creature or Blood token");
    }

    [Fact]
    public void Activation_SacrificesAnotherCreature_AndDrawsACard()
    {
        var fighter = FalkenrathPitFighterFactory.Create(_alice);
        SeatOnBattlefield(fighter);

        var fodder = new Creature("Fodder", "{1}", 1, 1);
        fodder.SetOwner(_alice);
        fodder.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        // Library top.
        var libTop = new Instant("Top", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        var ability = fighter.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = ability.Costs.OfType<SacrificeAnotherCreatureOrBloodTokenCost>().Single();
        sacCost.CanPay(_alice).Should().BeTrue("fodder is an eligible sacrifice target");
        sacCost.Pay(_alice);

        foreach (var effect in ability.Effects) effect.Execute();

        // Fodder went to graveyard.
        _alice.Zones.Graveyard.GetCards().Should().Contain(fodder);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(fodder);

        // Fighter is still on the battlefield (it sacrificed "another" creature).
        _alice.Zones.Battlefield.GetCards().Should().Contain(fighter);

        // Draw happened.
        _alice.Zones.Hand.GetCards().Should().Contain(libTop);
    }

    [Fact]
    public void Activation_PrefersSacrificingBloodToken_OverCreature()
    {
        var fighter = FalkenrathPitFighterFactory.Create(_alice);
        SeatOnBattlefield(fighter);

        // Add both a creature AND a Blood token. The deterministic v1
        // picker prefers Blood (cheaper to trade than a creature).
        var fodder = new Creature("Fodder", "{1}", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
        };
        _alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        var blood = TokenFactory.CreateBlood(_alice);

        // Library top so the draw fires.
        var libTop = new Instant("Top", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        var ability = fighter.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = ability.Costs.OfType<SacrificeAnotherCreatureOrBloodTokenCost>().Single();
        sacCost.Pay(_alice);

        foreach (var effect in ability.Effects) effect.Execute();

        // Deterministic v1 picker — Blood goes to graveyard, fodder stays.
        _alice.Zones.Battlefield.GetCards().Should().Contain(fodder,
            "v1 picker prefers Blood token over creature");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(blood);
        _alice.Zones.Graveyard.GetCards().Should().Contain(blood);

        // Draw happened.
        _alice.Zones.Hand.GetCards().Should().Contain(libTop);
    }

    [Fact]
    public void Activation_CanPay_FailsWithNoOtherCreatureOrBlood()
    {
        var fighter = FalkenrathPitFighterFactory.Create(_alice);
        SeatOnBattlefield(fighter);
        // Battlefield has ONLY the Fighter — no other creature, no Blood.

        var ability = fighter.Abilities.OfType<ActivatedAbility>().Single();
        var sacCost = ability.Costs.OfType<SacrificeAnotherCreatureOrBloodTokenCost>().Single();

        sacCost.CanPay(_alice).Should().BeFalse(
            "no eligible permanent (the Fighter itself is excluded by 'another')");
    }

    private void SeatOnBattlefield(Creature card)
    {
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
