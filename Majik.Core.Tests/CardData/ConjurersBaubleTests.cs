using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ConjurersBaubleFactory"/>.
///
/// Covers card identity, the {1} + {T} + Sacrifice activated ability shape
/// (mana cost + tap + sacrifice + one graveyard target), the resolution
/// semantics (move target Graveyard -> bottom of Library, sac self, draw 1),
/// and edge cases (illegal target at resolution still cantrips; empty
/// library is a silent no-op for the cantrip).
/// </summary>
public class ConjurersBaubleTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ConjurersBauble_IsArtifact()
    {
        var bauble = ConjurersBaubleFactory.Create(_alice);
        bauble.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void ConjurersBauble_NameIsCorrect()
    {
        var bauble = ConjurersBaubleFactory.Create(_alice);
        bauble.Name.Should().Be("Conjurer's Bauble");
    }

    [Fact]
    public void ConjurersBauble_OwnerAndControllerAreSet()
    {
        var bauble = ConjurersBaubleFactory.Create(_alice);
        bauble.Owner.Should().BeSameAs(_alice);
        bauble.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ConjurersBauble_HasExactlyOneActivatedAbility()
    {
        var bauble = ConjurersBaubleFactory.Create(_alice);
        bauble.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ConjurersBauble_Ability_HasManaTapAndSacrificeCosts()
    {
        var bauble = ConjurersBaubleFactory.Create(_alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>()
            .Should().ContainSingle(c => c.Cost.Generic == 1,
                "the {1} generic mana cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap, "the {T} cost");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice, "the sac cost");
    }

    [Fact]
    public void ConjurersBauble_Ability_HasSingleGraveyardTargetRequest()
    {
        var bauble = ConjurersBaubleFactory.Create(_alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.TargetRequests.Should().ContainSingle();
        var req = ability.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("graveyard");
    }

    // -----------------------------------------------------------------------
    // Resolution: target graveyard card -> bottom of library, sac, draw 1
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_PutsTargetGraveyardCardOnBottomOfLibrary()
    {
        // Seed library so the cantrip has something to draw + so the
        // "bottom" placement is observable as the last card. Two cards
        // so that after the draw, there's still a clear "first" left to
        // verify the recycled card ended up at the bottom, not the top.
        var top = new Card("Top of library", "");
        var second = new Card("Second of library", "");
        _alice.Zones.Library.AddCard(top); top.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(second); second.SetZone(ZoneType.Library);

        var gyCard = new Card("Recycled", "");
        _alice.Zones.Graveyard.AddCard(gyCard);
        gyCard.SetZone(ZoneType.Graveyard);

        var bauble = ConjurersBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { gyCard },
        });

        foreach (var e in ability.Effects) e.Execute();

        // gyCard moved from graveyard to library (bottom = last position).
        _alice.Zones.Graveyard.GetCards().Should().NotContain(gyCard);
        gyCard.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Library.GetCards().Last().Should().BeSameAs(gyCard,
            "the recycled card is placed on the bottom of the library");
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(second,
            "after the draw of top, second is now on top — recycled went to bottom");
    }

    [Fact]
    public void Activation_SacrificesBauble_MovesToGraveyard()
    {
        var gyCard = new Card("Recycled", "");
        _alice.Zones.Graveyard.AddCard(gyCard);
        gyCard.SetZone(ZoneType.Graveyard);

        var bauble = ConjurersBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { gyCard },
        });

        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Graveyard.GetCards().Should().Contain(bauble,
            "sacrifice moves the bauble to its owner's graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(bauble);
        bauble.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activation_DrawsACard()
    {
        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var gyCard = new Card("Recycled", "");
        _alice.Zones.Graveyard.AddCard(gyCard);
        gyCard.SetZone(ZoneType.Graveyard);

        var bauble = ConjurersBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { gyCard },
        });

        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(top,
            "the cantrip draws the top card of the library");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
    }

    // -----------------------------------------------------------------------
    // CR 608.2b — illegal target at resolution skips the move step;
    // sacrifice + cantrip still happen because the cost was paid.
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_IllegalTarget_SkipsMoveButStillSacrificesAndDraws()
    {
        var top = new Card("Top", "");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        // Target was in graveyard at activation, but moved away before
        // resolution (e.g. someone else exiled it). Simulate by leaving it
        // outside any zone — the closure's "still in graveyard?" gate
        // should fail and the move should be skipped.
        var gone = new Card("Stolen", "");
        gone.SetZone(ZoneType.Exile);

        var bauble = ConjurersBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { gone },
        });

        foreach (var e in ability.Effects) e.Execute();

        // No surprise library write of the illegal target.
        _alice.Zones.Library.GetCards().Should().NotContain(gone);
        // Sacrifice + cantrip still happen.
        _alice.Zones.Graveyard.GetCards().Should().Contain(bauble);
        _alice.Zones.Hand.GetCards().Should().Contain(top);
    }

    // -----------------------------------------------------------------------
    // Cantrip is silently a no-op on an empty library — same simplification
    // used everywhere else in the engine (SBAs handle the loss condition).
    // -----------------------------------------------------------------------

    [Fact]
    public void Activation_EmptyLibrary_DrawIsSilentNoOp_StillSacrifices()
    {
        // No graveyard target chosen + empty library: the move step is
        // skipped and the draw is a silent no-op. Sacrifice still resolves
        // because the cost was paid.
        var bauble = ConjurersBaubleFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bauble);
        bauble.SetZone(ZoneType.Battlefield);

        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(Array.Empty<IReadOnlyList<object>>());

        // Sanity: library starts empty.
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        foreach (var e in ability.Effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "empty library = draw is a silent no-op");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bauble,
            "sacrifice still happens because the cost was paid");
    }
}
