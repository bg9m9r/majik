using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="OverlordOfTheBoilerbilgesFactory"/> — Overlord of the
/// Boilerbilges (Duskmourn: House of Horror, {4}{R}{R}). Enchantment Creature —
/// Avatar Horror 5/5.
///
/// Covers:
///   - Card shape (name, types Creature + Enchantment, Avatar + Horror
///     subtypes, {4}{R}{R}, 5/5).
///   - Impending 4 marker keyword (mechanic deferred; marker present).
///   - Two enters-or-attacks triggered abilities (ETB + attack), each with a
///     single 1..1 "any target" request.
///   - NamedCardFactory dispatch.
///   - Trigger body: deals 4 damage to any target — Creature (marked damage),
///     Player (life loss), Planeswalker (loyalty removal).
/// </summary>
public class OverlordOfTheBoilerbilgesTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Boilerbilges_IsEnchantmentCreature_AvatarHorror_FiveFive()
    {
        var c = OverlordOfTheBoilerbilgesFactory.Create(_alice);

        c.Name.Should().Be("Overlord of the Boilerbilges");
        c.ManaCost.Should().Be("{4}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Avatar).Should().BeTrue();
        c.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        c.BasePower.Should().Be(5);
        c.BaseToughness.Should().Be(5);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Boilerbilges_HasImpendingMarker_WithCount4()
    {
        var c = OverlordOfTheBoilerbilgesFactory.Create(_alice);

        var impending = c.Abilities.OfType<KeywordAbility>()
            .SingleOrDefault(k => k.Keyword == "Impending");
        impending.Should().NotBeNull();
        impending!.Arg.Should().Be(4);
    }

    [Fact]
    public void Boilerbilges_HasTwoTriggers_EntersAndAttacks_EachWithAnyTarget()
    {
        var c = OverlordOfTheBoilerbilgesFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "Boilerbilges prints one ability that triggers on enters OR attacks "
            + "— modelled as two TriggeredAbility instances.");
        triggers.Should().OnlyContain(
            t => t.TargetRequests.Count == 1
                 && t.TargetRequests[0].MinTargets == 1
                 && t.TargetRequests[0].MaxTargets == 1,
            "each trigger deals 4 damage to a single any target.");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Boilerbilges()
    {
        var card = NamedCardFactory.Create("Overlord of the Boilerbilges", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Overlord of the Boilerbilges");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Impending");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Trigger body: deal 4 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void Trigger_DealsFourDamage_ToCreature()
    {
        var overlord = OverlordOfTheBoilerbilgesFactory.Create(_alice);
        var victim = new Creature("Victim", "{2}{G}", 4, 5);

        ResolveFirstTriggerAt(overlord, victim);

        victim.Damage.Should().Be(4, "deals 4 damage to any target");
    }

    [Fact]
    public void Trigger_DealsFourDamage_ToPlayer()
    {
        var overlord = OverlordOfTheBoilerbilgesFactory.Create(_alice);
        var bob = new Player("Bob", 20);

        ResolveFirstTriggerAt(overlord, bob);

        bob.LifeTotal.Should().Be(16, "20 - 4 = 16");
    }

    [Fact]
    public void Trigger_DealsFourDamage_ToPlaneswalker_AsLoyaltyRemoval()
    {
        var overlord = OverlordOfTheBoilerbilgesFactory.Create(_alice);
        var walker = new Planeswalker("Planey", "{2}{B}", 6);

        ResolveFirstTriggerAt(overlord, walker);

        walker.Loyalty.Should().Be(2, "6 starting loyalty - 4 = 2 (CR 306.7)");
    }

    [Fact]
    public void Trigger_WithNoTargetChosen_IsCleanNoOp()
    {
        var overlord = OverlordOfTheBoilerbilgesFactory.Create(_alice);
        var trigger = overlord.Abilities.OfType<TriggeredAbility>().First();

        // No SetChosenTargets call — resolving must not throw.
        Action resolve = () => { foreach (var e in trigger.Effects) e.Execute(); };
        resolve.Should().NotThrow();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void ResolveFirstTriggerAt(Creature overlord, object target)
    {
        var trigger = overlord.Abilities.OfType<TriggeredAbility>().First();
        trigger.SetChosenTargets(new IReadOnlyList<object>[] { new[] { target } });
        foreach (var effect in trigger.Effects)
            effect.Execute();
    }
}
