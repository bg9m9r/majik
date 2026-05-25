using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="EmryLurkerOfTheLochFactory"/>.
///
/// Card: Emry, Lurker of the Loch — Legendary Creature — Merfolk Wizard
/// {4}{U} (Throne of Eldraine).
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    When Emry enters, mill four cards.
///    {T}: Choose target artifact card in your graveyard. You may cast
///    that card this turn."
///
/// Covers:
///   - Identity (name, type, mana cost, supertype Legendary, subtypes
///     Merfolk + Wizard, 1/2, owner/controller).
///   - NamedCardFactory dispatch returns a Creature shell with all
///     abilities attached.
///   - Affinity for artifacts (CR 702.40) — generic reduced by 1 per
///     controlled artifact; coloured pip {U} untouched; floor-at-zero.
///   - ETB trigger mills 4 cards from controller's library to graveyard.
///   - Activated {T} ability stamps GrantRuntimeGraveyardCast on the
///     first artifact card in controller's graveyard, with that card's
///     printed mana cost.
///   - Activated ability no-op when no artifact in graveyard.
///   - Tap cost is wired (AdditionalCost.Tap).
///   - Activated ability does not stamp non-artifact graveyard cards.
/// </summary>
public class EmryLurkerOfTheLochTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    // -------------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void Emry_Identity()
    {
        var c = EmryLurkerOfTheLochFactory.Create(_alice);

        c.Name.Should().Be("Emry, Lurker of the Loch");
        c.ManaCost.Should().Be("{4}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Emry is Legendary");
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue("Merfolk is a printed subtype");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Wizard is a printed subtype");
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Emry_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Emry, Lurker of the Loch", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Emry, Lurker of the Loch");
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1,
            "the Affinity-for-artifacts cost reducer is attached");
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB mill-4 trigger is attached");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the tap → grant grave-cast activated ability is attached");
    }

    [Fact]
    public void Emry_AbilityList_OneCostReducer_OneTrigger_OneActivated()
    {
        var c = EmryLurkerOfTheLochFactory.Create(_alice);

        c.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------------
    // Affinity for artifacts (CR 702.40)
    // -------------------------------------------------------------------------

    [Fact]
    public void Affinity_NoArtifactsControlled_FullPrintedCost()
    {
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);
        // Emry sits in hand pre-cast; not on battlefield yet.
        _alice.Zones.Hand.AddCard(emry);
        emry.SetZone(ZoneType.Hand);

        var effective = CostReduction.GetEffectiveCost(emry, _alice);

        effective.Generic.Should().Be(4, "no artifacts controlled — no Affinity discount");
        effective.Blue.Should().Be(1, "coloured pip untouched");
        effective.TotalValue.Should().Be(5);
    }

    [Fact]
    public void Affinity_ThreeArtifactsControlled_GenericReducedByThree()
    {
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(emry);
        emry.SetZone(ZoneType.Hand);

        // Three artifacts on Alice's battlefield.
        for (var i = 0; i < 3; i++)
        {
            var bauble = new Artifact($"Artifact {i}", "{0}");
            bauble.SetOwner(_alice);
            PutOnBattlefield(_alice, bauble);
        }

        var effective = CostReduction.GetEffectiveCost(emry, _alice);

        effective.Generic.Should().Be(1, "{4} generic reduced by 3 → {1}");
        effective.Blue.Should().Be(1, "coloured pip untouched (CR 117.7c)");
        effective.TotalValue.Should().Be(2);
    }

    [Fact]
    public void Affinity_FiveArtifactsControlled_FloorAtZero_ColouredPipUntouched()
    {
        // Five artifacts → {4} generic floors at 0; {U} pip remains.
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(emry);
        emry.SetZone(ZoneType.Hand);

        for (var i = 0; i < 5; i++)
        {
            var art = new Artifact($"Artifact {i}", "{0}");
            art.SetOwner(_alice);
            PutOnBattlefield(_alice, art);
        }

        var effective = CostReduction.GetEffectiveCost(emry, _alice);

        effective.Generic.Should().Be(0, "{4} reduced by 5 → floor at 0 (CR 117.7c)");
        effective.Blue.Should().Be(1, "coloured pip never reduced");
        effective.TotalValue.Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // ETB mill-4 (CR 603.1 + CR 701.13)
    // -------------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_Resolve_MillsFourCardsFromControllersLibrary()
    {
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);

        // Seed library with 10 distinct vanilla cards so we can observe
        // the top-4 move to graveyard.
        for (var i = 0; i < 10; i++)
        {
            var c = new Card($"Lib {i}", "");
            c.SetOwner(_alice);
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
        }

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();

        var etb = emry.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().HaveCount(
            EmryLurkerOfTheLochFactory.MillOnEnterCount,
            "Emry's ETB mills exactly 4 cards");
        _alice.Zones.Library.GetCards().Should().HaveCount(6,
            "10 starting cards − 4 milled = 6 remaining in library");
    }

    // -------------------------------------------------------------------------
    // Activated {T}: grant grave-cast on target artifact in graveyard
    // -------------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_HasTapCost()
    {
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);

        var activated = emry.Abilities.OfType<ActivatedAbility>().Single();
        activated.Costs.Should().ContainSingle(c => c is AdditionalCost,
            "the only printed cost on Emry's activated ability is the tap symbol");
    }

    [Fact]
    public void ActivatedAbility_Resolve_StampsGraveyardCastGrantOnArtifactCard()
    {
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);
        PutOnBattlefield(_alice, emry);

        // Seed graveyard with an artifact card (e.g. Mishra's Bauble {0}).
        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(_alice);
        bauble.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble);

        bauble.RuntimeGraveyardCastCost.Should().BeNull("nothing stamped before activation");

        var activated = emry.Abilities.OfType<ActivatedAbility>().Single();
        activated.Resolve();

        bauble.RuntimeGraveyardCastCost.Should().NotBeNull(
            "Emry's activated ability stamps a grave-cast grant on the chosen artifact");
        bauble.RuntimeGraveyardCastCost!.TotalValue.Should().Be(0,
            "Bauble's printed mana cost is {0} — total value 0");
    }

    [Fact]
    public void ActivatedAbility_Resolve_UsesPrintedManaCostForGrant()
    {
        // Grant cost = the chosen card's PRINTED mana cost (matches
        // Yawgmoth's-Will-shape).
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);
        PutOnBattlefield(_alice, emry);

        var pithDriller = new Artifact("Pith Driller", "{3}");
        pithDriller.SetOwner(_alice);
        pithDriller.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(pithDriller);

        var activated = emry.Abilities.OfType<ActivatedAbility>().Single();
        activated.Resolve();

        pithDriller.RuntimeGraveyardCastCost.Should().NotBeNull();
        pithDriller.RuntimeGraveyardCastCost!.TotalValue.Should().Be(3,
            "Pith Driller's printed mana value is 3 — stamped grant matches");
    }

    [Fact]
    public void ActivatedAbility_Resolve_NoArtifactInGraveyard_NoOp()
    {
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);
        PutOnBattlefield(_alice, emry);

        // Graveyard has only a non-artifact card (Lightning Bolt).
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var activated = emry.Abilities.OfType<ActivatedAbility>().Single();
        activated.Resolve();

        bolt.RuntimeGraveyardCastCost.Should().BeNull(
            "Lightning Bolt is not an artifact — Emry's grant doesn't apply");
    }

    [Fact]
    public void ActivatedAbility_Resolve_OnlyArtifact_PicksThatCard()
    {
        // Graveyard has one artifact + one non-artifact. The picker
        // should stamp the artifact and leave the non-artifact alone.
        var emry = EmryLurkerOfTheLochFactory.Create(_alice);
        PutOnBattlefield(_alice, emry);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var bauble = new Artifact("Mishra's Bauble", "{0}");
        bauble.SetOwner(_alice);
        bauble.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble);

        var activated = emry.Abilities.OfType<ActivatedAbility>().Single();
        activated.Resolve();

        bolt.RuntimeGraveyardCastCost.Should().BeNull("non-artifact — skipped");
        bauble.RuntimeGraveyardCastCost.Should().NotBeNull("artifact — stamped");
    }
}
