using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Sterling Grove (Invasion, {G}{W}, Enchantment).
///
/// "Other enchantments you control have shroud. (They can't be the targets
///  of spells or abilities.)
///  {1}, Sacrifice Sterling Grove: Search your library for an enchantment
///  card, reveal that card, and put it on top of your library. Then shuffle."
///
/// Coverage:
///  - Identity (name / type / mana cost) + NamedCardFactory dispatch.
///  - Static shroud grant: other enchantments controller controls receive
///    "Shroud" in their characteristic keyword set (CR 702.18, CR 613.1f
///    Layer 6).
///  - Sterling Grove itself does NOT get Shroud ("Other").
///  - Opponent's enchantments do NOT get Shroud ("you control").
///  - Non-enchantment permanents (creatures, lands, artifacts) do NOT get
///    Shroud.
///  - LTB lifts the bonus (IsActive battlefield-gate).
///  - Activated ability identity ({1} + sacrifice cost).
///  - Activated ability resolves: sacrifices self, tutors an enchantment,
///    places pick on top of library, shuffles.
///  - Empty library / no enchantment in library → still shuffle (CR 701.20a)
///    but no card moved.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class SterlingGroveTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ─── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var grove = SterlingGroveFactory.Create(_alice);

        grove.Name.Should().Be("Sterling Grove");
        grove.ManaCost.Should().Be("{G}{W}");
        grove.HasType(CardType.Enchantment).Should().BeTrue();
        grove.Owner.Should().BeSameAs(_alice);
        grove.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SterlingGrove()
    {
        var card = NamedCardFactory.Create("Sterling Grove", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Sterling Grove");
        card.ManaCost.Should().Be("{G}{W}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    // ─── Static shroud grant ────────────────────────────────────────────────

    [Fact]
    public void Static_GrantsShroudToOtherEnchantments_ControllerControls()
    {
        var svc = new ContinuousEffectsService();

        var grove = SterlingGroveFactory.Create(_alice, svc);
        grove.Zone = ZoneType.Battlefield;

        // Another enchantment Alice controls — should receive Shroud.
        var rest = new Enchantment("Rest in Peace", "{1}{W}");
        rest.SetOwner(_alice); rest.SetController(_alice);
        rest.Zone = ZoneType.Battlefield;

        var chars = svc.Compute(rest);
        chars.Keywords.Should().Contain("Shroud",
            "Sterling Grove grants Shroud to other enchantments its controller controls.");
    }

    [Fact]
    public void Static_DoesNotSelfBuff_SterlingGrove()
    {
        // "Other enchantments" — Sterling Grove never grants itself Shroud.
        var svc = new ContinuousEffectsService();

        var grove = SterlingGroveFactory.Create(_alice, svc);
        grove.Zone = ZoneType.Battlefield;

        var chars = svc.Compute(grove);
        chars.Keywords.Should().NotContain("Shroud",
            "Sterling Grove says 'Other' — no self-grant.");
    }

    [Fact]
    public void Static_DoesNotGrantToOpponentsEnchantments()
    {
        // "you control" — Bob's enchantments are unaffected by Alice's
        // Sterling Grove. (CR 109.4 — "you" refers to the controller.)
        var svc = new ContinuousEffectsService();

        var grove = SterlingGroveFactory.Create(_alice, svc);
        grove.Zone = ZoneType.Battlefield;

        var bobEnch = new Enchantment("Oblivion Ring", "{2}{W}");
        bobEnch.SetOwner(_bob); bobEnch.SetController(_bob);
        bobEnch.Zone = ZoneType.Battlefield;

        var chars = svc.Compute(bobEnch);
        chars.Keywords.Should().NotContain("Shroud",
            "Sterling Grove only protects 'enchantments you control'.");
    }

    [Fact]
    public void Static_DoesNotGrantShroudToNonEnchantmentPermanents()
    {
        // "enchantments" — creatures / lands / artifacts Alice controls
        // are unaffected. Sterling Grove buffs the enchantment type only.
        var svc = new ContinuousEffectsService();

        var grove = SterlingGroveFactory.Create(_alice, svc);
        grove.Zone = ZoneType.Battlefield;

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice); bear.SetController(_alice);
        bear.Zone = ZoneType.Battlefield;
        bear.ActiveEffects = svc;

        var land = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        land.SetOwner(_alice); land.SetController(_alice);
        land.Zone = ZoneType.Battlefield;

        var artifact = new Artifact("Sol Ring", "{1}");
        artifact.SetOwner(_alice); artifact.SetController(_alice);
        artifact.Zone = ZoneType.Battlefield;

        svc.Compute((Permanent)bear).Keywords.Should().NotContain("Shroud");
        svc.Compute(land).Keywords.Should().NotContain("Shroud");
        svc.Compute(artifact).Keywords.Should().NotContain("Shroud");
    }

    [Fact]
    public void Static_LTB_LiftsShroudGrant()
    {
        // CR 613.1g + IsActive battlefield-gate — when Sterling Grove
        // leaves the battlefield, the static keyword grant lifts
        // automatically without an explicit unregister.
        var svc = new ContinuousEffectsService();

        var grove = SterlingGroveFactory.Create(_alice, svc);
        grove.Zone = ZoneType.Battlefield;

        var rest = new Enchantment("Rest in Peace", "{1}{W}");
        rest.SetOwner(_alice); rest.SetController(_alice);
        rest.Zone = ZoneType.Battlefield;

        svc.Compute(rest).Keywords.Should().Contain("Shroud");

        // Sterling Grove leaves the battlefield → grant lifts.
        grove.Zone = ZoneType.Graveyard;

        svc.Compute(rest).Keywords.Should().NotContain("Shroud",
            "Shroud grant lifts when Sterling Grove leaves the battlefield.");
    }

    [Fact]
    public void Static_GrantsShroudToEnchantmentCreature_DualType()
    {
        // Dual-typed enchantment creatures (e.g. Bestow auras attached as
        // creatures, theros-style enchantment creatures) satisfy the
        // Enchantment type predicate and receive Shroud.
        var svc = new ContinuousEffectsService();

        var grove = SterlingGroveFactory.Create(_alice, svc);
        grove.Zone = ZoneType.Battlefield;

        // Synthesize an enchantment creature by adding the Enchantment
        // type explicitly to a Creature instance (mirrors how Theros
        // gods / Nyx-born creatures appear in the engine). Card.AddCardType
        // is the internal seam multi-type factories use post-construction.
        var nyxborn = new Creature("Nyxborn Rollicker", "{R}", 1, 1);
        nyxborn.AddCardType(CardType.Enchantment);
        nyxborn.SetOwner(_alice); nyxborn.SetController(_alice);
        nyxborn.Zone = ZoneType.Battlefield;
        nyxborn.ActiveEffects = svc;

        var chars = svc.Compute((Permanent)nyxborn);
        chars.Keywords.Should().Contain("Shroud",
            "Enchantment creatures satisfy the Enchantment type predicate.");
    }

    // ─── Activated ability ──────────────────────────────────────────────────

    [Fact]
    public void ActivatedAbility_HasManaCostAndSacrificeCost()
    {
        var grove = SterlingGroveFactory.Create(_alice);

        var ability = grove.Abilities.OfType<ActivatedAbility>().FirstOrDefault();
        ability.Should().NotBeNull("Sterling Grove ships its activated tutor ability");

        // The activated ability declares two costs: {1} mana + sacrifice
        // self. The exact cost-type surfacing depends on engine internals;
        // assert presence of both AdditionalCost.Sacrifice + a ManaCostCost.
        ability!.Costs.Should().Contain(c => c is Majik.Core.Costs.ManaCostCost,
            "activation requires {1}.");
        ability.Costs.Should().Contain(c => c is Majik.Core.Costs.AdditionalCost,
            "activation requires sacrificing Sterling Grove.");
    }

    [Fact]
    public void ActivatedAbility_Resolves_SacrificesSelf_TutorsEnchantmentToTopOfLibrary()
    {
        // End-to-end: cards laid out with one eligible enchantment and
        // one ineligible (creature + land). Run the resolve closure
        // directly (the cost layer is shape-only on Sacrifice; the
        // closure handles the visible state change to match CR 701.16).
        var grove = SterlingGroveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(grove);
        grove.Zone = ZoneType.Battlefield;

        var oblivion = new Enchantment("Oblivion Ring", "{2}{W}");
        oblivion.SetOwner(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        var forest = new Land("Forest",
            new[] { CardSupertype.Basic },
            new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);
        _alice.Zones.Library.AddCard(oblivion);
        _alice.Zones.Library.AddCard(forest);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        // CR 701.20a — shuffle wired; seed so the post-search shuffle of
        // the remaining (Bear, Forest) pile is deterministic.
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1));

        var ability = grove.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in ability.Effects) fx.Execute();

        // Self-sacrificed — Sterling Grove must be in the graveyard.
        grove.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(c => c.Name == "Sterling Grove");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(c => c.Name == "Sterling Grove");

        // Library: pick (Oblivion Ring) on top; remaining cards shuffled
        // below it.
        var libCards = _alice.Zones.Library.GetCards().ToList();
        libCards.Should().HaveCount(3);
        libCards[0].Name.Should().Be("Oblivion Ring");
        libCards.Skip(1).Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Grizzly Bears", "Forest" });

        // Hand untouched — tutor lands on top of library, not in hand.
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ActivatedAbility_PublishesLibraryShuffledEvent()
    {
        // CR 701.20a — even when no enchantment exists in the library
        // the shuffle still fires (the printed oracle says "Then shuffle"
        // unconditionally; this matches Mystical / Worldly Tutor's posture).
        var grove = SterlingGroveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(grove);
        grove.Zone = ZoneType.Battlefield;

        // Library with no enchantments — candidate list is empty, but
        // CR 701.20a still shuffles.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bear);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1));
        var bus = new EventBus();
        LibraryShuffledEvent? captured = null;
        bus.Subscribe<LibraryShuffledEvent>(e => captured = e);
        EventBusRegistry.Set(_alice, bus);
        try
        {
            var ability = grove.Abilities.OfType<ActivatedAbility>().Single();
            foreach (var fx in ability.Effects) fx.Execute();

            captured.Should().NotBeNull(
                "no-enchantment-in-library case still triggers CR 701.20a shuffle.");
            captured!.Player.Should().BeSameAs(_alice);
            captured.Reason.Should().Be("sterling-grove");

            // Sterling Grove still sacrificed.
            grove.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            EventBusRegistry.Clear();
            GameRandomRegistry.Clear();
        }
    }

    [Fact]
    public void ActivatedAbility_SacrificeFirst_CannotTutorSelf()
    {
        // The sacrifice happens before the search, so Sterling Grove
        // cannot tutor itself out of the library on a hypothetical
        // double-resolve (it has already left the battlefield AND was
        // never in the library to begin with).
        var grove = SterlingGroveFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(grove);
        grove.Zone = ZoneType.Battlefield;

        var oblivion = new Enchantment("Oblivion Ring", "{2}{W}");
        oblivion.SetOwner(_alice);
        _alice.Zones.Library.AddCard(oblivion);

        AgentRegistry.Set(_alice, new DeterministicBotAgent());
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1));

        var ability = grove.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var fx in ability.Effects) fx.Execute();

        // Top-of-library is Oblivion Ring, not Sterling Grove.
        var libTop = _alice.Zones.Library.GetCards().First();
        libTop.Name.Should().Be("Oblivion Ring");
        // Sterling Grove sits in the graveyard, never in the library.
        _alice.Zones.Library.GetCards().Should().NotContain(c => c.Name == "Sterling Grove");
        _alice.Zones.Graveyard.GetCards().Should().Contain(c => c.Name == "Sterling Grove");
    }
}
