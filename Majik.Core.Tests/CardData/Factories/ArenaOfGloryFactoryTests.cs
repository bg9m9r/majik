using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ArenaOfGloryFactory"/> (Modern Horizons 3, Land).
///
/// Oracle text:
///   "This land enters tapped unless you control a Mountain."
///   "{T}: Add {R}."
///   "{R}, {T}, Exert this land: Add {R}{R}. If that mana is spent on a
///    creature spell, it gains haste until end of turn. (An exerted
///    permanent won't untap during your next untap step.)"
///
/// Covers:
/// - Card identity (Land, non-legendary, owner/controller).
/// - ETB tapped unless you control a Mountain (CR 614.1c) via the
///   <see cref="ConditionalEntersTappedReplacement"/> over a ReplacementBus.
/// - {T}: Add {R} — vanilla red mana ability.
/// - Exert ability: pays {R} from the pool + taps the land, adds {R}{R},
///   and marks the land "doesn't untap during your next untap step" (CR
///   502.1 — the exert clause).
/// - Mana provenance: the {R}{R} produced by the exert ability grants the
///   creature spell it pays for haste until end of turn (CR 702.10); a
///   noncreature spell paid with that mana gets nothing, and a creature
///   spell paid with non-exert mana gets nothing.
/// - NamedCardFactory dispatcher resolves "Arena of Glory".
/// </summary>
public class ArenaOfGloryFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public void Dispose() => UntapStepRestrictions.Clear();

    private static ManaAbility SimpleRedTap(Land land) =>
        land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.TotalValue == 1);

    private static ManaAbility ExertDoubleRed(Land land) =>
        land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Red == 2);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ArenaOfGlory_IsLand()
    {
        var land = ArenaOfGloryFactory.Create(_alice);
        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ArenaOfGlory_NameIsCorrect()
    {
        var land = ArenaOfGloryFactory.Create(_alice);
        land.Name.Should().Be("Arena of Glory");
    }

    [Fact]
    public void ArenaOfGlory_OwnerAndControllerAreSet()
    {
        var land = ArenaOfGloryFactory.Create(_alice);
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArenaOfGlory_IsNotLegendary()
    {
        var land = ArenaOfGloryFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ArenaOfGlory_NamedCardFactory_ResolvesShape()
    {
        var card = NamedCardFactory.Create("Arena of Glory", _alice);

        card.Should().NotBeNull();
        card!.Name.Should().Be("Arena of Glory");
        card.HasType(CardType.Land).Should().BeTrue();
        // {T}: Add {R} + the {R}{R} exert ability.
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // ETB tapped unless you control a Mountain (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void ArenaOfGlory_EntersTapped_WhenNoMountainControlled()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var land = ArenaOfGloryFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Arena of Glory enters tapped when its controller has no Mountain");
    }

    [Fact]
    public void ArenaOfGlory_EntersUntapped_WhenControllerHasMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var mountain = (Land)NamedCardFactory.Create("Mountain", alice);
        alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        var land = ArenaOfGloryFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Arena of Glory enters untapped when its controller has a Mountain");
    }

    [Fact]
    public void ArenaOfGlory_EntersTapped_WhenOnlyOpponentHasMountain()
    {
        var bus = new ReplacementBus();
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var mountain = (Land)NamedCardFactory.Create("Mountain", bob);
        bob.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        var land = ArenaOfGloryFactory.Create(alice, replacements: bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: alice);

        var after = bus.Apply(intent);
        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "only the controller's own Mountains turn off the tapped clause");
    }

    [Fact]
    public void ArenaOfGlory_SingleArgDispatch_DoesNotRegisterReplacement()
    {
        // No ReplacementBus on the dispatcher path → ETB-tapped predicate
        // omitted (shape-only posture, mirrors CheckLandCycleFactory).
        var land = ArenaOfGloryFactory.Create(_alice);
        land.Should().NotBeNull();
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {R}
    // -----------------------------------------------------------------------

    [Fact]
    public void ArenaOfGlory_HasSimpleRedTapAbility()
    {
        var land = ArenaOfGloryFactory.Create(_alice);
        var tap = SimpleRedTap(land);
        tap.ManaGenerated.Red.Should().Be(1);
        tap.ManaGenerated.TotalValue.Should().Be(1);
    }

    [Fact]
    public void ArenaOfGlory_SimpleRedTap_TapsLandAndAddsRed()
    {
        var alice = new Player("Alice", 20);
        var land = ArenaOfGloryFactory.Create(alice);
        var produced = SimpleRedTap(land).Activate();

        produced.Red.Should().Be(1);
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Exert ability — {R}, {T}, Exert: Add {R}{R}
    // -----------------------------------------------------------------------

    [Fact]
    public void ArenaOfGlory_ExertAbility_ProducesDoubleRed()
    {
        var land = ArenaOfGloryFactory.Create(_alice);
        ExertDoubleRed(land).ManaGenerated.Red.Should().Be(2);
    }

    [Fact]
    public void ArenaOfGlory_ExertAbility_CannotActivateWithoutRedInPool()
    {
        var alice = new Player("Alice", 20);
        var land = ArenaOfGloryFactory.Create(alice);

        ExertDoubleRed(land).CanActivate().Should().BeFalse(
            "the {R} portion of the cost must be in the pool to activate");
    }

    [Fact]
    public void ArenaOfGlory_ExertAbility_CannotActivateWhenTapped()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice);
        land.Tap();

        ExertDoubleRed(land).CanActivate().Should().BeFalse(
            "the {T} portion of the cost requires an untapped land");
    }

    [Fact]
    public void ArenaOfGlory_ExertAbility_PaysRedAndTapsAndAddsDoubleRed()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice);
        var activator = new ManaAbilityActivator();

        var produced = activator.ActivateManaAbility(ExertDoubleRed(land), alice);

        produced.Red.Should().Be(2, "exert produces {R}{R}");
        land.IsTapped.Should().BeTrue("{T} cost tapped the land");
        // Started with 1 R, the {R} cost consumed it, then +2 R produced.
        alice.ManaPool.Red.Should().Be(2,
            "the {R} cost was paid and {R}{R} was added to the pool");
    }

    [Fact]
    public void ArenaOfGlory_ExertAbility_MarksLandDoesNotUntapNextUntapStep()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice);

        UntapStepRestrictions.ShouldSkipUntap(land, alice).Should().BeFalse(
            "no exert yet");

        ExertDoubleRed(land).Activate();

        UntapStepRestrictions.ShouldSkipUntap(land, alice).Should().BeTrue(
            "exerting the land marks it to skip the controller's next untap step (CR 502.1)");
    }

    [Fact]
    public void ArenaOfGlory_ExertCleanup_RemovesSkipAfterNextUntapStep()
    {
        var bus = new EventBus();
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice, eventBus: bus, replacements: null);

        ExertDoubleRed(land).Activate();
        UntapStepRestrictions.ShouldSkipUntap(land, alice).Should().BeTrue();

        // Controller's next Untap step fires → the one-shot exert skip lifts.
        bus.Publish(new StepStartedEvent(PhaseStateType.Untap, alice));

        UntapStepRestrictions.ShouldSkipUntap(land, alice).Should().BeFalse(
            "the exert skip is a one-shot — it lifts after the controller's next untap step");
    }

    // -----------------------------------------------------------------------
    // Mana provenance — "if that mana is spent on a creature spell, it gains
    // haste until end of turn" (CR 702.10)
    // -----------------------------------------------------------------------

    [Fact]
    public void ArenaOfGlory_ExertActivation_TagsManaWithSlotProvenance()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice);
        var exert = ExertDoubleRed(land);

        alice.ManaProvenance.Should().BeEmpty();

        new ManaAbilityActivator().ActivateManaAbility(exert, alice);

        // {R}{R} produced → two red provenance slots tagged with the exert
        // ability (CR 106.4). The paid {R} from the pool was untagged, so it
        // adds no slots.
        alice.ManaProvenance.Should().HaveCount(2);
        alice.ManaProvenance.Should().OnlyContain(s =>
            s.Color == ManaColor.Red && ReferenceEquals(s.Source, exert));
    }

    [Fact]
    public void ArenaOfGlory_CreatureSpellPaidWithExertMana_GainsHaste()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice);

        // Exert: produces {R}{R} provenance-tagged for haste.
        new ManaAbilityActivator().ActivateManaAbility(ExertDoubleRed(land), alice);

        // Pay {R} for a creature spell from the exert-tagged floating pool —
        // the realistic flow the engine drives in TurnDriver.DispatchCast.
        var goblin = new Creature("Goblin", "R", 1, 1) { Owner = alice, Zone = ZoneType.Hand };
        var resolver = new ManaPaymentResolver();
        resolver.Pay(alice, ManaCost.Parse("R"), ManaPayment.Empty, spentOn: goblin, out _, out _)
            .Should().BeTrue();

        CombatAbilities.HasHaste(goblin).Should().BeTrue(
            "the exert-tagged {R} paid for a creature spell ⇒ it gains haste (CR 702.10)");
    }

    [Fact]
    public void ArenaOfGlory_NoncreatureSpellPaidWithExertMana_GetsNoHaste()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice);
        new ManaAbilityActivator().ActivateManaAbility(ExertDoubleRed(land), alice);

        var bolt = new Instant("Bolt", "R") { Owner = alice, Zone = ZoneType.Hand };
        var resolver = new ManaPaymentResolver();
        resolver.Pay(alice, ManaCost.Parse("R"), ManaPayment.Empty, spentOn: bolt, out _, out _)
            .Should().BeTrue();

        // The provenance slot was consumed (spent on a noncreature) — the
        // reaction fired but granted nothing; one tagged slot remains floating.
        alice.ManaProvenance.Should().HaveCount(1,
            "one exert {R} was spent on the noncreature; the other is still floating");
    }

    [Fact]
    public void ArenaOfGlory_CreatureSpellWithoutExertMana_GetsNoHaste()
    {
        var alice = new Player("Alice", 20);
        // No exert — just plain (untagged) red mana in the pool.
        alice.AddManaToPool(ManaCost.Parse("RR"));

        var goblin = new Creature("Goblin", "R", 1, 1) { Owner = alice, Zone = ZoneType.Hand };
        var resolver = new ManaPaymentResolver();
        resolver.Pay(alice, ManaCost.Parse("R"), ManaPayment.Empty, spentOn: goblin, out _, out _)
            .Should().BeTrue();

        CombatAbilities.HasHaste(goblin).Should().BeFalse(
            "a creature paid with ordinary mana gets no haste — provenance is required");
    }

    [Fact]
    public void ArenaOfGlory_UnrelatedLaterSpell_DoesNotGetHaste()
    {
        // The bug the coarse player-scoped counter had: it granted haste to
        // the FIRST spell cast after the exert, not the spell the exert mana
        // actually paid for. With slot-level provenance, the exert {R}{R} that
        // fully pays creature1 leaves NO tagged mana for a later creature2 paid
        // with fresh untapped lands.
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice);
        new ManaAbilityActivator().ActivateManaAbility(ExertDoubleRed(land), alice);

        var resolver = new ManaPaymentResolver();

        // creature1 spends both exert {R}{R}.
        var creature1 = new Creature("First", "RR", 2, 2) { Owner = alice, Zone = ZoneType.Hand };
        resolver.Pay(alice, ManaCost.Parse("RR"), ManaPayment.Empty, spentOn: creature1, out _, out _)
            .Should().BeTrue();
        CombatAbilities.HasHaste(creature1).Should().BeTrue("creature1 was paid with the exert mana");

        // creature2 is paid with fresh (untagged) mountains — no exert mana left.
        var m1 = NamedCardFactory.Create("Mountain", alice);
        var m2 = NamedCardFactory.Create("Mountain", alice);
        m1.SetZone(ZoneType.Battlefield);
        m2.SetZone(ZoneType.Battlefield);
        var creature2 = new Creature("Second", "RR", 2, 2) { Owner = alice, Zone = ZoneType.Hand };
        resolver.Pay(alice, ManaCost.Parse("RR"),
            new ManaPayment(new ICard[] { m1, m2 }), spentOn: creature2, out _, out _)
            .Should().BeTrue();

        CombatAbilities.HasHaste(creature2).Should().BeFalse(
            "the exert mana was already spent on creature1 — an unrelated later spell gets no haste");
    }

    [Fact]
    public void ArenaOfGlory_ManaFromTaggedSource_TriggersReaction_UntaggedSourceDoesNot()
    {
        // Mana from source A (tagged with a reaction) spent on a spell fires
        // the reaction; mana from source B (untagged) spent on the same spell
        // does not. Strict per-source provenance.
        var alice = new Player("Alice", 20);
        var land = ArenaOfGloryFactory.Create(alice);
        alice.AddManaToPool(ManaCost.Parse("R")); // for the exert's {R} cost
        new ManaAbilityActivator().ActivateManaAbility(ExertDoubleRed(land), alice);
        // Now pool = {R}{R} both tagged by the exert (source A).

        // Add one untagged {R} (source B — plain mana, no provenance).
        alice.AddManaToPool(ManaCost.Parse("R"));

        alice.ManaProvenance.Should().HaveCount(2, "only source A's {R}{R} is tagged");

        // Spend exactly {R} on a creature — FIFO consumes one source-A slot,
        // firing its haste reaction.
        var goblin = new Creature("Goblin", "R", 1, 1) { Owner = alice, Zone = ZoneType.Hand };
        var resolver = new ManaPaymentResolver();
        resolver.Pay(alice, ManaCost.Parse("R"), ManaPayment.Empty, spentOn: goblin, out _, out _)
            .Should().BeTrue();

        CombatAbilities.HasHaste(goblin).Should().BeTrue(
            "the spent {R} came from the tagged exert source ⇒ reaction fired");
        alice.ManaProvenance.Should().HaveCount(1,
            "one tagged slot consumed; one tagged + one untagged {R} remain");
    }

    [Fact]
    public void Player_EmptyManaPool_ClearsSlotProvenance()
    {
        var alice = new Player("Alice", 20);
        alice.AddManaToPool(ManaCost.Parse("R"));
        var land = ArenaOfGloryFactory.Create(alice);
        new ManaAbilityActivator().ActivateManaAbility(ExertDoubleRed(land), alice);
        alice.ManaProvenance.Should().HaveCount(2);

        alice.EmptyManaPool();

        alice.ManaProvenance.Should().BeEmpty(
            "slot provenance dies with the floating mana at end of step/phase (CR 500.4)");
    }
}
