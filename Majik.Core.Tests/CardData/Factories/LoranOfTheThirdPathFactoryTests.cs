using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LoranOfTheThirdPathFactory"/> — Loran of the Third
/// Path (The Brothers' War, {2}{W}). Legendary Creature — Human Artificer
/// 2/1. Oracle text (verified against Scryfall):
///   "Vigilance
///    When Loran enters, destroy up to one target artifact or enchantment.
///    {T}: You and target opponent each draw a card."
///
/// Covers:
///   - Card identity (Legendary Creature — Human Artificer, {2}{W}, 2/1,
///     white, owner / controller) sourced from the embedded JSON definition.
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Vigilance keyword marker (CR 702.20).
///   - ETB destroy <see cref="TriggeredAbility"/> shape: 0..1 "up to one
///     target artifact or enchantment" request (CR 115.1a — optional).
///   - ETB resolve: agent-set artifact/enchantment target → destroyed.
///   - ETB resolve: no chosen target (declined "up to one") → clean no-op.
///   - ETB resolve: illegal pick (creature) → no destroy (CR 608.2b).
///   - {T} activated ability shape: tap-only cost + 1..1 "target opponent".
///   - {T} resolve: controller and chosen opponent each draw one card.
///   - {T} resolve: no opponent chosen → only the controller draws.
/// </summary>
[Trait("Color", "W")]
public class LoranOfTheThirdPathFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void Loran_Identity_LegendaryHumanArtificer_2_1_At2W()
    {
        var loran = LoranOfTheThirdPathFactory.Create(_alice);

        loran.Name.Should().Be("Loran of the Third Path");
        loran.ManaCost.Should().Be("{2}{W}");
        loran.HasType(CardType.Creature).Should().BeTrue();
        loran.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        loran.HasSubtype(CardSubtype.Human).Should().BeTrue();
        loran.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        loran.BasePower.Should().Be(2);
        loran.BaseToughness.Should().Be(1);
        CardColors.GetColors(loran).Should().Contain(ManaColor.White);
        loran.Owner.Should().BeSameAs(_alice);
        loran.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Loran_HasVigilanceKeyword()
    {
        var loran = LoranOfTheThirdPathFactory.Create(_alice);

        loran.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain("Vigilance");
    }

    // ── ETB destroy shape ───────────────────────────────────────────────

    [Fact]
    public void EtbTrigger_IsUpToOne_ArtifactOrEnchantment()
    {
        var loran = LoranOfTheThirdPathFactory.Create(_alice);

        var etb = loran.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);

        etb.TargetRequests.Should().HaveCount(1);
        etb.TargetRequests[0].MinTargets.Should().Be(0,
            "the printed text is 'destroy UP TO ONE target artifact or enchantment'");
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
        etb.TargetRequests[0].Description.Should().Contain("artifact or enchantment");
    }

    [Fact]
    public void EtbResolve_ChosenEnchantment_IsDestroyed()
    {
        var loran = LoranOfTheThirdPathFactory.Create(_alice);
        loran.SetController(_alice);

        var aura = new Enchantment("Bob's Aura", "{1}{W}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);

        var etb = loran.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aura } });

        ResolveAll(etb);

        aura.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(aura);
    }

    [Fact]
    public void EtbResolve_NoChosenTarget_IsCleanNoOp()
    {
        // "Up to one" declined (CR 115.1a) — even with a legal artifact on the
        // battlefield, no destroy happens because no target was chosen.
        var loran = LoranOfTheThirdPathFactory.Create(_alice);
        loran.SetController(_alice);

        var bauble = new Artifact("Bob's Bauble", "{1}");
        bauble.SetOwner(_bob);
        bauble.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var etb = loran.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        // No SetChosenTargets call — agent declined the optional target.

        ResolveAll(etb);

        bauble.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void EtbResolve_IllegalPick_Creature_NoDestroy()
    {
        var loran = LoranOfTheThirdPathFactory.Create(_alice);
        loran.SetController(_alice);

        var bear = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var etb = loran.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count > 0);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        ResolveAll(etb);

        bear.Zone.Should().Be(ZoneType.Battlefield);
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // ── {T} symmetric draw ──────────────────────────────────────────────

    [Fact]
    public void DrawAbility_TapOnlyCost_TargetsOpponent()
    {
        var loran = LoranOfTheThirdPathFactory.Create(_alice);

        var draw = loran.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);

        draw.Costs.Should().ContainSingle()
            .Which.Should().BeOfType<AdditionalCost>();
        draw.TargetRequests[0].MinTargets.Should().Be(1);
        draw.TargetRequests[0].MaxTargets.Should().Be(1);
        draw.TargetRequests[0].Description.Should().Contain("opponent");
    }

    [Fact]
    public void DrawResolve_ControllerAndChosenOpponent_EachDrawOne()
    {
        var loran = LoranOfTheThirdPathFactory.Create(_alice);
        loran.SetController(_alice);

        SeedLibrary(_alice, "A1", "A2");
        SeedLibrary(_bob, "B1", "B2");

        var draw = loran.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        draw.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().Equal("A1");
        _bob.Zones.Hand.GetCards().Select(c => c.Name).Should().Equal("B1");
    }

    [Fact]
    public void DrawResolve_NoOpponentChosen_OnlyControllerDraws()
    {
        var loran = LoranOfTheThirdPathFactory.Create(_alice);
        loran.SetController(_alice);

        SeedLibrary(_alice, "A1", "A2");
        SeedLibrary(_bob, "B1");

        var draw = loran.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        // No target chosen — only the controller (the non-targeted "you") draws.

        draw.Resolve();

        _alice.Zones.Hand.GetCards().Select(c => c.Name).Should().Equal("A1");
        _bob.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void ResolveAll(TriggeredAbility ability)
    {
        foreach (var e in ability.Effects)
        {
            e.Execute();
        }
    }

    private static void SeedLibrary(Player p, params string[] names)
    {
        foreach (var n in names)
        {
            var card = new Creature(n, "{1}", 1, 1);
            card.SetOwner(p);
            p.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }
    }
}
