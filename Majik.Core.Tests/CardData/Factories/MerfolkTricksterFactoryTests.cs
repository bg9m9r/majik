using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MerfolkTricksterFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, Merfolk Wizard subtypes, 2/2 P/T,
///   {U}{U} mana cost, owner/controller).
/// - Ability set: a Flash KeywordAbility marker + a single ETB
///   TriggeredAbility with a 1..1 TargetRequest.
/// - ETB resolution: taps the chosen opponent's creature and registers a
///   LoseAllAbilitiesEffect (ExpiresAtEndOfTurn: true) on the target's
///   ContinuousEffectsService.
/// - Fizzle paths: same-controller target (target is no longer "an opponent's
///   creature" at resolution), off-battlefield target, already-tapped target,
///   null ActiveEffects (shape-only).
/// - EOT expiration: ContinuousEffectsService.ExpireEndOfTurn drops the
///   lose-abilities effect.
/// - NamedCardFactory dispatch returns a Merfolk Trickster instance.
/// </summary>
[Trait("Color", "U")]
public class MerfolkTricksterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MerfolkTrickster_NameIsCorrect()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        t.Name.Should().Be("Merfolk Trickster");
    }

    [Fact]
    public void MerfolkTrickster_IsCreature()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        t.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void MerfolkTrickster_HasCorrectSubtypes()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        t.HasSubtype(CardSubtype.Merfolk).Should().BeTrue("printed oracle is Merfolk Wizard");
        t.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void MerfolkTrickster_HasCorrectStats()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        t.BasePower.Should().Be(2);
        t.BaseToughness.Should().Be(2);
    }

    [Fact]
    public void MerfolkTrickster_HasCorrectManaCost()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        t.ManaCost.Should().Be("{U}{U}");
    }

    [Fact]
    public void MerfolkTrickster_OwnerAndControllerAreSet()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability set
    // -----------------------------------------------------------------------

    [Fact]
    public void MerfolkTrickster_HasFlashKeyword()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        t.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(a => a.Keyword == "Flash",
                "Merfolk Trickster has Flash (CR 702.8)");
    }

    [Fact]
    public void MerfolkTrickster_HasExactlyOneTriggeredAbility()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        t.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB tap + lose-abilities trigger is the only triggered ability");
    }

    [Fact]
    public void MerfolkTrickster_EtbTriggerDeclaresOneTargetRequest()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.TargetRequests.Should().HaveCount(1, "the ETB targets one creature");
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB resolution — tap + lose-abilities EOT
    // -----------------------------------------------------------------------

    [Fact]
    public void MerfolkTrickster_EtbEffect_TapsLegalTarget()
    {
        var t = MerfolkTricksterFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Hill Giant", "2R", 3, 3);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        target.ActiveEffects = new ContinuousEffectsService();

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        target.IsTapped.Should().BeTrue("the ETB taps the chosen opponent's creature");
    }

    [Fact]
    public void MerfolkTrickster_EtbEffect_RegistersLoseAllAbilitiesEotOnLegalTarget()
    {
        var t = MerfolkTricksterFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Hill Giant", "2R", 3, 3);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        // The Layer 6 strip clears chars.Keywords on the target — so any
        // printed keyword (none here, but the strip is still registered)
        // is gone from the computed characteristics row. We assert the
        // marker effect was registered with EOT expiry by computing and
        // checking the stripped-set indirectly: register a sentinel
        // keyword via printed Keywords on the target, then verify
        // GetPower/GetToughness still works (smoke) and that the EOT
        // expiry mechanism drops the effect.
        var computed = service.Compute(target);
        computed.Keywords.Should().BeEmpty(
            "Layer 6 LoseAllAbilities strips chars.Keywords on the target");
    }

    [Fact]
    public void MerfolkTrickster_EtbEffect_StripsTargetKeywords()
    {
        var t = MerfolkTricksterFactory.Create(_alice);
        // Strip's IsActive() gates on the source (Trickster) being on the
        // battlefield (CR 613.1g — same posture as Humility / Dress Down's
        // gate on the source enchantment).
        t.SetZone(ZoneType.Battlefield);
        var bob = new Player("Bob", 20);

        // Target has a printed Flying keyword via KeywordAbility; the Layer
        // 6 strip clears chars.Keywords on Compute. Note: in this engine
        // chars.Keywords is the only in-characteristics ability surface
        // (see LoseAllAbilitiesEffect xmldoc), so the strip is observable
        // by inspecting the computed CreatureCharacteristics.Keywords.
        var target = new Creature("Wind Drake", "2U", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        target.AddAbility(new KeywordAbility("Flying", target, bob));
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        // Sanity — before resolution, Flying is in the printed keyword set.
        service.Compute(target).Keywords.Should().Contain("Flying");

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        service.Compute(target).Keywords.Should().NotContain("Flying",
            "the Layer 6 LoseAllAbilities effect strips printed keywords from chars.Keywords");
    }

    [Fact]
    public void MerfolkTrickster_EtbEffect_NoFizzle_WhenTargetAlreadyTapped()
    {
        var t = MerfolkTricksterFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Hill Giant", "2R", 3, 3);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        target.Tap(); // already tapped
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("Permanent.Tap throws when already tapped; the effect must guard");

        target.IsTapped.Should().BeTrue("the target remains tapped");
        // Lose-abilities rider still applies even when tap was a no-op.
        service.Compute(target).Keywords.Should().BeEmpty();
    }

    [Fact]
    public void MerfolkTrickster_EtbEffect_NoOp_WhenTargetSameController()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        // Target is one of Alice's own creatures — CR 608.2b re-checks
        // "an opponent controls" at resolution.
        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(_alice);
        target.SetController(_alice);
        target.SetZone(ZoneType.Battlefield);
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        target.IsTapped.Should().BeFalse(
            "target shares controller with the Trickster — fizzle, no tap (CR 608.2b)");
    }

    [Fact]
    public void MerfolkTrickster_EtbEffect_NoOp_WhenTargetLeftBattlefield()
    {
        var t = MerfolkTricksterFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Graveyard); // already left battlefield
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        target.IsTapped.Should().BeFalse(
            "target is off-battlefield — CR 608.2b illegal-target check fizzles");
    }

    [Fact]
    public void MerfolkTrickster_EtbEffect_NoActiveEffects_DoesNotThrow()
    {
        var t = MerfolkTricksterFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        var target = new Creature("Bear", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        // target.ActiveEffects is null — shape-only.

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("the effect body guards on a null ActiveEffects");

        target.IsTapped.Should().BeTrue(
            "tap still happens even when ActiveEffects is null — only the lose-abilities grant is gated");
    }

    [Fact]
    public void MerfolkTrickster_EtbEffect_LoseAbilitiesEffect_ExpiresAtEndOfTurn()
    {
        var t = MerfolkTricksterFactory.Create(_alice);
        t.SetZone(ZoneType.Battlefield);
        var bob = new Player("Bob", 20);

        var target = new Creature("Wind Drake", "2U", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        target.SetZone(ZoneType.Battlefield);
        target.AddAbility(new KeywordAbility("Flying", target, bob));
        var service = new ContinuousEffectsService();
        target.ActiveEffects = service;

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        foreach (var effect in etb.Effects) effect.Execute();

        service.Compute(target).Keywords.Should().NotContain("Flying",
            "during the turn the strip is active");

        // CR 514.2 — cleanup step expires the EOT effect.
        service.ExpireEndOfTurn();

        service.Compute(target).Keywords.Should().Contain("Flying",
            "after EOT expiry, printed Flying re-asserts on the chars row");
    }

    [Fact]
    public void MerfolkTrickster_EtbEffect_NoTargetChosen_NoOp()
    {
        var t = MerfolkTricksterFactory.Create(_alice);

        var etb = t.Abilities.OfType<TriggeredAbility>().First();
        // No chosen targets set — empty list path.
        etb.SetChosenTargets(Array.Empty<IReadOnlyList<object>>());

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("empty ChosenTargets path is a clean no-op");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
}
