using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GhostlyFlickerFactory"/>.
///
/// Oracle (verified against Scryfall):
///   "Exile two target artifacts, creatures, and/or lands you control, then
///    return those cards to the battlefield under your control."
///
/// Covers:
/// - Identity (Instant, {2}{U}, blue, owner / controller).
/// - NamedCardFactory dispatch.
/// - SpellDefinition shape — single 2..2 "artifacts, creatures, and/or lands
///   you control" request, Protection intent.
/// - Resolve: exiles both targets and immediately returns them under the
///   caster's control (CR 701.21 + CR 614).
/// - Resolve: opponent-controlled target fizzles at the resolution-time
///   legality check (CR 608.2b).
/// - Gatherer: scopes to the caster's artifact/creature/land permanents only.
/// </summary>
public class GhostlyFlickerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GhostlyFlicker_IsInstant_AtCost2U_Blue()
    {
        var c = GhostlyFlickerFactory.Create(_alice);

        c.Name.Should().Be("Ghostly Flicker");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(c).Should().Contain(ManaColor.Blue);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GhostlyFlicker_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ghostly Flicker", _alice);

        c.Should().BeOfType<Instant>();
        c.Name.Should().Be("Ghostly Flicker");
        c.ManaCost.Should().Be("{2}{U}");
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GhostlyFlicker_Definition_HasSingleTwoTargetControllerRequest()
    {
        var def = GhostlyFlickerFactory.BuildDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(2);
        tr.MaxTargets.Should().Be(2);
        tr.Description.Should().Contain("you control");
        tr.Intent.Should().Be(BotIntent.Protection);
    }

    [Fact]
    public void GhostlyFlicker_Gatherer_ScopesToCasterArtifactCreatureLand()
    {
        var myBear = NewControlled(_alice, new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        var myArtifact = NewControlled(_alice, new Artifact("Sol Ring", "{1}"));
        var myLand = NewControlled(_alice, new Land("Island"));
        var myEnchantment = NewControlled(_alice, new Enchantment("Rancor", "{G}"));
        var bobBear = NewControlled(_bob, new Creature("Goblin Guide", "{R}", 2, 2));

        var def = GhostlyFlickerFactory.BuildDefinition(_alice);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            Majik.Core.StateMachine.PhaseStateType.PreCombatMain,
            new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus()));

        var candidates = def.TargetRequests[0].CandidateGatherer!(ctx);

        candidates.Should().Contain(new object[] { myBear, myArtifact, myLand });
        candidates.Should().NotContain(myEnchantment,
            "an enchantment is not an artifact/creature/land target");
        candidates.Should().NotContain(bobBear,
            "'you control' excludes opponent-controlled permanents (CR 109.5)");
    }

    // -----------------------------------------------------------------------
    // Resolve — exile-then-return (both targets)
    // -----------------------------------------------------------------------

    [Fact]
    public void GhostlyFlicker_Resolve_ExilesThenReturnsBothTargets()
    {
        var bear = NewControlled(_alice, new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        var land = NewControlled(_alice, new Land("Island"));

        var def = GhostlyFlickerFactory.BuildDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bear, land } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "CR 614 — the exiled card returns in the same resolution");
        land.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(new ICard[] { bear, land });
        _alice.Zones.Exile.GetCards().Should().NotContain(new ICard[] { bear, land });
        bear.Controller.Should().BeSameAs(_alice, "returned 'under your control' (CR 614)");
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — independent illegal-target handling (CR 608.2b / .2c)
    // -----------------------------------------------------------------------

    [Fact]
    public void GhostlyFlicker_Resolve_OpponentControlledTarget_Fizzles_OtherStillBlinks()
    {
        var myBear = NewControlled(_alice, new Creature("Grizzly Bears", "{1}{G}", 2, 2));
        var bobBear = NewControlled(_bob, new Creature("Goblin Guide", "{R}", 2, 2));

        var def = GhostlyFlickerFactory.BuildDefinition(_alice);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { myBear, bobBear } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        // Bob's creature is not controlled by Alice → CR 608.2b no-op.
        bobBear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobBear);
        bobBear.Controller.Should().BeSameAs(_bob);

        // Alice's creature still blinks independently (CR 608.2c).
        myBear.Zone.Should().Be(ZoneType.Battlefield);
        myBear.Controller.Should().BeSameAs(_alice);
        _alice.Zones.Battlefield.GetCards().Should().Contain(myBear);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static T NewControlled<T>(Player owner, T permanent) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }
}
