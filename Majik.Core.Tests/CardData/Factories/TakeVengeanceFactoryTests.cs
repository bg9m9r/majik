using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TakeVengeanceFactory"/>
/// (Amonkhet, {1}{W} Sorcery).
///
/// Oracle text: "Destroy target tapped creature."
///
/// Covers:
///   - Card identity ({1}{W} Sorcery, white, mana value 2, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch returns a <see cref="Sorcery"/>.
///   - SpellDefinition shape: single 1..1 "target tapped creature"
///     <see cref="TargetRequest"/> with <see cref="BotIntent.Removal"/>.
///   - CandidateGatherer returns tapped creatures only — an untapped creature
///     is NOT included.
///   - Resolve: destroys a tapped target (moves to graveyard, CR 701.7).
///   - Resolve: target that untapped after targeting → no-op (CR 608.2b).
///   - Resolve: target that left the battlefield before resolution → no-op
///     (CR 608.2b).
/// </summary>
public class TakeVengeanceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void TakeVengeance_IsSorcery_AtCost1W()
    {
        var card = TakeVengeanceFactory.Create(_alice);

        card.Name.Should().Be("Take Vengeance");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TakeVengeance_IsWhite()
    {
        var card = TakeVengeanceFactory.Create(_alice);

        CardColors.GetColors(card).Should().Contain(ManaColor.White,
            "Take Vengeance has {W} in its mana cost (CR 105)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TakeVengeance()
    {
        var card = NamedCardFactory.Create("Take Vengeance", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Take Vengeance");
        card.ManaCost.Should().Be("{1}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void TakeVengeance_Definition_HasSingleTappedCreatureRequest()
    {
        var def = TakeVengeanceFactory.BuildDefinition(targetResolver: o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.Description.Should().Be("target tapped creature");
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // CandidateGatherer — tapped vs untapped
    // -----------------------------------------------------------------------

    [Fact]
    public void CandidateGatherer_ReturnsTappedCreature_ExcludesUntappedCreature()
    {
        var tappedBear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");
        tappedBear.Tap();

        var untappedBear = NewControlledCreature(_bob, "Runeclaw Bear", "{1}{G}");
        // untappedBear is untapped by default.

        var ctx = MakeContext();
        var def = TakeVengeanceFactory.BuildDefinition(targetResolver: o => o);
        var tr  = def.TargetRequests[0];

        var candidates = tr.ResolveCandidates(ctx);

        candidates.Should().Contain(tappedBear,
            "a tapped creature is a legal target for Take Vengeance");
        candidates.Should().NotContain(untappedBear,
            "an untapped creature is not a legal target (CR 115.5 — only tapped creatures)");
    }

    [Fact]
    public void CandidateGatherer_ReturnsNoCandidates_WhenNoCreatureIsTapped()
    {
        var untappedBear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");
        // untapped — should not appear.

        var ctx = MakeContext();
        var def = TakeVengeanceFactory.BuildDefinition(targetResolver: o => o);
        var tr  = def.TargetRequests[0];

        var candidates = tr.ResolveCandidates(ctx);

        candidates.Should().NotContain(untappedBear);
    }

    // -----------------------------------------------------------------------
    // Resolution — destroys a tapped target
    // -----------------------------------------------------------------------

    [Fact]
    public void TakeVengeance_Resolve_DestroysTappedCreature()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");
        bear.Tap();

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "Take Vengeance destroys a tapped creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op when target untapped at resolution (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TakeVengeance_Resolve_TargetUntappedBeforeResolution_NoOp()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");
        bear.Tap(); // Targeted while tapped...

        // Simulate something untapping it before the spell resolves.
        bear.Untap(); // ...but untapped by resolution time.

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "CR 608.2b — target untapped since targeting; no longer a legal " +
            "target at resolution → spell does nothing");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bear);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(bear);
    }

    // -----------------------------------------------------------------------
    // Resolution — no-op when target left the battlefield (CR 608.2b)
    // -----------------------------------------------------------------------

    [Fact]
    public void TakeVengeance_Resolve_TargetLeftBattlefield_NoOp()
    {
        var bear = NewControlledCreature(_bob, "Grizzly Bears", "{1}{G}");
        bear.Tap();

        // Simulate target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(bear);
        _bob.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        Resolve(bear);

        bear.Zone.Should().Be(ZoneType.Graveyard,
            "CR 608.2b — target left the battlefield before resolution → no-op");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void Resolve(Creature target)
    {
        var def = TakeVengeanceFactory.BuildDefinition(targetResolver: o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new IReadOnlyList<object>[] { new object[] { target } },
            Mana:      ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    private GameContext MakeContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));
}
