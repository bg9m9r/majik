using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// End-to-end tests for Sea's Claim — Enchantment — Aura {U}.
///
///   "Enchant land.
///    Enchanted land is an Island."
///
/// Identical to Spreading Seas' retype machinery; no ETB draw trigger.
/// </summary>
public class SeasClaimTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public SeasClaimTests()
    {
        _zones = new ZoneService(_bus);
    }

    [Fact]
    public void SeasClaim_IsAura_AtCost_U()
    {
        var sc = SeasClaimFactory.Create(_alice);

        sc.Name.Should().Be("Sea's Claim");
        sc.HasType(CardType.Enchantment).Should().BeTrue();
        sc.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        sc.IsAura.Should().BeTrue();
        sc.ManaCost.Should().Be("{U}");
        sc.Owner.Should().BeSameAs(_alice);
        sc.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SeasClaim()
    {
        var sc = NamedCardFactory.Create("Sea's Claim", _alice);

        sc.Should().BeOfType<Enchantment>();
        sc.Name.Should().Be("Sea's Claim");
        sc.ManaCost.Should().Be("{U}");
        sc.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    /// <summary>
    /// Headline lifecycle test: Mountain (basic, printed {R}) becomes an
    /// Island under Sea's Claim — taps for {U}.
    /// </summary>
    [Fact]
    public void Attached_To_Mountain_RetypeIsland_TapsForBlue()
    {
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        OracleManaBinder.BindBasicLandMana(mountain, _alice);
        _zones.MoveCard(mountain, ZoneType.Library, ZoneType.Battlefield, _alice);

        // Baseline: Mountain taps for {R}.
        var baseline = EffectiveManaAbilities.For(mountain, _effects, _alice);
        baseline.Should().ContainSingle().Which.ManaGenerated.Red.Should().Be(1);

        var sc = SeasClaimFactory.Create(_alice, _effects, _bus);
        sc.AttachTo(mountain);
        _zones.MoveCard(sc, ZoneType.Library, ZoneType.Battlefield, _alice);

        var attached = EffectiveManaAbilities.For(mountain, _effects, _alice);
        attached.Should().HaveCount(1, "CR 305.6 strips printed {R} and adds {U}");
        attached[0].ManaGenerated.Blue.Should().Be(1);
        attached[0].ManaGenerated.Red.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Cast-time targeting + auto-attach on resolution (CR 303.4f / 601.2c)
    // -----------------------------------------------------------------------

    /// <summary>
    /// End-to-end cast flow: agent picks the target Mountain at cast time;
    /// on resolution, the aura attaches to the Mountain BEFORE the engine
    /// moves it to the battlefield. The Layer 4 retype then activates as
    /// the aura ETBs, so the Mountain taps for {U}.
    /// </summary>
    [Fact]
    public async Task SeasClaim_CastFlow_AutoAttaches()
    {
        var mountain = new Land(
            "Mountain",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        OracleManaBinder.BindBasicLandMana(mountain, _alice);
        _zones.MoveCard(mountain, ZoneType.Library, ZoneType.Battlefield, _alice);

        var sc = SeasClaimFactory.Create(_alice, _effects, _bus);
        _alice.Zones.Library.RemoveCard(sc);
        _alice.Zones.Hand.AddCard(sc);
        sc.SetZone(ZoneType.Hand);

        var stack = new Majik.Core.Stack.Stack(_bus);
        var castFlow = new SpellCastFlow(stack, _zones, _bus);
        var resolver = new StackResolver(_bus, _zones);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { mountain });
        agent.QueueMana(ManaPayment.Empty);

        var def = SeasClaimFactory.BuildSpellDefinition(
            sc, _alice.Zones.Battlefield.GetCards().OfType<Permanent>());
        var ctx = new GameContext(_alice, new[] { _alice }, _alice, 1,
            PhaseStateType.Main, stack);

        await castFlow.CastAsync(_alice, sc, def, agent, ctx);
        resolver.ResolveTop(stack);

        sc.Zone.Should().Be(ZoneType.Battlefield);
        sc.AttachedTo.Should().BeSameAs(mountain,
            "CR 303.4f — Aura enters the battlefield attached to its chosen target");
        mountain.Attachments.Should().Contain(sc);

        var attached = EffectiveManaAbilities.For(mountain, _effects, _alice);
        attached.Should().HaveCount(1);
        attached[0].ManaGenerated.Blue.Should().Be(1);
        attached[0].ManaGenerated.Red.Should().Be(0);
    }
}
