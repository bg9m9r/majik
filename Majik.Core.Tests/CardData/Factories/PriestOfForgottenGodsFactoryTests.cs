using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
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
///   - "Any number of target players" — v1 affects every opponent. The affected
///     players are read from the LIVE game at resolution
///     (<c>ctx.Game.AllPlayers</c>) — NOT a captured opponents resolver — so the
///     each-opponent rider is correct on the production routed build too (the
///     resolver-null bug this fix addresses).
///   - Each affected player loses 2 life (CR 119.3) and then sacrifices a
///     creature of their choice (CR 701.16 — that player's agent picks via
///     <see cref="AgentRegistry"/> / an explicit override; deterministic
///     first-creature fallback otherwise).
///   - You add {B}{B} (CR 106.1) and draw a card (CR 120.1).
/// </summary>
[Trait("Color", "B")]
public class PriestOfForgottenGodsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>
    /// Drive the activated ability's resolution through the real async path with
    /// a live <see cref="GameContext"/> built from the supplied players, so the
    /// each-opponent rider reads opponents off <c>ctx.Game.AllPlayers</c> exactly
    /// as it does in a live match (mirrors <see cref="ActivatedAbility.ResolveAsync"/>).
    /// </summary>
    private static void ResolveWithGame(Creature priest, params Player[] players)
    {
        var controller = priest.Controller!;
        GameContext? game = players.Length == 0
            ? null
            : new GameContext(
                self: controller,
                allPlayers: players,
                activePlayer: controller,
                turnNumber: 1,
                currentPhase: null,
                stack: new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var ab = priest.Abilities.OfType<ActivatedAbility>().Single();
        ab.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }

    private static Creature SeedCreature(Player owner, string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

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
    // Resolution — each opponent loses 2 life (read from live context)
    // -----------------------------------------------------------------------

    [Fact]
    public void Priest_Effect_EachOtherPlayerLosesTwoLife()
    {
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        ResolveWithGame(priest, _alice, bob, carol);

        bob.LifeTotal.Should().Be(18, "each other player loses 2 life");
        carol.LifeTotal.Should().Be(18);
        _alice.LifeTotal.Should().Be(20, "the controller is not affected");
    }

    [Fact]
    public void Priest_Effect_YouAddTwoBlackMana()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        ResolveWithGame(priest, _alice);

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

        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        ResolveWithGame(priest, _alice);

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        _alice.Zones.Library.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void Priest_Effect_DrawMarksEmptyLibrary()
    {
        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        ResolveWithGame(priest, _alice);

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

        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        ResolveWithGame(priest, _alice, bob);

        bear.Zone.Should().Be(ZoneType.Graveyard, "the other player sacrifices a creature");
    }

    [Fact]
    public void Priest_Effect_OtherPlayerWithNoCreature_NoSacrifice()
    {
        var bob = new Player("Bob", 20);
        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        var act = () => ResolveWithGame(priest, _alice, bob);

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

        var priest = PriestOfForgottenGodsFactory.Create(_alice, sacrificeAgent: agent);

        ResolveWithGame(priest, _alice, bob);

        goyf.Zone.Should().Be(ZoneType.Graveyard, "the player chose to sacrifice Tarmogoyf");
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void Priest_Effect_NoOpWhenNoGameContext()
    {
        // Shape-only resolution (no live game context) — no opponents to read,
        // so the per-player rider is a safe no-op; the controller's add-mana +
        // draw still run.
        var priest = PriestOfForgottenGodsFactory.Create(_alice);

        var act = () => priest.Abilities.OfType<ActivatedAbility>().Single().Resolve();

        act.Should().NotThrow();
        _alice.ManaPool.Black.Should().Be(2, "the controller still adds {B}{B}");
    }

    // -----------------------------------------------------------------------
    // PROD-PATH: GameFacade routed build wires the each-opponent rider
    // -----------------------------------------------------------------------

    /// <summary>
    /// PROD-PATH regression guard (the resolver-null bug class). The production
    /// <c>GameFacade</c> routed build dispatches
    /// <see cref="NamedCardFactory.Create(string, Player, Majik.Core.Effects.ContinuousEffectsService?)"/>
    /// (the effects-aware overload), NOT the single-arg factory overload. The
    /// each-opponent rider must read opponents from the live resolution context
    /// so it is NOT inert on this path. This builds the card exactly as prod
    /// does and asserts each opponent loses 2 life + sacrifices a creature.
    /// </summary>
    [Fact]
    public void EffectsAwareDispatch_EachOpponentRiderRuns_OnProdPath()
    {
        var effects = new Majik.Core.Effects.ContinuousEffectsService();

        // Prod dispatch: GameFacade.BuildDeckCard → NamedCardFactory.Create(name, owner, effects).
        var built = NamedCardFactory.Create("Priest of Forgotten Gods", _alice, effects);
        built.Should().BeOfType<Creature>();
        var priest = (Creature)built;

        priest.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the prod effects-aware dispatch must route through the "
            + "Create(Player, ContinuousEffectsService) overload");

        var bob = new Player("Bob", 20);
        var bear = SeedCreature(bob, "Runeclaw Bear");

        ResolveWithGame(priest, _alice, bob);

        bob.LifeTotal.Should().Be(18,
            "the prod-built rider makes each opponent lose 2 life (not inert)");
        bear.Zone.Should().Be(ZoneType.Graveyard,
            "the prod-built rider makes each opponent sacrifice a creature");
    }
}
