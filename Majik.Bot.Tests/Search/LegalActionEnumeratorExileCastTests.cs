using FluentAssertions;
using Majik.Bot.Search;
using Majik.Bot.Tests.Helpers;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Bot.Tests.Search;

/// <summary>
/// Task 2.2 — <see cref="LegalActionEnumerator"/> must enumerate cast-from-
/// exile runtime grants (CR 118.9: madness / Ragavan / foretell / impulse).
/// A card in the player's EXILE zone carrying a
/// <see cref="Card.RuntimeExileCastAllowedCaster"/> grant nominating that
/// player becomes a legal cast when affordable; the enumerator surfaces it as
/// a <see cref="PriorityAction.CastSpell"/> with a non-null
/// <see cref="ExileCastAlternativeCost"/>.
/// </summary>
public class LegalActionEnumeratorExileCastTests
{
    [Fact]
    public void ForPriority_SurfacesExileCast_WhenRuntimeGrantNominatesSelf_AndAffordable()
    {
        var s = new BotTestScenario();

        // Fiery Temper discarded to madness — exiled, granted {B}{R}.
        var temper = new Instant("Fiery Temper", "1RR");
        temper.ChangeOwner(s.Self);
        s.Self.Zones.Exile.AddCard(temper);
        temper.GrantRuntimeExileCast(s.Self, ManaCost.Parse("{B}{R}"));

        // Two lands for {B}{R} (colour-blind affordability, 2 mana).
        s.AddLandToBattlefield(s.Self, "Mountain");
        s.AddLandToBattlefield(s.Self, "Swamp");

        var casts = LegalActionEnumerator.ForPriority(s.Context, s.Self)
            .OfType<PriorityAction.CastSpell>()
            .ToList();

        var exileCast = casts.SingleOrDefault(c => ReferenceEquals(c.Card, temper));
        exileCast.Should().NotBeNull();
        exileCast!.AlternativeCost.Should().BeOfType<ExileCastAlternativeCost>();
        exileCast.AlternativeCost!.AlternativeManaCost.Should().Be(ManaCost.Parse("{B}{R}"));
    }

    [Fact]
    public void ForPriority_DoesNotSurfaceExileCard_WhenNoRuntimeGrant()
    {
        var s = new BotTestScenario();

        var temper = new Instant("Fiery Temper", "1RR");
        temper.ChangeOwner(s.Self);
        s.Self.Zones.Exile.AddCard(temper);
        // Deliberately NO grant.

        s.AddLandToBattlefield(s.Self, "Mountain");
        s.AddLandToBattlefield(s.Self, "Swamp");

        var casts = LegalActionEnumerator.ForPriority(s.Context, s.Self)
            .OfType<PriorityAction.CastSpell>()
            .ToList();

        casts.Should().NotContain(c => ReferenceEquals(c.Card, temper));
    }

    [Fact]
    public void ForPriority_DoesNotSurfaceExileCard_WhenGrantNominatesOtherPlayer()
    {
        var s = new BotTestScenario();

        var temper = new Instant("Fiery Temper", "1RR");
        temper.ChangeOwner(s.Self);
        s.Self.Zones.Exile.AddCard(temper);
        // Grant is for the opponent, not self.
        temper.GrantRuntimeExileCast(s.Opponent, ManaCost.Parse("{B}{R}"));

        s.AddLandToBattlefield(s.Self, "Mountain");
        s.AddLandToBattlefield(s.Self, "Swamp");

        var casts = LegalActionEnumerator.ForPriority(s.Context, s.Self)
            .OfType<PriorityAction.CastSpell>()
            .ToList();

        casts.Should().NotContain(c => ReferenceEquals(c.Card, temper));
    }

    [Fact]
    public void ForPriority_DoesNotSurfaceExileCast_WhenUnaffordable()
    {
        var s = new BotTestScenario();

        var temper = new Instant("Fiery Temper", "1RR");
        temper.ChangeOwner(s.Self);
        s.Self.Zones.Exile.AddCard(temper);
        temper.GrantRuntimeExileCast(s.Self, ManaCost.Parse("{B}{R}"));

        // Only one land — {B}{R} (2 mana) is unaffordable.
        s.AddLandToBattlefield(s.Self, "Mountain");

        var casts = LegalActionEnumerator.ForPriority(s.Context, s.Self)
            .OfType<PriorityAction.CastSpell>()
            .ToList();

        casts.Should().NotContain(c => ReferenceEquals(c.Card, temper));
    }
}
