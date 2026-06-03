using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="MysticForgeFactory"/> (Modern Horizons, {4} Artifact).
///
///   "You may look at the top card of your library any time.
///    You may cast artifact spells and colorless spells from the top of your
///    library.
///    {T}, Pay 1 life: Exile the top card of your library."
///
/// Covers identity + dispatch, the description riders, the {T}, Pay 1 life
/// exile-top ability, and the battlefield-gated cast-artifact/colorless-from-top
/// grant (CR 601.3e) registered/revoked via the per-game
/// <see cref="ContinuousEffectsService"/>'s event bus.
/// </summary>
public class MysticForgeFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => LibraryTopPlayPermissions.Clear();

    private static (ZoneService zones, ContinuousEffectsService effects, EventBus bus) BuildEngine()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var effects = new ContinuousEffectsService(bus);
        return (zones, effects, bus);
    }

    private static void EnterBattlefield(ZoneService zones, Player owner, ICard card)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
        zones.MoveCardTo(card, ZoneType.Battlefield, controller: owner);
    }

    private T PutOnTopOfLibrary<T>(T card) where T : ICard
    {
        card.SetOwner(_alice);
        _alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);
        return card;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticForge_Identity_Artifact_At4()
    {
        var forge = MysticForgeFactory.Create(_alice);

        forge.Name.Should().Be("Mystic Forge");
        forge.ManaCost.Should().Be("{4}");
        forge.HasType(CardType.Artifact).Should().BeTrue();
        forge.Owner.Should().BeSameAs(_alice);
        forge.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MysticForge()
    {
        var card = NamedCardFactory.Create("Mystic Forge", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Mystic Forge");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void MysticForge_HasRiders_LookTop_CastFromTop_ExileAbility()
    {
        var forge = MysticForgeFactory.Create(_alice);

        var statics = forge.Abilities.OfType<StaticAbility>().Select(s => s.Description).ToList();
        statics.Should().Contain(MysticForgeFactory.LookTopDescription);
        statics.Should().Contain(MysticForgeFactory.CastFromTopDescription);

        var exile = forge.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        exile.Costs.OfType<PayLifeCost>().Should().ContainSingle(
            c => c.Amount == MysticForgeFactory.ExileLifeCost);
    }

    // -----------------------------------------------------------------------
    // Battlefield-gated cast-from-top grant (CR 601.3e)
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticForge_OnBattlefield_TopArtifact_IsCastable()
    {
        var (zones, effects, _) = BuildEngine();
        var forge = MysticForgeFactory.Create(_alice, effects);

        var artifact = PutOnTopOfLibrary(new Artifact("Ornithopter", "{0}"));

        // Before Mystic Forge is on the battlefield: no permission.
        LibraryTopPlayPermissions.MayCastTopCard(_alice, artifact).Should().BeFalse();

        EnterBattlefield(zones, _alice, forge);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, artifact).Should().BeTrue(
            "Mystic Forge grants 'cast artifact spells from the top of your library'");
        LibraryTopPlayPermissions.CastableSpellFromTop(_alice).Should().BeSameAs(artifact);
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeTrue(
            "Mystic Forge lets you look at the top card any time (CR 715.4)");
    }

    [Fact]
    public void MysticForge_OnBattlefield_TopColorlessNonArtifact_IsCastable()
    {
        var (zones, effects, _) = BuildEngine();
        var forge = MysticForgeFactory.Create(_alice, effects);

        // A {8} colorless creature (Eldrazi) — colorless but not an artifact.
        var eldrazi = PutOnTopOfLibrary(new Creature("Eldrazi", "{8}", 5, 5));

        EnterBattlefield(zones, _alice, forge);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, eldrazi).Should().BeTrue(
            "Mystic Forge also lets you cast colorless spells from the top");
    }

    [Fact]
    public void MysticForge_OnBattlefield_TopColoredNonArtifact_NotCastable()
    {
        var (zones, effects, _) = BuildEngine();
        var forge = MysticForgeFactory.Create(_alice, effects);

        var bolt = PutOnTopOfLibrary(new Instant("Lightning Bolt", "{R}"));

        EnterBattlefield(zones, _alice, forge);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, bolt).Should().BeFalse(
            "a red instant is neither an artifact nor colorless");
        LibraryTopPlayPermissions.CastableSpellFromTop(_alice).Should().BeNull();
    }

    [Fact]
    public void MysticForge_LeavesBattlefield_GrantRevoked()
    {
        var (zones, effects, _) = BuildEngine();
        var forge = MysticForgeFactory.Create(_alice, effects);

        var artifact = PutOnTopOfLibrary(new Artifact("Ornithopter", "{0}"));

        EnterBattlefield(zones, _alice, forge);
        LibraryTopPlayPermissions.MayCastTopCard(_alice, artifact).Should().BeTrue();

        zones.MoveCardTo(forge, ZoneType.Graveyard, controller: _alice);

        LibraryTopPlayPermissions.MayCastTopCard(_alice, artifact).Should().BeFalse(
            "the grant ends when Mystic Forge leaves the battlefield (CR 603.6e)");
        LibraryTopPlayPermissions.IsTopRevealed(_alice).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // {T}, Pay 1 life: Exile the top card of your library
    // -----------------------------------------------------------------------

    [Fact]
    public void MysticForge_ExileAbility_MovesTopCardToExile()
    {
        var forge = MysticForgeFactory.Create(_alice);
        forge.SetController(_alice);

        var top = PutOnTopOfLibrary(new Artifact("Ornithopter", "{0}"));

        var exile = forge.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in exile.Effects)
        {
            fx.Execute();
        }

        _alice.Zones.Library.GetCards().Should().NotContain(top);
        _alice.Zones.Exile.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Bot integration: the HeuristicBotAgent proposes casting a free artifact
    // from the top of the library when a Mystic Forge grant is active.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Bot_WithCastFromTopGrant_ProposesCastingTopArtifact()
    {
        // Mystic Forge's Artifacts grant is active for Alice.
        LibraryTopPlayPermissions.AddGrant(new object(), _alice, TopPlayFilter.Artifacts);

        // A {0} artifact (Ornithopter) on top — free to cast, so affordability
        // never gates the proposal.
        var ornithopter = PutOnTopOfLibrary(new Artifact("Ornithopter", "{0}"));

        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _alice }, _alice,
            1, StepStateType.PreCombatMain, new Majik.Core.Stack.Stack(),
            landPlayAvailable: false);

        var action = await bot.ChoosePriorityActionAsync(ctx);

        action.Should().BeOfType<PriorityAction.CastSpell>()
            .Which.Card.Should().BeSameAs(ornithopter,
                "the bot enumerates the castable-from-top artifact as a printed-cost bid");
    }

    [Fact]
    public async Task Bot_NoGrant_DoesNotProposeCastingTopArtifact()
    {
        // No grant — the top artifact is NOT a legal cast source.
        var ornithopter = PutOnTopOfLibrary(new Artifact("Ornithopter", "{0}"));

        var bot = new HeuristicBotAgent();
        var ctx = new GameContext(_alice, new[] { _alice, _alice }, _alice,
            1, StepStateType.PreCombatMain, new Majik.Core.Stack.Stack(),
            landPlayAvailable: false);

        var action = await bot.ChoosePriorityActionAsync(ctx);

        action.Should().Be(PriorityAction.Pass,
            "without a cast-from-top grant the library top is not castable");
    }
}
