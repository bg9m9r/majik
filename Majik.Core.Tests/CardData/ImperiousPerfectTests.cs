using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ImperiousPerfectFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Elf + Warrior subtypes, 2/2,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - LordStaticEffect: other controller-Elves get +1/+1.
/// - Imperious Perfect itself doesn't double-stack +1/+1 (includeSelf:
///   false).
/// - Opponent's Elf is NOT pumped (controller-scoped).
/// - {G}, {T}: create a 1/1 green Elf Warrior token — activated ability
///   identity (cost vector, effect), token spec (P/T, subtypes, colour),
///   controller routing.
/// </summary>
public class ImperiousPerfectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeElf(Player owner, string name = "Llanowar Elves")
    {
        var c = new Creature(name, "G", 1, 1, subtypes: new[] { CardSubtype.Elf });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void ImperiousPerfect_Identity()
    {
        var c = ImperiousPerfectFactory.Create(_alice);

        c.Name.Should().Be("Imperious Perfect");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ImperiousPerfect_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Imperious Perfect", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Imperious Perfect");
        ((Creature)c).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Warrior).Should().BeTrue();
    }

    // ── Lord static — +1/+1 to other controller-Elves ────────────────────

    [Fact]
    public void ImperiousPerfect_PumpsOtherControllerElf_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();

        var otherElf = MakeElf(_alice);
        otherElf.ActiveEffects = svc;

        var perfect = ImperiousPerfectFactory.Create(_alice, svc);
        perfect.SetZone(ZoneType.Battlefield);
        perfect.ActiveEffects = svc;

        otherElf.GetPower().Should().Be(2,
            "other controller-Elves get +1/+1 from Imperious Perfect's lord static.");
        otherElf.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ImperiousPerfect_DoesNotSelfPump_IncludeSelfFalse()
    {
        var svc = new ContinuousEffectsService();

        var perfect = ImperiousPerfectFactory.Create(_alice, svc);
        perfect.SetZone(ZoneType.Battlefield);
        perfect.ActiveEffects = svc;

        perfect.GetPower().Should().Be(2,
            "includeSelf:false — Perfect's own +1/+1 static doesn't stack on itself.");
        perfect.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ImperiousPerfect_DoesNotPump_OpponentElf()
    {
        var svc = new ContinuousEffectsService();

        var oppElf = MakeElf(_bob);
        oppElf.ActiveEffects = svc;

        var perfect = ImperiousPerfectFactory.Create(_alice, svc);
        perfect.SetZone(ZoneType.Battlefield);
        perfect.ActiveEffects = svc;

        oppElf.GetPower().Should().Be(1,
            "Lord static is scoped to the controller (CR 109.5 — 'you').");
    }

    [Fact]
    public void TwoElfLords_StackPlus2Plus2_OnOtherElf()
    {
        var svc = new ContinuousEffectsService();

        var otherElf = MakeElf(_alice);
        otherElf.ActiveEffects = svc;

        var perfect = ImperiousPerfectFactory.Create(_alice, svc);
        perfect.SetZone(ZoneType.Battlefield);
        perfect.ActiveEffects = svc;

        var archdruid = ElvishArchdruidFactory.Create(_alice, svc);
        archdruid.SetZone(ZoneType.Battlefield);
        archdruid.ActiveEffects = svc;

        otherElf.GetPower().Should().Be(3,
            "Imperious Perfect + Elvish Archdruid stack +1/+1 each — 1 base + 2 = 3.");
        otherElf.GetToughness().Should().Be(3);
    }

    // ── Activated ability — {G}, {T}: create token ────────────────────────

    [Fact]
    public void ImperiousPerfect_HasActivatedAbility_WithManaAndTapCosts()
    {
        var c = ImperiousPerfectFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1,
            "Imperious Perfect has exactly one activated ability: {G}, {T}: create token.");

        var ability = activated[0];
        ability.Costs.Should().HaveCount(2,
            "Cost vector is {G} (ManaCostCost) + {T} (AdditionalCost.Tap).");
        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1,
            "ManaCostCost for the {G} payment.");
        ability.Costs.OfType<AdditionalCost>().Should().HaveCount(1,
            "AdditionalCost for the {T} tap.");
    }

    [Fact]
    public void ImperiousPerfect_ActivatedAbility_CreatesElfWarriorToken()
    {
        var c = ImperiousPerfectFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var beforeBattlefieldCount = _alice.Zones.Battlefield.GetCards().Count();

        // Execute the effect body directly (cost payment is exercised by
        // separate ActivatedAbility tests; this assertion is on the
        // post-resolution state).
        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        var battlefield = _alice.Zones.Battlefield.GetCards().ToList();
        battlefield.Count.Should().Be(beforeBattlefieldCount + 1,
            "one token entered the controller's battlefield.");

        var token = battlefield.OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Elf Warrior");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        token.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice,
            "token is created under Imperious Perfect's controller.");
    }

    [Fact]
    public void ImperiousPerfect_TokenIsGreen()
    {
        var c = ImperiousPerfectFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var eff in ability.Effects)
        {
            eff.Execute();
        }

        var token = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Single(c => c.IsToken && c.Name == "Elf Warrior");

        var colors = CardColors.GetColors(token).ToList();
        colors.Should().ContainSingle().And.Contain(ManaColor.Green,
            "CR 111.4 — printed 1/1 GREEN Elf Warrior creature token.");
    }
}
