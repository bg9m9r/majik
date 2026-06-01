using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="HauntedRidgeFactory"/> — Innistrad: Midnight
/// Hunt B/R "slow land".
///
/// Oracle (verified against Scryfall):
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {B} or {R}."
///
/// Covers card identity, the two mana abilities ({B} + {R}), and that no
/// triggered or non-mana activated abilities ship (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production — see <see cref="ConditionalEntersTappedBinder"/>). A final
/// test wires the actual Haunted Ridge oracle text through that binder to
/// lock the slow-land "two or more other lands" ETB behaviour (CR 614.1c).
/// </summary>
public class HauntedRidgeTests
{
    private const string Oracle =
        "This land enters tapped unless you control two or more other lands.\n{T}: Add {B} or {R}.";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HauntedRidge_IsLand()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void HauntedRidge_NameIsCorrect()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.Name.Should().Be("Haunted Ridge");
    }

    [Fact]
    public void HauntedRidge_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HauntedRidge_IsNotLegendary()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void HauntedRidge_HasTwoManaAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void HauntedRidge_HasBlackManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void HauntedRidge_HasRedManaAbility()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void HauntedRidge_HasNoTriggeredAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-two-or-more-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void HauntedRidge_HasNoActivatedAbilities()
    {
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void HauntedRidge_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Haunted Ridge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Haunted Ridge");
    }

    // -----------------------------------------------------------------------
    // Slow-land ETB-tapped predicate (CR 614.1c) — production binder layer.
    // "two or more other lands" => enters untapped iff controller has >= 2
    // OTHER lands (self excluded).
    // -----------------------------------------------------------------------

    [Fact]
    public void HauntedRidge_BinderRegistersTwoOrMoreOtherLandsReplacement()
    {
        var bus = new ReplacementBus();
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);
        var entity = new CardEntity
        {
            Name = "Haunted Ridge",
            OracleText = Oracle,
            TypeLine = "Land",
        };

        ConditionalEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue(
            "Haunted Ridge's oracle matches the 'two or more other lands' slow-land form");
    }

    [Fact]
    public void HauntedRidge_EntersTapped_WithFewerThanTwoOtherLands()
    {
        var bus = new ReplacementBus();
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);
        var entity = new CardEntity { Name = "Haunted Ridge", OracleText = Oracle, TypeLine = "Land" };
        ConditionalEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue();

        // Only one other land on the battlefield.
        var swamp = (Land)NamedCardFactory.Create("Swamp", _alice);
        _alice.Zones.Battlefield.AddCard(swamp);
        swamp.SetZone(ZoneType.Battlefield);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Haunted Ridge enters tapped when the controller has fewer than two other lands");
    }

    [Fact]
    public void HauntedRidge_EntersUntapped_WithTwoOrMoreOtherLands()
    {
        var bus = new ReplacementBus();
        var land = (Land)NamedCardFactory.Create("Haunted Ridge", _alice);
        var entity = new CardEntity { Name = "Haunted Ridge", OracleText = Oracle, TypeLine = "Land" };
        ConditionalEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue();

        // Two other lands on the battlefield.
        foreach (var name in new[] { "Swamp", "Mountain" })
        {
            var basic = (Land)NamedCardFactory.Create(name, _alice);
            _alice.Zones.Battlefield.AddCard(basic);
            basic.SetZone(ZoneType.Battlefield);
        }

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Haunted Ridge enters untapped when the controller has two or more other lands");
    }
}
