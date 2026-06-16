using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.SpellTemplates;
using Majik.Core.CardData.SpellTemplates.Templates.Destroy;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Murderous Compulsion (Innistrad, {1}{B}, Sorcery).
///
/// Oracle text: "Destroy target tapped creature." / "Madness {1}{B}".
///
/// The pay-down adds the declarative <c>tapped_creature</c> target filter
/// (<see cref="TargetFilters"/>, any-controller sibling of
/// <c>tapped_creature_opponent_controls</c>) and a dedicated
/// <see cref="DestroyTappedCreatureTemplate"/> so the PROD cast path
/// (oracle-text binder) honours the printed "tapped" restriction — the generic
/// <see cref="DestroyCreatureTemplate"/> would otherwise drop it (CR 109.5 /
/// CR 701.7 / CR 608.2b). Madness is engine-intrinsic (MadnessCatalog) — not
/// re-tested here.
///
/// Covers:
///   - The new <c>tapped_creature</c> filter: gatherer offers only TAPPED
///     battlefield creatures (any controller); Matches gates tapped + creature.
///   - Card identity (Sorcery, {1}{B}) + NamedCardFactory dispatch.
///   - Prod bind: "Destroy target tapped creature." → DestroyTappedCreature
///     template (higher priority than DestroyCreature).
///   - Resolution: destroys a tapped creature (CR 701.7).
///   - Resolution: untapped target fizzles (CR 608.2b).
/// </summary>
public class MurderousCompulsionTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // tapped_creature filter (the actual deferral pay-down)
    // -----------------------------------------------------------------------

    [Fact]
    public void TappedCreatureFilter_Gatherer_OffersOnlyTappedCreatures_AnyController()
    {
        var aliceTapped = OnBattlefield(new Creature("Alice Tapped", "{B}", 2, 2), _alice);
        aliceTapped.Tap();
        var bobTapped = OnBattlefield(new Creature("Bob Tapped", "{G}", 1, 1), _bob);
        bobTapped.Tap();
        var bobUntapped = OnBattlefield(new Creature("Bob Untapped", "{R}", 3, 3), _bob);

        var request = TargetFilters.ToTargetRequest("tapped_creature", "destroy");
        var stack = new Majik.Core.Stack.Stack(_bus);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, stack);

        var candidates = request.CandidateGatherer!(ctx);

        // Any controller — both tapped creatures are offered, the untapped one is not.
        candidates.Should().Contain(aliceTapped);
        candidates.Should().Contain(bobTapped);
        candidates.Should().NotContain(bobUntapped);
    }

    [Fact]
    public void TappedCreatureFilter_Matches_GatesTappedCreatureOnBattlefield()
    {
        var tapped = OnBattlefield(new Creature("Tapped", "{B}", 1, 1), _bob);
        tapped.Tap();
        var untapped = OnBattlefield(new Creature("Untapped", "{B}", 1, 1), _bob);

        TargetFilters.Matches("tapped_creature", tapped).Should().BeTrue();
        TargetFilters.Matches("tapped_creature", untapped).Should()
            .BeFalse("an untapped creature is not a legal 'tapped creature' target");
        TargetFilters.Matches("tapped_creature", new object()).Should().BeFalse();
        TargetFilters.Matches("tapped_creature", null).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MurderousCompulsion_IsSorcery_AtCost1B()
    {
        var card = MurderousCompulsionFactory.Create(_alice);

        card.Name.Should().Be("Murderous Compulsion");
        card.ManaCost.Should().Be("{1}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MurderousCompulsion()
    {
        var card = NamedCardFactory.Create("Murderous Compulsion", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Murderous Compulsion");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}");
    }

    // -----------------------------------------------------------------------
    // Prod cast path — oracle-text bind selects the tapped template
    // -----------------------------------------------------------------------

    [Fact]
    public void DestroyTappedCreatureTemplate_OutranksGenericDestroyCreature()
    {
        new DestroyTappedCreatureTemplate().Priority
            .Should().BeGreaterThan(new DestroyCreatureTemplate().Priority,
                "the tapped-filtered bind must win over the generic destroy-creature bind");
    }

    [Fact]
    public void ProdBind_DestroyTargetTappedCreature_BindsThroughTappedTemplate()
    {
        // The full oracle-text binder picks the highest-priority matching
        // template. With the tapped template registered above DestroyCreature,
        // "Destroy target tapped creature." resolves to the tapped-filtered def.
        var entity = new CardEntity
        {
            Name = "Murderous Compulsion",
            OracleText = "Destroy target tapped creature.",
        };

        var def = OracleSpellBinder.Bind(entity, _alice, o => o, stack: null);

        def.Should().NotBeNull();
        def!.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Contain("tapped creature");
        def.TargetRequests[0].CandidateGatherer.Should().NotBeNull(
            "the tapped-filtered request carries a live candidate gatherer");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void MurderousCompulsion_DestroysTappedCreature()
    {
        var goblin = OnBattlefield(new Creature("Goblin Guide", "{R}", 2, 2), _bob);
        goblin.Tap();

        ResolveOn(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Murderous Compulsion destroys the tapped target (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
    }

    [Fact]
    public void MurderousCompulsion_UntappedTarget_Fizzles()
    {
        // A creature that was tapped at announcement but untapped before
        // resolution (CR 608.2b — illegal target at resolution → no-op).
        var bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        // Not tapped — the resolution re-check via TargetFilters.Matches fails.

        ResolveOn(bear);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "an untapped creature is not a legal 'tapped creature' target (CR 608.2b)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void ResolveOn(object targetToken)
    {
        var def = MurderousCompulsionFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }
}
