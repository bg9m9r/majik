using System.Linq;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Fateful Absence (Innistrad: Midnight Hunt, {1}{W}, Instant).
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "Destroy target creature or planeswalker. Its controller investigates.
///    (Create a Clue token. It's an artifact with '{2}, Sacrifice this
///    token: Draw a card.')"
///
/// Fateful Absence fuses Dreadbore's "destroy target creature or
/// planeswalker" resolve with the shared Clue/Investigate primitive
/// (Thraben Inspector). The test set mirrors DreadboreTests plus an
/// investigate assertion.
///
/// Covers:
///   - Card identity (Instant, {1}{W}, owner / controller).
///   - NamedCardFactory dispatch.
///   - SpellDefinition shape — single 1..1 creature-or-PW target request,
///     no modes, no variable X, BotIntent.Removal.
///   - Resolve: destroys a creature (CR 701.7) AND its controller
///     investigates (CR 701.39 — a Clue token under that controller).
///   - Resolve: destroys a planeswalker (CR 701.7) AND its controller
///     investigates.
///   - Resolve: artifact target (not creature, not PW) → no-op, no Clue
///     (CR 608.2b / 608.2c).
///   - Resolve: off-battlefield target → no-op, no Clue (CR 608.2b).
/// </summary>
public class FatefulAbsenceTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void FatefulAbsence_IsInstant_AtCost1W()
    {
        var card = FatefulAbsenceFactory.Create(_alice);

        card.Name.Should().Be("Fateful Absence");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FatefulAbsence()
    {
        var card = NamedCardFactory.Create("Fateful Absence", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Fateful Absence");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{W}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // SpellDefinition — structural shape
    // -----------------------------------------------------------------------

    [Fact]
    public void FatefulAbsence_Definition_HasSingleCreatureOrPlaneswalkerTarget()
    {
        var def = FatefulAbsenceFactory.BuildDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);

        var tr = def.TargetRequests[0];
        tr.MinTargets.Should().Be(1);
        tr.MaxTargets.Should().Be(1);
        tr.Description.Should().Contain("creature or planeswalker");
        tr.Intent.Should().Be(BotIntent.Removal);
    }

    // -----------------------------------------------------------------------
    // Resolve — destroy + investigate
    // -----------------------------------------------------------------------

    [Fact]
    public void FatefulAbsence_DestroysCreature_AndControllerInvestigates()
    {
        var goblin = NewControlledCreature(_bob, "Goblin Guide", "{R}");

        Resolve(goblin);

        goblin.Zone.Should().Be(ZoneType.Graveyard,
            "Fateful Absence destroys the targeted creature (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(goblin);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(goblin);

        // "Its controller investigates." — Bob owned/controlled the creature,
        // so Bob banks exactly one Clue token (CR 701.39).
        BobClues().Should().HaveCount(1,
            "the destroyed permanent's controller investigates (CR 701.39)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(
            c => c.Name == "Clue", "only the target's controller investigates");
    }

    [Fact]
    public void FatefulAbsence_DestroysPlaneswalker_AndControllerInvestigates()
    {
        var pw = new Planeswalker(
            name: "Liliana, the Last Hope",
            manaCost: "{1}{B}{B}",
            startingLoyalty: 3,
            subtypes: new[] { CardSubtype.Liliana })
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        Resolve(pw);

        pw.Zone.Should().Be(ZoneType.Graveyard,
            "Fateful Absence destroys the targeted planeswalker (CR 701.7)");
        _bob.Zones.Graveyard.GetCards().Should().Contain(pw);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(pw);

        BobClues().Should().HaveCount(1,
            "the planeswalker's controller investigates (CR 701.39)");
    }

    // -----------------------------------------------------------------------
    // Resolve — illegal targets (no destroy, no investigate)
    // -----------------------------------------------------------------------

    [Fact]
    public void FatefulAbsence_ArtifactTarget_DoesNothing_AndNoInvestigate()
    {
        // Pure artifact (not creature, not PW) — illegal at resolution.
        var artifact = new Artifact("Sol Ring", "{1}")
        {
            Owner = _bob,
            Controller = _bob,
        };
        _bob.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);

        Resolve(artifact);

        artifact.Zone.Should().Be(ZoneType.Battlefield,
            "Fateful Absence can only destroy creatures or planeswalkers (CR 608.2b)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(artifact);

        // Illegal target → whole instruction skipped, including the
        // investigate rider (CR 608.2c).
        BobClues().Should().BeEmpty(
            "an illegal target skips the entire effect, including investigate");
    }

    [Fact]
    public void FatefulAbsence_TargetNotOnBattlefield_DoesNothing_AndNoInvestigate()
    {
        var creature = NewControlledCreature(_bob, "Tarmogoyf", "{1}{G}");

        // Simulate the target leaving the battlefield before resolution.
        _bob.Zones.Battlefield.RemoveCard(creature);
        creature.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(creature);

        Resolve(creature);

        // Zone unchanged by the resolve — CR 608.2b illegal target → no-op.
        creature.Zone.Should().Be(ZoneType.Graveyard);
        BobClues().Should().BeEmpty(
            "an illegal target skips the entire effect, including investigate");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private System.Collections.Generic.IEnumerable<ICard> BobClues() =>
        _bob.Zones.Battlefield.GetCards().Where(c => c.Name == "Clue");

    private static void Resolve(object targetToken)
    {
        var def = FatefulAbsenceFactory.BuildDefinition(targetResolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { targetToken } },
            Mana: ManaPayment.Empty);

        foreach (var fx in def.EffectFactory(chosen))
        {
            fx.Execute();
        }
    }

    private static Creature NewControlledCreature(Player owner, string name, string cost)
    {
        var c = new Creature(name, cost, 1, 1);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
