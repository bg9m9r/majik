using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="KabiraCrossroadsFactory"/> (Zendikar mono-white
/// enters-tapped gain-life land). Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 2 life.
///    {T}: Add {W}."
///
/// Same oracle shape as <see cref="AkoumRefugeFactory"/> (a mana ability plus a
/// self-ETB "gain N life" trigger, CR 119) — only the mana colour ({W}) and the
/// life amount (2) differ. Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, nonbasic, owner/controller).
/// - One single-colour mana ability — {W} (CR 605.1a).
/// - One battlefield-active ETB triggered ability that gains 2 life.
/// - ETB effect: controller's life total rises by exactly 2 (CR 119.3).
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production load
/// path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>, not by this
/// named-card factory — same posture as the Refuge cycle.
/// </summary>
[Trait("Color", "W")]
public class KabiraCrossroadsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void KabiraCrossroads_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Kabira Crossroads", _alice);

        land.Name.Should().Be("Kabira Crossroads");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Kabira Crossroads is nonbasic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void KabiraCrossroads_HasManaAbility_ForWhite()
    {
        var land = (Land)NamedCardFactory.Create("Kabira Crossroads", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1
                && m.ManaGenerated.TotalValue == 1);
    }

    [Fact]
    public void KabiraCrossroads_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Kabira Crossroads", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void KabiraCrossroads_EtbEffect_GainsExactlyTwoLife()
    {
        // CR 119.3 — "you gain 2 life" raises the controller's life total by 2.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Kabira Crossroads", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(22, "Kabira Crossroads's ETB gains its controller 2 life");
    }
}
