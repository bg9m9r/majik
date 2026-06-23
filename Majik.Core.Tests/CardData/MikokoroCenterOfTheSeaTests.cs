using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MikokoroCenterOfTheSeaFactory"/> (Champions of
/// Kamigawa).
///
/// Mikokoro, Center of the Sea — Legendary Land.
///   "{T}: Add {C}.
///    {2}, {T}: Each player draws a card."
///
/// Covers ONLY the card's unique behaviour (its group-draw ability) plus a
/// single identity assert for the Legendary supertype. Dispatch + general
/// well-formedness are covered for every implemented card by
/// CardFactoryContractTests, so they are NOT re-asserted here.
///
/// - Identity (Legendary Land).
/// - One {C} mana ability (from the embedded JSON).
/// - One non-mana <see cref="ActivatedAbility"/> with cost {2} + tap, no targets.
/// - Single-arg path: only the controller draws.
/// - allPlayers path: every player draws one card (CR 121.1 / CR 101.4 APNAP).
/// - Empty library flags the SBA loss (CR 704.5b).
/// </summary>
[Trait("Color", "C")]
public class MikokoroCenterOfTheSeaTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void SeedLibrary(Player p, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var c = new Card($"Filler-{p.Name}-{i}", "{0}");
            c.SetOwner(p);
            p.Zones.Library.AddCard(c);
        }
    }

    // -----------------------------------------------------------------------
    // Identity (Legendary supertype is the only non-vanilla identity bit)
    // -----------------------------------------------------------------------

    [Fact]
    public void MikokoroCenterOfTheSea_Identity()
    {
        var land = MikokoroCenterOfTheSeaFactory.Create(_alice);

        land.Name.Should().Be("Mikokoro, Center of the Sea");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Mikokoro, Center of the Sea is a Legendary Land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MikokoroCenterOfTheSea_HasOneColorlessManaAbility()
    {
        var land = MikokoroCenterOfTheSeaFactory.Create(_alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(1, "single {T}: Add {C}");
        var ma = mas[0];
        ma.ManaGenerated.Generic.Should().Be(1);
        ma.ManaGenerated.TotalValue.Should().Be(1);
    }

    [Fact]
    public void MikokoroCenterOfTheSea_DrawAbility_HasCost2AndTapAndNoTargets()
    {
        var land = MikokoroCenterOfTheSeaFactory.Create(_alice);

        var draw = land.Abilities.OfType<ActivatedAbility>().Single();

        draw.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the draw cost has one ManaCostCost ({2})");
        draw.Costs.Count(c => c is not ManaCostCost).Should().BeGreaterThan(0,
            "the {T} tap component is an additional (non-mana) cost");
        draw.IsSorcerySpeed.Should().BeFalse(
            "the group-draw ability is instant-speed per oracle");
        draw.TargetRequests.Should().BeEmpty(
            "'each player draws a card' is a symmetric effect with no targets");
    }

    // -----------------------------------------------------------------------
    // {2}, {T}: Each player draws a card.
    // -----------------------------------------------------------------------

    [Fact]
    public void MikokoroCenterOfTheSea_Activated_SingleArg_ControllerDraws()
    {
        var land = MikokoroCenterOfTheSeaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SeedLibrary(_alice, 5);
        var handBefore = _alice.Zones.Hand.GetCards().Count();

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Count().Should().Be(4,
            "controller draws one card (lib 5 -> 4)");
        _alice.Zones.Hand.GetCards().Count().Should().Be(handBefore + 1,
            "the drawn card is now in hand");
    }

    [Fact]
    public void MikokoroCenterOfTheSea_Activated_AllPlayers_EachDrawsOne()
    {
        var land = MikokoroCenterOfTheSeaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        SeedLibrary(_alice, 5);
        SeedLibrary(_bob, 5);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        // "Each player draws" reads ctx.Game.AllPlayers at resolution — resolve
        // with a live GameContext over both players (CR 121.1 / CR 101.4 APNAP).
        ResolveWithGame(ability, _alice, _alice, _bob);

        _alice.Zones.Library.GetCards().Count().Should().Be(4);
        _bob.Zones.Library.GetCards().Count().Should().Be(4);
        _alice.Zones.Hand.GetCards().Count().Should().Be(1, "Alice drew one");
        _bob.Zones.Hand.GetCards().Count().Should().Be(1, "Bob drew one");
    }

    [Fact]
    public void MikokoroCenterOfTheSea_Activated_EmptyLibrary_FlagsSbaLoss()
    {
        // Empty library: the draw flags the SBA loss (CR 704.5b).
        var land = MikokoroCenterOfTheSeaFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // No library seeded.
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "drawing from an empty library flags the SBA loss (CR 704.5b)");
    }

    private static void ResolveWithGame(
        ActivatedAbility ability, Player controller, params Player[] players)
    {
        var game = new Majik.Core.Game.GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        ability.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }
}
