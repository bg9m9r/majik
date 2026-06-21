using System;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Guards the source-indirect retrofit: every migrated bespoke factory whose
/// activated ability was re-sourced to read ResolutionContext.Source must report
/// RebindSafe = true, so Agatha's Soul Cauldron (and copy effects) re-home them
/// via the exact RebindTo path instead of the oracle-text reconstruction fallback.
///
/// Add a row to <see cref="MigratedSelfPumpCards"/> per migrated card.
/// </summary>
public class SourceIndirectRebindTests
{
    private static readonly Player Owner = new("Alice", 20);

    public static TheoryData<string, Func<Player, Creature>> MigratedSelfPumpCards =>
        new()
        {
            { "Fiery Hellhound", FieryHellhoundFactory.Create },
            { "Wall of Fire",    WallOfFireFactory.Create },
        };

    [Theory]
    [MemberData(nameof(MigratedSelfPumpCards))]
    public void MigratedActivatedAbility_IsRebindSafe(
        string name, Func<Player, Creature> create)
    {
        var card = create(Owner);
        // Each of these cards has exactly ONE non-mana activated ability.
        var ability = card.Abilities.OfType<ActivatedAbility>()
            .Single(a => a is not IManaAbility);

        ability.RebindSafe.Should().BeTrue(
            $"{name}'s activated ability was migrated to read ResolutionContext.Source "
            + "(ctx.Source as Creature), so it is sound to re-home via RebindTo");
    }
}
