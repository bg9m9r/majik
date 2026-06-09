using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Behavioural verification of the LAND triggered abilities + off-card-effect
/// replacements synthesised from oracle text by the binder chain — the only
/// prod binding path for lands (never routed through a [CardName] factory).
/// Real oracle text from <see cref="EmbeddedCardRepository"/>.
/// </summary>
public class BoundLandTriggerBinderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EmbeddedCardRepository _repo = new();
    private readonly IReadOnlyList<Player> _players;

    public BoundLandTriggerBinderTests()
    {
        _players = new[] { _alice, _bob };
    }

    private Land MakeLandShell(string name)
    {
        var entity = _repo.GetByName(name)!;
        var parsed = TypeLineParser.Parse(entity.TypeLine);
        var land = new Land(name, parsed.Supertypes, parsed.Subtypes);
        land.SetOwner(_alice);
        land.SetController(_alice);
        return land;
    }

    private List<TriggeredAbility> BindTriggers(Land land) =>
        OracleTriggeredAbilityBinder.Bind(land, _repo.GetByName(land.Name)!, _alice, _players).ToList();

    // -----------------------------------------------------------------------
    // Glimmervoid — end-step "if you control no artifacts, sacrifice this land".
    // -----------------------------------------------------------------------

    [Fact]
    public void Glimmervoid_BindsEndStepConditionalSacTrigger()
    {
        var land = MakeLandShell("Glimmervoid");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var triggers = BindTriggers(land);
        var trig = triggers.Should().ContainSingle().Subject;

        // No artifacts → intervening-if true → resolving sacrifices the land.
        trig.CanBePutOnStack().Should().BeTrue("controller has no artifacts");
        foreach (var e in trig.Effects) e.Execute();
        land.Zone.Should().Be(ZoneType.Graveyard, "sacrificed at end step (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(land);
    }

    [Fact]
    public void Glimmervoid_DoesNotSacrifice_WhenControllingAnArtifact()
    {
        var land = MakeLandShell("Glimmervoid");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Ornithopter", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        var trig = BindTriggers(land).Single();
        trig.CanBePutOnStack().Should().BeFalse("controller has an artifact — intervening if false");

        // Even if resolved, the CR 603.4 re-check no-ops.
        foreach (var e in trig.Effects) e.Execute();
        land.Zone.Should().Be(ZoneType.Battlefield, "not sacrificed while an artifact is controlled");
    }

    // -----------------------------------------------------------------------
    // Abraded Bluffs — ETB "deals 1 damage to target opponent".
    // -----------------------------------------------------------------------

    [Fact]
    public void AbradedBluffs_BindsEtbDamageToOpponentTrigger()
    {
        var land = MakeLandShell("Abraded Bluffs");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trig = BindTriggers(land).Should().ContainSingle().Subject;
        // Retrofitted to a real "target opponent" TargetRequest (agent-chosen).
        trig.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("target opponent");

        // No agent / no chosen target → first-opponent fallback (the
        // non-interactive posture): Bob takes the damage.
        foreach (var e in trig.Effects) e.Execute();
        _bob.LifeTotal.Should().Be(19, "the opponent takes 1 damage on ETB (CR 119.1d)");
        _alice.LifeTotal.Should().Be(20, "the controller is unaffected");
    }

    // -----------------------------------------------------------------------
    // Witch's Cottage — "enters untapped" recur from graveyard.
    // -----------------------------------------------------------------------

    [Fact]
    public void WitchsCottage_BindsEntersUntappedRecurTrigger()
    {
        var land = MakeLandShell("Witch's Cottage");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield); // untapped

        // A creature card in the graveyard to recur.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var trig = BindTriggers(land).Should().ContainSingle().Subject;

        foreach (var e in trig.Effects) e.Execute();
        _alice.Zones.Library.GetCards().FirstOrDefault().Should().BeSameAs(bear,
            "the recurred creature is put on top of the library (CR 701.20)");
        bear.Zone.Should().Be(ZoneType.Library);
    }

    // -----------------------------------------------------------------------
    // Vesuva — off-card EntersAsCopyReplacement (land filter, tapped, no-legend).
    // -----------------------------------------------------------------------

    [Fact]
    public void Vesuva_BinderRegistersEntersAsCopyReplacement_CopiesLand_EntersTapped()
    {
        var bus = new ReplacementBus();
        var effects = new ContinuousEffectsService();

        // A dual land on the battlefield to copy.
        var dual = new Land("Volcanic Island", supertypes: null,
            subtypes: new[] { CardSubtype.Island, CardSubtype.Mountain });
        dual.SetOwner(_alice);
        dual.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(dual);
        dual.SetZone(ZoneType.Battlefield);

        var vesuva = MakeLandShell("Vesuva");
        EntersAsCopyBinder.Bind(vesuva, _repo.GetByName("Vesuva")!, bus, effects)
            .Should().BeTrue("the binder detects Vesuva's land-copy oracle text");

        vesuva.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(vesuva);

        var zones = new ZoneService(eventBus: null, replacements: bus);
        zones.MoveCard(vesuva, ZoneType.Hand, ZoneType.Battlefield, _alice);

        // CR 707.2 — copied the dual land's subtypes.
        var chars = effects.Compute(vesuva);
        chars.Subtypes.Should().Contain(CardSubtype.Island);
        chars.Subtypes.Should().Contain(CardSubtype.Mountain);
        // CR 706.2 — enters tapped.
        vesuva.IsTapped.Should().BeTrue("Vesuva enters tapped as a copy");
    }

    // -----------------------------------------------------------------------
    // Valakut — "Whenever a Mountain you control enters, if you control ≥5
    // other Mountains, you may have this land deal 3 damage to any target."
    // -----------------------------------------------------------------------

    [Fact]
    public void Valakut_BindsMountainEntersDealDamageTrigger_WithAnyTargetRequest()
    {
        var land = MakeLandShell("Valakut, the Molten Pinnacle");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var trig = BindTriggers(land).Should().ContainSingle().Subject;
        trig.TargetRequests.Should().ContainSingle()
            .Which.Description.Should().Be("any target");
    }

    [Fact]
    public void Valakut_DealsDamageToTheChosenTarget_WhenControllerHasFiveOtherMountains()
    {
        var land = MakeLandShell("Valakut, the Molten Pinnacle");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Five OTHER Mountains under the controller satisfy the intervening-if.
        for (var i = 0; i < 5; i++)
        {
            var mtn = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
            _alice.Zones.Battlefield.AddCard(mtn);
            mtn.SetZone(ZoneType.Battlefield);
        }

        var trig = BindTriggers(land).Single();

        // The chosen "any target" is Bob — resolution deals 3 to him (CR 119).
        trig.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });
        foreach (var e in trig.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(17, "Valakut deals 3 to the AGENT-CHOSEN target");
        _alice.LifeTotal.Should().Be(20, "the controller is unaffected");
    }

    [Fact]
    public void Valakut_NoChosenTarget_IsCleanNoOp()
    {
        // "you may" + no target chosen → the optional ability does nothing.
        var land = MakeLandShell("Valakut, the Molten Pinnacle");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        for (var i = 0; i < 5; i++)
        {
            var mtn = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain })
            { Owner = _alice, Controller = _alice };
            _alice.Zones.Battlefield.AddCard(mtn);
            mtn.SetZone(ZoneType.Battlefield);
        }

        var trig = BindTriggers(land).Single();
        foreach (var e in trig.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no chosen target → no damage");
    }

    // -----------------------------------------------------------------------
    // Frostwalk Bastion — "Whenever this land deals combat damage to a
    // creature, tap that creature and it doesn't untap during its controller's
    // next untap step." Bound (non-targeted) via the binder's prod path.
    // -----------------------------------------------------------------------

    [Fact]
    public void FrostwalkBastion_BindsCombatDamageToCreatureRiderTrigger()
    {
        var land = MakeLandShell("Frostwalk Bastion");
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // The binder must surface a real ITriggeredAbility for the combat-damage
        // rider (the audit's MissingTrigger criterion). It's non-targeted.
        var triggers = OracleTriggeredAbilityBinder.Bind(
            land, _repo.GetByName("Frostwalk Bastion")!, _alice, _players,
            eventBus: new Majik.Core.Events.EventBus()).ToList();
        triggers.Should().ContainSingle();
        triggers[0].TargetRequests.Should().BeEmpty("the rider acts on the damaged creature, not a chosen target");
    }
}
