using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="KhalniGardenFactory"/> (Worldwake).
///
/// Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    When this land enters, create a 0/1 green Plant creature token.
///    {T}: Add {G}."
///
/// Scryfall type line: Land (no basic supertype, no subtypes).
///
/// Mirrors the suggested analogues
/// <see cref="DenOfTheBugbearFactory"/> / <see cref="CastleArdenvaleFactory"/>:
/// enters-tapped land (here unconditional, CR 614.1c) + a {T}: Add {G} mana
/// ability + an ETB triggered ability minting a token via TokenFactory.
///
/// Covers:
/// - Identity: Land type, name, non-basic, non-legendary, owner/controller.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Khalni Garden".
/// - Two abilities: one <see cref="ManaAbility"/> ({T}: Add {G}) + one ETB
///   <see cref="TriggeredAbility"/>.
/// - Mana ability: {T} produces {G}; CanActivate false when tapped.
/// - Unconditional ETB-tapped replacement (CR 614.1c) when a ReplacementBus
///   is wired.
/// - ETB trigger resolve: one 0/1 green Plant creature token enters.
/// </summary>
public class KhalniGardenFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land PlaceOnBattlefield()
    {
        var land = KhalniGardenFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return land;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_NamedKhalniGarden()
    {
        var land = KhalniGardenFactory.Create(_alice);
        land.Name.Should().Be("Khalni Garden");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary()
    {
        var land = KhalniGardenFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Khalni Garden is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Khalni Garden is not legendary");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_KhalniGarden()
    {
        var card = NamedCardFactory.Create("Khalni Garden", _alice);
        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Khalni Garden");

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {G} mana ability is wired");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB Plant-token trigger is attached");
    }

    [Fact]
    public void Create_HasExactlyTwoAbilities_OneManaOneTriggered()
    {
        var land = KhalniGardenFactory.Create(_alice);
        land.Abilities.Should().HaveCount(2,
            "one {T}: Add {G} mana ability + one ETB triggered ability");
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        land.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {G}
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaAbility_Activate_ProducesOneGreen()
    {
        var land = PlaceOnBattlefield();
        var mana = (IManaAbility)land.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
        var produced = mana.Activate();

        produced.Green.Should().Be(1, "the mana ability produces {G}");
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void ManaAbility_CanActivate_FalseWhenTapped()
    {
        var land = PlaceOnBattlefield();
        land.Tap();
        var mana = (IManaAbility)land.Abilities.OfType<ManaAbility>().Single();
        mana.CanActivate().Should().BeFalse("already tapped — {T} cost cannot be paid");
    }

    // -----------------------------------------------------------------------
    // Unconditional ETB-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EntersTapped_Always_WhenBusWired()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = KhalniGardenFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Khalni Garden enters tapped unconditionally (CR 614.1c)");
    }

    [Fact]
    public void SingleArgDispatch_DoesNotRegisterReplacement()
    {
        var alice = new Player("Alice", 20);
        var card = NamedCardFactory.Create("Khalni Garden", alice);
        card.Should().BeOfType<Land>();
        ((Land)card).Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        ((Land)card).Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — creates a 0/1 green Plant token (CR 111 / 111.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_CreatesOneZeroOneGreenPlantToken()
    {
        var land = PlaceOnBattlefield();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty(
            "no creatures before the ETB trigger resolves");

        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1, "one token is created");
        var token = tokens.Single();
        token.Name.Should().Be("Plant");
        token.BasePower.Should().Be(0);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Plant).Should().BeTrue("the token is a Plant");
        CardColors.GetColors(token).Should().BeEquivalentTo(new[] { ManaColor.Green },
            "the token is green (CR 111.4)");
    }
}
