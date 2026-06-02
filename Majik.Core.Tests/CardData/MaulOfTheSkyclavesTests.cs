using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MaulOfTheSkyclavesFactory"/>.
///
/// Maul of the Skyclaves (Zendikar Rising, {2}{W}) — Artifact — Equipment.
/// Oracle text (Scryfall, verified 2026-06-02):
///   "When this Equipment enters, attach it to target creature you control."
///   "Equipped creature gets +2/+2 and has flying and first strike."
///   "Equip {2}{W}{W}"
///
/// Covers:
/// - Identity (name, type, Equipment subtype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Equip {2}{W}{W} activated-ability shape.
/// - Static +2/+2 boost (Layer 7c) on the equipped creature.
/// - Granted flying + first strike (CR 702.9 / CR 702.7) read through layers.
/// - Detach: boost lapses and granted keywords are revoked.
/// - ETB trigger present + auto-attaches the Maul to the first controller
///   creature.
/// </summary>
public class MaulOfTheSkyclavesTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MaulOfTheSkyclaves_Identity()
    {
        var c = MaulOfTheSkyclavesFactory.Create(_alice);

        c.Name.Should().Be("Maul of the Skyclaves");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue(
            "Maul of the Skyclaves is an Equipment");
        c.ManaCost.Should().Be("{2}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MaulOfTheSkyclaves_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Maul of the Skyclaves", _alice);

        c.Should().BeOfType<Artifact>("Maul of the Skyclaves is an Artifact");
        c.Name.Should().Be("Maul of the Skyclaves");
        c.HasSubtype(CardSubtype.Equipment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Equip cost {2}{W}{W}
    // -----------------------------------------------------------------------

    [Fact]
    public void MaulOfTheSkyclaves_EquipAbility_Has2WWCost()
    {
        var c = MaulOfTheSkyclavesFactory.Create(_alice);

        var equip = c.Abilities.OfType<EquipActivatedAbility>().Single();
        equip.EquipCost.Generic.Should().Be(2, "printed Equip {2}{W}{W}");
        equip.EquipCost.White.Should().Be(2, "printed Equip {2}{W}{W}");
    }

    // -----------------------------------------------------------------------
    // Static continuous effects — +2/+2, flying, first strike
    // -----------------------------------------------------------------------

    [Fact]
    public void MaulOfTheSkyclaves_Equipped_Bear_Becomes_4_4()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var maul = MaulOfTheSkyclavesFactory.Create(_alice, svc, triggers: null);
        maul.Zone = ZoneType.Battlefield;

        maul.AttachTo(bear);

        bear.GetPower().Should().Be(4, "+2/+2 boost from Maul of the Skyclaves");
        bear.GetToughness().Should().Be(4, "+2/+2 boost from Maul of the Skyclaves");
    }

    [Fact]
    public void MaulOfTheSkyclaves_GrantsFlyingAndFirstStrike_ToEquippedCreature()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        CombatAbilities.HasFlying(bear).Should().BeFalse("the bear has no printed flying");
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse(
            "the bear has no printed first strike");

        var maul = MaulOfTheSkyclavesFactory.Create(_alice, svc, triggers: null);
        maul.Zone = ZoneType.Battlefield;
        maul.AttachTo(bear);

        // CR 613 — a Layer-6 ability grant materialises onto the bearer during
        // a layer pass (SyncAbilityGrants); the returned keyword set settles on
        // the FOLLOWING Compute (the grant-attach side effect invalidates the
        // in-pass cache by design). Prime one pass so the assertion reads the
        // settled state, exactly as repeated SBA / layer recomputation does
        // during a real game.
        svc.Compute(bear);

        CombatAbilities.HasFlying(bear).Should().BeTrue(
            "Maul of the Skyclaves grants flying to the equipped creature (CR 702.9)");
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue(
            "Maul of the Skyclaves grants first strike to the equipped creature (CR 702.7)");
    }

    [Fact]
    public void MaulOfTheSkyclaves_Detach_RevokesBoostAndKeywords()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        var maul = MaulOfTheSkyclavesFactory.Create(_alice, svc, triggers: null);
        maul.Zone = ZoneType.Battlefield;
        maul.AttachTo(bear);

        // While attached: 4/4, flying, first strike. Prime a layer pass so the
        // Layer-6 grants settle (see the grants test for the rationale).
        svc.Compute(bear);
        bear.GetPower().Should().Be(4);
        CombatAbilities.HasFlying(bear).Should().BeTrue();
        CombatAbilities.HasFirstStrike(bear).Should().BeTrue();

        maul.Unattach();

        // All grants gate on AttachedTo — revoked on detach. Prime once more so
        // the revoke settles before asserting.
        svc.Compute(bear);
        bear.GetPower().Should().Be(2, "boost lapses on detach");
        bear.GetToughness().Should().Be(2, "boost lapses on detach");
        CombatAbilities.HasFlying(bear).Should().BeFalse("granted flying is revoked");
        CombatAbilities.HasFirstStrike(bear).Should().BeFalse(
            "granted first strike is revoked once the Maul is no longer attached");
    }

    // -----------------------------------------------------------------------
    // ETB-attach trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void MaulOfTheSkyclaves_HasEtbAttachTrigger()
    {
        var maul = MaulOfTheSkyclavesFactory.Create(_alice);

        maul.Abilities.OfType<TriggeredAbility>().Should().NotBeEmpty(
            "Maul of the Skyclaves has a 'when this Equipment enters' attach trigger");
    }

    [Fact]
    public void MaulOfTheSkyclaves_EtbTrigger_AutoAttachesToBear()
    {
        // Direct-effect smoke test: locate the trigger, fire its predicate once
        // with a CardMovedEvent for the Maul's own ETB, then resolve the
        // effect. Bypasses TriggerManager wiring — same posture as the
        // shape-only equipment tests.
        var svc = new ContinuousEffectsService();
        var maul = MaulOfTheSkyclavesFactory.Create(_alice, svc, triggers: null);
        maul.Zone = ZoneType.Battlefield;
        _alice.Zones.Battlefield.AddCard(maul);

        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
        };
        _alice.Zones.Battlefield.AddCard(bear);

        var trigger = maul.Abilities.OfType<TriggeredAbility>().Single();

        var moved = new Majik.Core.Events.CardMovedEvent(
            maul, ZoneType.Hand, ZoneType.Battlefield);
        var matched = trigger.Condition.Matches(moved, trigger);
        matched.Should().BeTrue("the ETB trigger fires on the Maul's own ETB");

        foreach (var eff in trigger.Effects) eff.Execute();

        maul.AttachedTo.Should().BeSameAs(bear,
            "the ETB trigger auto-attaches the Maul to the first controller creature");
    }

    [Fact]
    public void MaulOfTheSkyclaves_EtbTrigger_DoesNotFireForOtherEquipment()
    {
        // The trigger is scoped to THIS card's own ETB ("this Equipment"),
        // unlike Hammer of Nazahn's "an Equipment" trigger.
        var maul = MaulOfTheSkyclavesFactory.Create(_alice);
        var trigger = maul.Abilities.OfType<TriggeredAbility>().Single();

        var otherEquipment = new Artifact("Bonesplitter", "{1}",
            subtypes: new[] { CardSubtype.Equipment });
        otherEquipment.SetOwner(_alice);
        otherEquipment.SetController(_alice);

        var moved = new Majik.Core.Events.CardMovedEvent(
            otherEquipment, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moved, trigger).Should().BeFalse(
            "Maul's trigger only fires on its own ETB, not other Equipment");
    }
}
