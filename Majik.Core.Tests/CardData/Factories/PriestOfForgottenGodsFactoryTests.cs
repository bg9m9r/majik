using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PriestOfForgottenGodsFactory"/>.
///
/// Card: Priest of Forgotten Gods — Creature — Human Cleric, {1}{B} (1/2).
/// Oracle text (Scryfall, verified):
///   "{T}, Sacrifice two other creatures: Any number of target players each
///    lose 2 life and sacrifice a creature of their choice. You add {B}{B}
///    and draw a card."
///
/// CR 602.1 — activated ability ("cost: effect"). Cost = {T} (CR 602.5e,
/// <see cref="AdditionalCost.Tap"/>) + sacrifice two other creatures (two
/// <see cref="SacrificeAnotherCreatureCost"/>, CR 118.4). Resolution:
///   - "Any number of target players" — v1 follows the Yawgmoth precedent:
///     every other player is affected (targeting / "any number" prompt is
///     deferred to the same queue as Yawgmoth's each-other-player iteration).
///   - Each affected player loses 2 life (CR 119.3) and then sacrifices a
///     creature of their choice (CR 701.16 — agent-driven pick, mirrors
///     <see cref="DiabolicEdictFactory"/>; deterministic first-creature
///     fallback when no agent or an illegal pick).
///   - You add {B}{B} (CR 106.1) and draw a card (CR 120.1).
///
/// Mirrors <see cref="YawgmothFactory"/> (sac-engine Cleric, opponentsResolver,
/// "you draw a card") + <see cref="DiabolicEdictFactory"/> (per-player
/// "sacrifice a creature of their choice", agent-driven pick).
/// </summary>
public class PriestOfForgottenGodsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Priest_Identity()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        priest.Name.Should().Be("Priest of Forgotten Gods");
        priest.ManaCost.Should().Be("{1}{B}");
        priest.HasType(CardType.Creature).Should().BeTrue();
        priest.BasePower.Should().Be(1);
        priest.BaseToughness.Should().Be(2);
        priest.Owner.Should().BeSameAs(_alice);
        priest.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Priest_HasHumanClericSubtypes()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        priest.HasSubtype(CardSubtype.Human).Should().BeTrue();
        priest.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Priest()
    {
        var card = NamedCardFactory.Create("Priest of Forgotten Gods", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Priest of Forgotten Gods");
        card.ManaCost.Should().Be("{1}{B}");
    }

    // -----------------------------------------------------------------------
    // Costs — {T} + sacrifice two other creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void Priest_HasExactlyOneActivatedAbility()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        priest.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Priest_AbilityCosts_IncludeTapSelf()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice);
        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();

        ab.Costs.OfType<AdditionalCost>()
            .Should().Contain(
                c => c.Description.Contains("Tap"),
                "the cost must include {T}");
    }

    [Fact]
    public void Priest_AbilityCosts_IncludeSacrificeTwoOtherCreatures()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice);
        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();

        ab.Costs.OfType<SacrificeAnotherCreatureCost>()
            .Should().HaveCount(2, "the cost is 'Sacrifice two other creatures'");
    }

    // -----------------------------------------------------------------------
    // Resolution — lose 2 life, mana, draw
    // -----------------------------------------------------------------------

    [Fact]
    public void Priest_Effect_EachOtherPlayerLosesTwoLife()
    {
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        var priest = PriestOfForgottenGodsFactory.Create(
            _alice, () => new[] { _alice, bob, carol });

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        bob.LifeTotal.Should().Be(18, "each other player loses 2 life");
        carol.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20, "the controller is not affected");
    }

    [Fact]
    public void Priest_Effect_YouAddTwoBlackMana()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice, () => new[] { _alice });

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.ManaPool.Black.Should().Be(2, "the controller adds {B}{B}");
        _alice.ManaPool.Total.Should().Be(2, "only {B}{B} is added");
    }

    [Fact]
    public void Priest_Effect_ControllerDrawsACard()
    {
        var top = new Card("Dark Ritual", "{B}");
        top.SetOwner(_alice);
        top.SetController(_alice);
        _alice.Zones.Library.AddCard(top);

        var priest = PriestOfForgottenGodsFactory.Create(_alice, () => new[] { _alice });

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void Priest_Effect_DrawMarksEmptyLibrary()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice, () => new[] { _alice });

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 120.3: drawing from an empty library sets the flag");
    }

    // -----------------------------------------------------------------------
    // Resolution — per-player "sacrifice a creature of their choice"
    // -----------------------------------------------------------------------

    [Fact]
    public void Priest_Effect_EachOtherPlayerSacrificesACreature_DeterministicFallback()
    {
        var bob = new Player("Bob", 20);
        var bear = SeedCreature(bob, "Runeclaw Bear");

        var priest = PriestOfForgottenGodsFactory.Create(
            _alice, () => new[] { _alice, bob });

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        bear.Zone.Should().Be(ZoneType.Graveyard, "the other player sacrifices a creature");
    }

    [Fact]
    public void Priest_Effect_OtherPlayerWithNoCreature_NoSacrifice()
    {
        var bob = new Player("Bob", 20);
        var priest = PriestOfForgottenGodsFactory.Create(
            _alice, () => new[] { _alice, bob });

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => ab.Resolve();

        act.Should().NotThrow();
        bob.LifeTotal.Should().Be(18, "the life loss still happens");
        bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Priest_Effect_SacrificeIsAgentDriven()
    {
        var bob = new Player("Bob", 20);
        var bear = SeedCreature(bob, "Runeclaw Bear");
        var goyf = SeedCreature(bob, "Tarmogoyf");

        var agent = new ScriptedAgent();
        agent.QueueFromBattlefield(c => c.First(x => x.Name == "Tarmogoyf"));

        var priest = PriestOfForgottenGodsFactory.Create(
            _alice, () => new[] { _alice, bob }, sacrificeAgent: agent);

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        ab.Resolve();

        goyf.Zone.Should().Be(ZoneType.Graveyard, "the player chose to sacrifice Tarmogoyf");
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Priest_Effect_NoOpWhenNoOpponentsResolver()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice); // null resolver

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => ab.Resolve();

        // Mana + draw still happen for the controller; per-player effects are
        // skipped (no player list) — must not throw.
        act.Should().NotThrow();
        _alice.ManaPool.Black.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
