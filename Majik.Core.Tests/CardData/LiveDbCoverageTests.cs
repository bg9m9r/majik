using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;
using Xunit.Abstractions;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Sanity-check the data-driven binders against the real Scryfall DB.
/// Tests no-op when the DB is missing (CI without bulk import passes).
/// </summary>
public class LiveDbCoverageTests
{
    private readonly ITestOutputHelper _output;

    private static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Majik", "cards.db");

    private static bool DbAvailable() => File.Exists(DbPath);

    public LiveDbCoverageTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Mountain_FromRealDb_TapsForRed()
    {
        if (!DbAvailable()) return;
        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));
        var card = factory.Create("Mountain", new Player("A", 20));
        card.Abilities.OfType<IManaAbility>().Should().ContainSingle();
    }

    [Fact]
    public void SerraAngel_FromRealDb_HasFlyingAndVigilance()
    {
        if (!DbAvailable()) return;
        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));
        var c = (Creature)factory.Create("Serra Angel", new Player("A", 20));

        CombatAbilities.HasFlying(c).Should().BeTrue();
        CombatAbilities.HasVigilance(c).Should().BeTrue();
    }

    [Fact]
    public void LightningBolt_FromRealDb_DealsThreeDamage()
    {
        if (!DbAvailable()) return;
        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));
        var alice = new Player("A", 20);
        var bob = new Player("B", 20);

        var def = factory.LookupSpellDefinition("Lightning Bolt", alice, raw => raw, stack: null);
        def.Should().NotBeNull("oracle text 'deals 3 damage to any target' should pattern-match");

        var chosen = new Majik.Core.Game.ChosenSpellParams(
            null, null,
            new[] { new object[] { bob } },
            Majik.Core.Players.Agents.ManaPayment.Empty);
        foreach (var e in def!.EffectFactory(chosen)) e.Execute();
        bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public void Counterspell_FromRealDb_BindsCounter()
    {
        if (!DbAvailable()) return;
        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));
        var def = factory.LookupSpellDefinition("Counterspell", new Player("A", 20), raw => raw, stack: null);
        def.Should().NotBeNull();
    }

    /// <summary>
    /// Survey a hand-picked sample of common cards — reports bind coverage
    /// (creature has keyword/mana abilities or spell has a definition).
    /// Soft assertion: ≥ 60% bind. Real coverage is much higher; the
    /// threshold is just a regression guard.
    /// </summary>
    [Fact]
    public void Sample_CommonCards_BindRateAbove60Percent()
    {
        if (!DbAvailable()) return;
        using var db = new CardDbContext();
        var factory = new ScryfallCardFactory(new DbCardRepository(db));
        var alice = new Player("A", 20);

        // 30 common cards across formats — mix of mana, keyword, and spell cards.
        string[] sample =
        {
            // Mana lands
            "Mountain", "Forest", "Plains", "Island", "Swamp",
            // Vanilla / keyword creatures
            "Grizzly Bears", "Hill Giant", "Centaur Courser", "Runeclaw Bear",
            "Serra Angel", "Air Elemental", "Wind Drake", "Royal Assassin",
            "Llanowar Elves",
            // Damage spells
            "Lightning Bolt", "Shock", "Lava Spike", "Searing Spear",
            // Counters
            "Counterspell", "Cancel", "Negate",
            // Draw/discard
            "Divination", "Mind Rot",
            // Removal
            "Doom Blade", "Murder", "Naturalize",
            // Misc
            "Healing Salve", "Giant Growth", "Cancel",
        };

        var bound = 0;
        var attempted = 0;
        foreach (var name in sample)
        {
            attempted++;
            var card = factory.Create(name, alice);
            var hasAbility = card.Abilities.Any();
            var hasSpellDef = factory.LookupSpellDefinition(name, alice, raw => raw, null) != null;
            if (hasAbility || hasSpellDef)
            {
                bound++;
            }
            else
            {
                _output.WriteLine($"  unbound: {name}");
            }
        }

        var rate = bound / (double)attempted;
        _output.WriteLine($"Bound {bound}/{attempted} = {rate:P0}");
        rate.Should().BeGreaterOrEqualTo(0.6);
    }
}
