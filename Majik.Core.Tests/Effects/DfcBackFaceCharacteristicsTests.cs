using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 711 / 712 — Layer-0 per-face characteristic replacement for transformed
/// DFC permanents (deferral #19). When a permanent is on its BACK face the
/// engine seeds <see cref="ContinuousEffectsService.Compute(Permanent)"/> from
/// the back face's printed characteristics (name-bearing type line, subtypes,
/// supertypes, P/T, keywords, colour) instead of the front-printed Card values;
/// the normal CR 613 layer pipeline (anthems, counters, type-grants) applies on
/// TOP of the back-face body. Reverts automatically on transform-back.
///
/// Covers: Delver of Secrets → Insectile Aberration 3/2 flying; Graveyard
/// Trespasser → Graveyard Glutton 4/4; layers-on-top; revert; generation-cache
/// invalidation on transform.
/// </summary>
public class DfcBackFaceCharacteristicsTests
{
    private readonly Player _alice = new("Alice", 20);

    // ------------------------------------------------------------------
    // Delver of Secrets → Insectile Aberration 3/2 flying.
    // ------------------------------------------------------------------

    [Fact]
    public void Delver_FrontFace_IsPrinted_1_1_NoFlying_Blue()
    {
        var svc = new ContinuousEffectsService();
        var delver = DelverOfSecretsFactory.Create(_alice);
        delver.SetZone(ZoneType.Battlefield);
        delver.ActiveEffects = svc;

        delver.Power.Should().Be(1);
        delver.Toughness.Should().Be(1);
        CombatAbilities.HasFlying(delver).Should().BeFalse();
        delver.MdfcState!.IsBackFace.Should().BeFalse();

        var chars = svc.Compute((Permanent)delver);
        chars.Subtypes.Should().Contain(CardSubtype.Wizard);
        chars.Subtypes.Should().NotContain(CardSubtype.Insect);
    }

