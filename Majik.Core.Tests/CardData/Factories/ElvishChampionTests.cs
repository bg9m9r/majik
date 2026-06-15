using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ElvishChampionFactory"/>.
///
/// Covers ONLY Elvish Champion's unique behaviour (the contract test asserts
/// dispatch + well-formedness automatically):
/// - Identity ({1}{G}{G}, 2/2, Elf) — a single sanity assert on the JSON shape.
/// - Anthem buffs OTHER Elves +1/+1 and grants Forestwalk.
/// - Symmetric: opponents' Elves are pumped too ("Other Elf creatures", no
///   "you control" — allPlayers: true).
/// - No self-buff ("Other").
/// - Non-Elves are unaffected.
/// - LTB lifts the bonus + Forestwalk (IsActive gate).
/// </summary>
[Trait("Color", "G")]
public class ElvishChampionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void ElvishChampion_Identity()
    {
        var champ = ElvishChampionFactory.Create(_alice);

        champ.Name.Should().Be("Elvish Champion");
        champ.ManaCost.Should().Be("{1}{G}{G}");
        champ.HasType(CardType.Creature).Should().BeTrue();
        champ.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        champ.BasePower.Should().Be(2);
        champ.BaseToughness.Should().Be(2);
        champ.Owner.Should().BeSameAs(_alice);
        champ.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ElvishChampion_BuffsOtherElf_Plus1Plus1AndForestwalk()
    {
        var svc = new ContinuousEffectsService();

        var elf = MakeElf("Llanowar Elves", _alice, svc);
        var champ = ElvishChampionFactory.Create(_alice, svc);
        champ.Zone = ZoneType.Battlefield;
        champ.ActiveEffects = svc;

        elf.GetPower().Should().Be(2,
            "Elf gets +1/+1 from Elvish Champion (1 base + 1).");
        elf.GetToughness().Should().Be(2);
        HasForestwalk(elf).Should().BeTrue(
            "Elvish Champion grants Forestwalk to other Elves.");
    }

    [Fact]
    public void ElvishChampion_IsSymmetric_BuffsOpponentElf()
    {
        // "Other Elf creatures" — no "you control" qualifier, so the anthem is
        // symmetric (allPlayers: true). Opponents' Elves also benefit.
        var svc = new ContinuousEffectsService();

        var bobElf = MakeElf("Wirewood Herald", _bob, svc);
        var champ = ElvishChampionFactory.Create(_alice, svc);
        champ.Zone = ZoneType.Battlefield;
        champ.ActiveEffects = svc;

        bobElf.GetPower().Should().Be(2,
            "Elvish Champion is symmetric — Bob's Elf also gets +1/+1.");
        bobElf.GetToughness().Should().Be(2);
        HasForestwalk(bobElf).Should().BeTrue(
            "Elvish Champion grants Forestwalk even to opponent's Elves.");
    }

    [Fact]
    public void ElvishChampion_DoesNotSelfBuff()
    {
        var svc = new ContinuousEffectsService();

        var champ = ElvishChampionFactory.Create(_alice, svc);
        champ.Zone = ZoneType.Battlefield;
        champ.ActiveEffects = svc;

        champ.GetPower().Should().Be(2, "Elvish Champion says 'Other' — no self-buff.");
        champ.GetToughness().Should().Be(2);
    }

    [Fact]
    public void ElvishChampion_DoesNotBuff_NonElf()
    {
        var svc = new ContinuousEffectsService();

        var bear = MakeCreature("Grizzly Bears", _alice, power: 2, toughness: 2, CardSubtype.Bear, svc);
        var champ = ElvishChampionFactory.Create(_alice, svc);
        champ.Zone = ZoneType.Battlefield;
        champ.ActiveEffects = svc;

        bear.GetPower().Should().Be(2, "Elvish Champion only buffs Elves.");
        bear.GetToughness().Should().Be(2);
        HasForestwalk(bear).Should().BeFalse("non-Elves don't get Forestwalk.");
    }

    [Fact]
    public void ElvishChampion_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var elf = MakeElf("Llanowar Elves", _alice, svc);
        var champ = ElvishChampionFactory.Create(_alice, svc);
        champ.Zone = ZoneType.Battlefield;
        champ.ActiveEffects = svc;

        elf.GetPower().Should().Be(2);

        champ.SetZone(ZoneType.Graveyard);

        elf.GetPower().Should().Be(1, "bonus lifts when Elvish Champion leaves the battlefield.");
        elf.GetToughness().Should().Be(1);
        HasForestwalk(elf).Should().BeFalse(
            "Forestwalk grant lifts when Elvish Champion leaves the battlefield.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Creature MakeElf(string name, Player controller, ContinuousEffectsService svc)
        => MakeCreature(name, controller, power: 1, toughness: 1, CardSubtype.Elf, svc);

    private static Creature MakeCreature(
        string name,
        Player controller,
        int power,
        int toughness,
        CardSubtype subtype,
        ContinuousEffectsService svc)
    {
        var c = new Creature(name, "G", power, toughness, subtypes: new[] { subtype })
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        return c;
    }

    private static bool HasForestwalk(Creature c)
    {
        var chars = c.ActiveEffects?.Compute(c);
        if (chars is null)
        {
            return c.Abilities.OfType<Majik.Core.Abilities.KeywordAbility>()
                .Any(k => k.Keyword == "Forestwalk");
        }
        return chars.Keywords.Contains("Forestwalk");
    }
}
