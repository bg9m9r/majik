using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="IgnobleHierarchFactory"/>.
///
/// Covers:
/// - Identity (name, mana cost, Goblin + Shaman subtypes, 0/1,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Three mana abilities ({B}, {R}, {G}) present and produce correct mana.
/// - Exalted keyword marker attached (CR 702.90).
/// - Exalted trigger fires and pumps the solo attacker +1/+1 EOT.
/// - Two controlled creatures attack — no pump (CR 702.90b).
/// - Single-arg dispatcher path is a no-op pump (no attackers source).
/// </summary>
public class IgnobleHierarchFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetExaltedTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void IgnobleHierarch_Identity()
    {
        var c = IgnobleHierarchFactory.Create(_alice);

        c.Name.Should().Be("Ignoble Hierarch");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.BasePower.Should().Be(0);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IgnobleHierarch_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ignoble Hierarch", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Ignoble Hierarch");
        ((Creature)c).HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Shaman).Should().BeTrue();
    }

    // ── Mana abilities ────────────────────────────────────────────────────

    [Fact]
    public void IgnobleHierarch_HasThreeManaAbilities()
    {
        var c = IgnobleHierarchFactory.Create(_alice);

        var manaAbilities = c.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(3,
            "Ignoble Hierarch has {T}: Add {B}, {T}: Add {R}, and {T}: Add {G}.");
    }

    [Fact]
    public void IgnobleHierarch_GreenManaAbility_ProducesGreenMana()
    {
        var c = IgnobleHierarchFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        // ManaCost.ToString() returns "G" (no braces) — see ManaCost.cs line 242.
        var greenAbility = c.Abilities.OfType<ManaAbility>()
            .FirstOrDefault(a => a.ManaGenerated?.ToString() == "G");

        greenAbility.Should().NotBeNull("{T}: Add {G} must be present.");
        greenAbility!.CanActivate().Should().BeTrue("creature is untapped.");

        var mana = greenAbility.Activate();
        mana.ToString().Should().Be("G",
            "activating the {G} ability produces one green mana (ManaCost.ToString omits braces).");
        c.IsTapped.Should().BeTrue("activating the {T} mana ability taps Ignoble Hierarch.");
    }

    [Fact]
    public void IgnobleHierarch_ManaAbilitiesCoverBlackAndRed()
    {
        var c = IgnobleHierarchFactory.Create(_alice);

        // ManaCost.ToString() returns bare letters: "B", "R", "G" — no braces.
        var manaStrings = c.Abilities.OfType<ManaAbility>()
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        manaStrings.Should().BeEquivalentTo(new[] { "B", "G", "R" },
            "Ignoble Hierarch taps for B, R, or G (ManaCost.ToString omits braces).");
    }

    // ── Exalted keyword marker ────────────────────────────────────────────

    [Fact]
    public void IgnobleHierarch_HasExaltedKeywordMarker()
    {
        var c = IgnobleHierarchFactory.Create(_alice);

        var exalted = c.Abilities.OfType<KeywordAbility>()
            .FirstOrDefault(k => k.Keyword == "Exalted");

        exalted.Should().NotBeNull("Exalted keyword marker must be present (CR 702.90).");
    }

    // ── Exalted trigger — single attacker ────────────────────────────────

    [Fact]
    public void IgnobleHierarch_Exalted_SoloAttacker_GetsPumped()
    {
        // CR 702.90 — attacker attacks alone; should get +1/+1 EOT.
        var svc = new ContinuousEffectsService();

        var attacker = MakeCreature(_alice, "Grizzly Bears");
        attacker.ActiveEffects = svc;

        var attackers = new List<Creature> { attacker };

        var hierarch = IgnobleHierarchFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        hierarch.SetZone(ZoneType.Battlefield);
        hierarch.ActiveEffects = svc;

        var trigger = GetExaltedTrigger(hierarch);
        // Verify condition fires for attacker controlled by Alice.
        trigger.IsTriggered(new CreatureAttacksEvent(attacker, _bob)).Should().BeTrue(
            "the exalted trigger fires whenever any creature Alice controls attacks.");

        // Execute the effect — at this point exactly 1 controlled attacker exists.
        foreach (var e in trigger.Effects) e.Execute();

        attacker.GetPower().Should().Be(2 + 1,
            "Exalted gives the solo attacker +1/+1 until end of turn.");
        attacker.GetToughness().Should().Be(2 + 1);
    }

    // ── Exalted trigger — multiple attackers ─────────────────────────────

    [Fact]
    public void IgnobleHierarch_Exalted_TwoAttackers_NoPump()
    {
        // CR 702.90b — "attacks alone" requires no other controlled attackers.
        var svc = new ContinuousEffectsService();

        var attacker1 = MakeCreature(_alice, "Bear Alpha");
        var attacker2 = MakeCreature(_alice, "Bear Beta");
        attacker1.ActiveEffects = svc;
        attacker2.ActiveEffects = svc;

        var attackers = new List<Creature> { attacker1, attacker2 };

        var hierarch = IgnobleHierarchFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        hierarch.SetZone(ZoneType.Battlefield);
        hierarch.ActiveEffects = svc;

        var trigger = GetExaltedTrigger(hierarch);
        trigger.IsTriggered(new CreatureAttacksEvent(attacker1, _bob)).Should().BeTrue();

        foreach (var e in trigger.Effects) e.Execute();

        attacker1.GetPower().Should().Be(2,
            "two attackers means the creature didn't attack alone — no pump.");
        attacker1.GetToughness().Should().Be(2);
        attacker2.GetPower().Should().Be(2);
        attacker2.GetToughness().Should().Be(2);
    }

    // ── Single-arg dispatcher path ────────────────────────────────────────

    [Fact]
    public void IgnobleHierarch_SingleArgPath_NoOpPumpBody()
    {
        // The single-arg path doesn't wire an attackers source — the pump
        // body short-circuits and attackers remain at base P/T.
        var svc = new ContinuousEffectsService();

        var attacker = MakeCreature(_alice, "Grizzly Bears");
        attacker.ActiveEffects = svc;

        var hierarch = IgnobleHierarchFactory.Create(_alice);
        hierarch.SetZone(ZoneType.Battlefield);
        hierarch.ActiveEffects = svc;

        var trigger = GetExaltedTrigger(hierarch);
        foreach (var e in trigger.Effects) e.Execute();

        attacker.GetPower().Should().Be(2,
            "no attackers source — pump body is a no-op (shape-only path).");
        attacker.GetToughness().Should().Be(2);
    }
}
