using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="SleepCursedFaerieFactory"/> — Creature — Faerie Wizard
/// {U} 3/3 (Scryfall, verified 2026-06-14):
///   "Flying, ward {2}
///    This creature enters tapped with three stun counters on it. (If it would
///    become untapped, remove a stun counter from it instead.)
///    {1}{U}: Untap this creature."
///
/// Covers ONLY the card's unique behaviour (plus one identity assert):
///   - Identity: {U}, 3/3, Faerie Wizard.
///   - Flying + Ward keyword markers (CR 702.9 / CR 702.21).
///   - ETB: taps itself (CR 701.20) and puts three stun counters on it
///     (CR 122.1c / 122.1g).
///   - {1}{U} untap ability: while stun counters remain, each activation
///     removes one stun counter instead of untapping (CR 122.1g); once they are
///     gone the activation untaps it.
/// </summary>
[Trait("Color", "U")]
public class SleepCursedFaerieFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature OnBattlefield(Creature c, Player owner)
    {
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats — single identity assert)
    // -----------------------------------------------------------------------

    [Fact]
    public void SleepCursedFaerie_IsFaerieWizard_3_3_AtCostU()
    {
        var c = SleepCursedFaerieFactory.Create(_alice);

        c.Name.Should().Be("Sleep-Cursed Faerie");
        c.ManaCost.Should().Be("{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Faerie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SleepCursedFaerie_HasFlyingAndWard()
    {
        var c = SleepCursedFaerieFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Ward");
    }

    // -----------------------------------------------------------------------
    // ETB — enters tapped with three stun counters (CR 122.1g)
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_TapsSelf_AndPutsThreeStunCounters()
    {
        var faerie = OnBattlefield(SleepCursedFaerieFactory.Create(_alice), _alice);
        faerie.IsTapped.Should().BeFalse("entry tap is applied by the ETB effect, not at construction.");

        var etb = faerie.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        faerie.IsTapped.Should().BeTrue("CR 701.20 — it enters tapped.");
        faerie.Counters.Count(CounterType.Stun).Should().Be(3,
            "CR 122.1g — it enters with three stun counters.");
    }

    // -----------------------------------------------------------------------
    // {1}{U}: Untap this creature (CR 602 + CR 122.1g replacement)
    // -----------------------------------------------------------------------

    [Fact]
    public void UntapAbility_CostsOneU()
    {
        var faerie = SleepCursedFaerieFactory.Create(_alice);

        var ability = faerie.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().HaveCount(1, "the only cost is the {1}{U} mana cost.");
    }

    [Fact]
    public void UntapAbility_WhileStunned_RemovesOneStunCounterInsteadOfUntapping()
    {
        var faerie = OnBattlefield(SleepCursedFaerieFactory.Create(_alice), _alice);

        // Simulate the entry state: tapped with three stun counters.
        var etb = faerie.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        var ability = faerie.Abilities.OfType<ActivatedAbility>().Single();

        ability.Resolve();
        faerie.Counters.Count(CounterType.Stun).Should().Be(2,
            "CR 122.1g — the untap is replaced by removing one stun counter.");
        faerie.IsTapped.Should().BeTrue("it is still tapped — only a stun counter came off.");

        ability.Resolve();
        ability.Resolve();
        faerie.Counters.Count(CounterType.Stun).Should().Be(0, "three activations clear all three stun counters.");
        faerie.IsTapped.Should().BeTrue("it remains tapped after the last stun counter is removed.");
    }

    [Fact]
    public void UntapAbility_WithoutStunCounters_ActuallyUntaps()
    {
        var faerie = OnBattlefield(SleepCursedFaerieFactory.Create(_alice), _alice);
        faerie.Tap(); // tapped, but no stun counters left

        var ability = faerie.Abilities.OfType<ActivatedAbility>().Single();
        ability.Resolve();

        faerie.IsTapped.Should().BeFalse("CR 122.1g — with no stun counters, the untap happens normally.");
    }
}
