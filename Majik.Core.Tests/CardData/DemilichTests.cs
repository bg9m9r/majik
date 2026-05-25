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
/// Tests for <see cref="DemilichFactory"/>.
///
/// Demilich (engine-spec, MODERN_COVERAGE row #10, {3}{U}{U}{U}):
///   Creature — Zombie Wizard 4/3. Flying.
///   This spell costs {U} less to cast for each instant or sorcery card in
///   your graveyard.
///   When you cast this spell, exile two instants or sorceries from your
///   graveyard.
///
/// Covers:
///   - Card identity: Zombie Wizard 4/3, mana cost {3}{U}{U}{U}, Flying.
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Cost reduction at 0 / 3 / 5 instants+sorceries in the caster's
///     graveyard (floor at the three blue pips per CR 117.7c).
///   - On-cast trigger structure (single TriggeredAbility, active on
///     stack).
///   - On-cast trigger effect: exiles the first two instant / sorcery
///     cards from the caster's graveyard via the deterministic fallback
///     when no agent is registered.
/// </summary>
public class DemilichTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Demilich_IsZombieWizard_4_3_WithFlying_At_3UUU()
    {
        var demi = DemilichFactory.Create(_alice);

        demi.Name.Should().Be("Demilich");
        demi.ManaCost.Should().Be("{3}{U}{U}{U}");
        demi.HasType(CardType.Creature).Should().BeTrue();
        demi.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        demi.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        demi.BasePower.Should().Be(4);
        demi.BaseToughness.Should().Be(3);
        demi.Owner.Should().BeSameAs(_alice);
        demi.Controller.Should().BeSameAs(_alice);

        demi.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flying");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Demilich()
    {
        var card = NamedCardFactory.Create("Demilich", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Demilich");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        card.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(4);
        ((Creature)card).BaseToughness.Should().Be(3);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flying");
        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Demilich_EmptyGraveyard_PaysFullCost()
    {
        // 0 instants / sorceries in graveyard → no reduction. Pays
        // {3}{U}{U}{U}: generic = 3, U pips = 3.
        var demi = DemilichFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(demi, _alice);

        effective.Generic.Should().Be(3);
        effective.Blue.Should().Be(3);
    }

    [Fact]
    public void Demilich_ThreeInstantsOrSorceriesInGraveyard_ReducesToUUU()
    {
        // 3 instants / sorceries in graveyard → reduction by 3 generic.
        // Pays {U}{U}{U}: generic = 0, U pips = 3.
        var demi = DemilichFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 2, sorceries: 1);

        var effective = CostReduction.GetEffectiveCost(demi, _alice);

        effective.Generic.Should().Be(0);
        effective.Blue.Should().Be(3);
    }

    [Fact]
    public void Demilich_FiveInstantsOrSorceriesInGraveyard_FloorsAtColouredPips()
    {
        // 5 instants/sorceries → reduction = 5 generic. Printed generic is
        // 3, so reduction floors at 0 generic. Coloured pips untouched
        // (CR 117.7c) — still pays {U}{U}{U}.
        var demi = DemilichFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 3, sorceries: 2);

        var effective = CostReduction.GetEffectiveCost(demi, _alice);

        effective.Generic.Should().Be(0);
        effective.Blue.Should().Be(3);
    }

    [Fact]
    public void Demilich_NonInstantSorceryGraveyardCards_DoNotReduce()
    {
        // Two creatures + a land in graveyard — none should count toward
        // the reduction.
        var demi = DemilichFactory.Create(_alice);
        AddToGraveyard(_alice, new Creature("Bear A", "1G", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear B", "1G", 2, 2));
        AddToGraveyard(_alice, new Land("Plains", new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains }));

        var effective = CostReduction.GetEffectiveCost(demi, _alice);

        effective.Generic.Should().Be(3, "non-instant/sorcery cards don't trigger Demilich's reduction");
        effective.Blue.Should().Be(3);
    }

    [Fact]
    public void Demilich_OnCastTrigger_HasStackActiveZone()
    {
        var demi = DemilichFactory.Create(_alice);

        var triggers = demi.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var t = triggers[0];
        t.ActiveZones.Should().Contain(ZoneType.Stack,
            "the on-cast trigger fires while Demilich is on the stack (SpellCastEvent)");
    }

    [Fact]
    public void Demilich_OnCastEffect_Exiles_TwoInstantsOrSorceries_FromGraveyard_Deterministic()
    {
        // Seed graveyard with 3 instants / sorceries + 1 creature. With no
        // agent registered, the deterministic fallback should pick the
        // first two eligible cards and exile them.
        var demi = DemilichFactory.Create(_alice);

        var bolt = new Instant("Lightning Bolt", "{R}");
        var ponder = new Instant("Ponder", "{U}");
        var rite = new Sorcery("Cabal Ritual", "{1}{B}");
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);

        AddToGraveyard(_alice, bolt);
        AddToGraveyard(_alice, ponder);
        AddToGraveyard(_alice, rite);
        AddToGraveyard(_alice, bear);

        var trigger = demi.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects)
        {
            effect.Execute();
        }

        // First two eligible (instants/sorceries) — bolt + ponder — go to
        // exile. The sorcery and the creature stay in the graveyard.
        bolt.Zone.Should().Be(ZoneType.Exile);
        ponder.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(new ICard[] { bolt, ponder });

        rite.Zone.Should().Be(ZoneType.Graveyard,
            "only the first two eligible cards are picked by the deterministic fallback");
        bear.Zone.Should().Be(ZoneType.Graveyard,
            "the creature was never eligible for the on-cast exile");
    }

    [Fact]
    public void Demilich_OnCastEffect_NoEligibleCards_NoOp()
    {
        // Only a creature in graveyard → no instants/sorceries → effect
        // exits cleanly without exiling anything.
        var demi = DemilichFactory.Create(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        AddToGraveyard(_alice, bear);

        var trigger = demi.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects)
        {
            effect.Execute();
        }

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Demilich_OnCastEffect_OneEligibleCard_ExilesOne()
    {
        // Only one eligible card in graveyard → loop short-circuits after
        // the first pick exhausts the candidate list.
        var demi = DemilichFactory.Create(_alice);
        var bolt = new Instant("Lightning Bolt", "{R}");
        AddToGraveyard(_alice, bolt);

        var trigger = demi.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects)
        {
            effect.Execute();
        }

        bolt.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().ContainSingle().Which.Should().BeSameAs(bolt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddToGraveyard(Player p, ICard card)
    {
        if (card is Card concrete)
        {
            concrete.SetOwner(p);
            concrete.SetZone(ZoneType.Graveyard);
        }
        p.Zones.Graveyard.AddCard(card);
    }

    private static void SeedGraveyardWithSpells(Player p, int instants, int sorceries)
    {
        for (var i = 0; i < instants; i++)
        {
            var c = new Instant($"Inst{i}", "{U}");
            AddToGraveyard(p, c);
        }
        for (var i = 0; i < sorceries; i++)
        {
            var c = new Sorcery($"Sorc{i}", "{U}");
            AddToGraveyard(p, c);
        }
    }
}