    [Fact]
    public void Delver_Transformed_IsInsectileAberration_3_2_Flying_HumanInsect()
    {
        var svc = new ContinuousEffectsService();
        var delver = DelverOfSecretsFactory.Create(_alice);
        delver.SetZone(ZoneType.Battlefield);
        delver.ActiveEffects = svc;

        delver.MdfcState!.Transform();

        // Back-face body surfaces through Compute + the combat keyword read.
        delver.Power.Should().Be(3, "Insectile Aberration is 3/2");
        delver.Toughness.Should().Be(2);
        CombatAbilities.HasFlying(delver).Should().BeTrue(
            "Insectile Aberration has Flying (CR 711/712 Layer-0 seed)");

        var chars = svc.Compute((Permanent)delver);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Human);
        chars.Subtypes.Should().Contain(CardSubtype.Insect);
        chars.Subtypes.Should().NotContain(CardSubtype.Wizard,
            "the front-face Wizard subtype is replaced by the back face");
        chars.Colors.Should().Contain(ManaColor.Blue);
        delver.MdfcState.ActiveFaceName.Should().Be("Insectile Aberration");
    }

    [Fact]
    public void Delver_TransformBack_RevertsToFrontFace_1_1_NoFlying()
    {
        var svc = new ContinuousEffectsService();
        var delver = DelverOfSecretsFactory.Create(_alice);
        delver.SetZone(ZoneType.Battlefield);
        delver.ActiveEffects = svc;

        delver.MdfcState!.Transform();
        delver.Power.Should().Be(3);

        // Flip back to the front face (CR 711 — transform is symmetric).
        delver.MdfcState.Transform();

        delver.MdfcState.IsBackFace.Should().BeFalse();
        delver.Power.Should().Be(1, "transform-back reverts to the printed 1/1");
        delver.Toughness.Should().Be(1);
        CombatAbilities.HasFlying(delver).Should().BeFalse();

        var chars = svc.Compute((Permanent)delver);
        chars.Subtypes.Should().Contain(CardSubtype.Wizard);
        chars.Subtypes.Should().NotContain(CardSubtype.Insect);
    }

    // ------------------------------------------------------------------
    // Graveyard Trespasser → Graveyard Glutton 4/4 (deferral #13 residual).
    // ------------------------------------------------------------------

    [Fact]
    public void GraveyardTrespasser_FrontFace_IsHumanWerewolf_3_3()
    {
        var svc = new ContinuousEffectsService();
        var card = GraveyardTrespasserFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        card.ActiveEffects = svc;

        card.Power.Should().Be(3);
        card.Toughness.Should().Be(3);

        var chars = svc.Compute((Permanent)card);
        chars.Subtypes.Should().Contain(CardSubtype.Human);
        chars.Subtypes.Should().Contain(CardSubtype.Werewolf);
    }

    [Fact]
    public void GraveyardTrespasser_Transformed_IsGraveyardGlutton_4_4_Werewolf_Black()
    {
        var svc = new ContinuousEffectsService();
        var card = GraveyardTrespasserFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);
        card.ActiveEffects = svc;

        // Day/night transform flips to the back face (CR 702.145).
        card.MdfcState!.Transform();

        card.Power.Should().Be(4, "Graveyard Glutton is a 4/4");
        card.Toughness.Should().Be(4);

        var chars = svc.Compute((Permanent)card);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Werewolf);
        chars.Subtypes.Should().NotContain(CardSubtype.Human,
            "Graveyard Glutton is just a Werewolf, not a Human Werewolf");
        chars.Colors.Should().Contain(ManaColor.Black);
        card.MdfcState.ActiveFaceName.Should().Be("Graveyard Glutton");
    }

    // ------------------------------------------------------------------
    // Layers apply ON TOP of the back-face seed.
    // ------------------------------------------------------------------

    [Fact]
    public void BackFaceBody_IsModifiedBy_Plus1Plus1_Counters_Layer7c()
    {
        var svc = new ContinuousEffectsService();
        var delver = DelverOfSecretsFactory.Create(_alice);
        delver.SetZone(ZoneType.Battlefield);
        delver.ActiveEffects = svc;

        delver.MdfcState!.Transform(); // 3/2 Insectile Aberration
        delver.Counters.Add(CounterType.PlusOnePlusOne, 2);

        delver.Power.Should().Be(5, "3 base back-face power + 2 counters");
        delver.Toughness.Should().Be(4, "2 base back-face toughness + 2 counters");
        CombatAbilities.HasFlying(delver).Should().BeTrue(
            "counters do not strip the back-face Flying keyword");
    }

    [Fact]
    public void BackFaceBody_IsModifiedBy_AnthemPump_OnTopOfBackPT()
    {
        var svc = new ContinuousEffectsService();
        var delver = DelverOfSecretsFactory.Create(_alice);
        delver.SetZone(ZoneType.Battlefield);
        delver.ActiveEffects = svc;

        delver.MdfcState!.Transform(); // 3/2
        svc.Register(new BackFaceAnthemPump(delver, 2, 2));

        delver.Power.Should().Be(5, "back-face 3 + anthem 2");
        delver.Toughness.Should().Be(4, "back-face 2 + anthem 2");
    }

    // ------------------------------------------------------------------
    // Generation-cache invalidation on transform.
    // ------------------------------------------------------------------

    [Fact]
    public void Transform_InvalidatesTheGenerationCache()
    {
        var svc = new ContinuousEffectsService();
        var delver = DelverOfSecretsFactory.Create(_alice);
        delver.SetZone(ZoneType.Battlefield);
        delver.ActiveEffects = svc;

        // Prime the cache on the front face.
        delver.Power.Should().Be(1);

        // Transform must bump the generation so the stale 1/1 cache entry is
        // not served — the next read sees the 3/2 back face.
        delver.MdfcState!.Transform();
        delver.Power.Should().Be(3, "transform invalidated the P/T cache");
    }

    [Fact]
    public void FaceDown_OverridesBackFaceSeed_StillTwoTwoVanilla()
    {
        // CR 708.2 — a face-down permanent is a 2/2 with no characteristics,
        // even if it transformed first. Face-down wins over the back-face seed.
        var svc = new ContinuousEffectsService();
        var delver = DelverOfSecretsFactory.Create(_alice);
        delver.SetZone(ZoneType.Battlefield);
        delver.ActiveEffects = svc;

        delver.MdfcState!.Transform();
        delver.MarkFaceDown();

        delver.Power.Should().Be(2);
        delver.Toughness.Should().Be(2);
        CombatAbilities.HasFlying(delver).Should().BeFalse(
            "face-down strips the back-face Flying");
    }

    /// <summary>Anthem-style +N/+N pump at Layer 7c, used to prove layers stack
    /// on top of the back-face seed.</summary>
    private sealed class BackFaceAnthemPump : ContinuousEffect
    {
        private readonly Creature _t;
        private readonly int _p, _to;
        public BackFaceAnthemPump(Creature t, int p, int to) { _t = t; _p = p; _to = to; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _t);
        public override void Apply(CreatureCharacteristics chars)
        { chars.Power += _p; chars.Toughness += _to; }
    }
}
