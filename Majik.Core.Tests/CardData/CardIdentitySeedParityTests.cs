using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Per-card printed-identity parity regression guard for the semantic-audit
/// data fix (wrong base P/T + wrong/missing creature subtypes across ~50
/// factories). Each named card is built through the prod dispatch path
/// (<see cref="NamedCardFactory.Create"/>) and its base power/toughness +
/// creature subtypes are compared against the Scryfall-derived seed
/// (<see cref="EmbeddedCardRepository"/> + <see cref="TypeLineParser"/>),
/// which is the source of truth for printed characteristics.
///
/// Guards against regressing any of these factories back to a fabricated stat
/// line or subtype set. Mirrors the gate of
/// <c>SemanticImplementationAuditTests.PrintPrintedCharacteristicsParity</c>
/// but pinned to the specific cards this fix corrected.
/// </summary>
public class CardIdentitySeedParityTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    // Every card corrected by the semantic-audit parity fix (Bucket A — wrong
    // base P/T; Bucket B — wrong/missing creature subtypes, incl. dropped
    // Phyrexian). The test resolves expected P/T + subtypes from the seed, so
    // the list is just "which cards must match the seed".
    public static TheoryData<string> FixedCards() => new()
    {
        // Bucket A — base P/T
        "Anointed Peacekeeper", "Arcbound Stinger", "Badgermole Cub",
        "Boromir, Warden of the Tower", "Eldrazi Skyspawner", "Etched Oracle",
        "Falkenrath Pit Fighter", "Goblin Anarchomancer", "Hired Claw",
        "Keldon Marauders", "Lonis, Cryptozoologist", "Monstrous Carabid",
        "Recruitment Officer", "Samwise Gamgee", "Setessan Champion",
        "Signal Pest", "Stormchaser Mage", "Voice of Victory",
        "Winding Constrictor",
        // Bucket B.ii — wrong/missing creature subtypes
        "Atarka, World Render", "Bedlam Reveler", "Cliffhaven Vampire",
        "Drift of Phantasms", "Earthshaker Khenra", "Falkenrath Noble",
        "Goblin Chieftain", "Guide of Souls", "Hedron Crab", "Insolent Neonate",
        "Kalitas, Traitor of Ghet", "Knight-Errant of Eos", "Krosan Tusker",
        "Lurking Roper", "Magmatic Channeler", "Phelia, Exuberant Shepherd",
        "Phyrexian Crusader", "Priest of Fell Rites", "Prized Amalgam",
        "Psychic Frog", "Puresteel Paladin", "Quirion Beastcaller",
        "Ramunap Excavator", "Reckless Bushwhacker", "Sakura-Tribe Scout",
        "Selfless Spirit", "Slickshot Show-Off", "Soaring Thought-Thief",
        "Soul-Scar Mage", "Squee, Dubious Monarch", "Sundering Titan",
        "Vengevine", "Vito, Thorn of the Dusk Rose", "Voldaren Epicure",
        "Whirler Virtuoso", "Zulaport Cutthroat",
        // Bucket B.i — dropped Phyrexian subtype
        "Deceiver Exarch", "Necropede", "Plague Engineer", "Plague Stinger",
        "Sheoldred, Whispering One", "Skithiryx, the Blight Dragon",
        "Spellskite", "Vault Skirge",
        // Bucket C — semantic-parity-tail creatures (wrong base P/T and/or
        // wrong/missing subtype). Demilich Skeleton (not Zombie); World
        // Breaker 5/7 (not 5/5); Legion Warboss 2/2 (not 2/1); Pashalik Mons
        // 2/2 (not 3/3); Foundation Breaker 2/2 power (not 3); Yawgmoth no
        // Phyrexian; Phoenix of Ash 2/2 power (not 3); Rattlechains 2/1
        // toughness (not 2); Boreal Druid Snow; Ocelot Pride non-Legendary;
        // Harbinger of the Seas + Merfolk; Matter Reshaper no Drone;
        // Munitions Expert no Warrior; Sojourner's Companion Salamander (not
        // Thopter Knight); Street Wraith no Zombie (Wraith has no enum value).
        "Demilich", "World Breaker", "Legion Warboss", "Pashalik Mons",
        "Foundation Breaker", "Yawgmoth, Thran Physician", "Phoenix of Ash",
        "Rattlechains", "Boreal Druid", "Ocelot Pride",
        "Harbinger of the Seas", "Matter Reshaper", "Munitions Expert",
        "Sojourner's Companion", "Street Wraith",
    };

    [Theory]
    [MemberData(nameof(FixedCards))]
    public void BuiltCard_BasePowerToughnessAndSubtypes_MatchSeed(string name)
    {
        var entity = Repo.GetByName(name);
        entity.Should().NotBeNull($"'{name}' must be present in the seed");

        var built = NamedCardFactory.Create(name, new Player("Alice", 20));
        built.Should().BeOfType<Creature>($"'{name}' is a creature");
        var creature = (Creature)built;

        var parsed = TypeLineParser.Parse(entity!.TypeLine);

        // --- Base power / toughness (printed integer stats only) ---
        int.TryParse(entity.Power, out var seedPower).Should().BeTrue(
            $"'{name}' has a fixed integer printed power in the seed");
        int.TryParse(entity.Toughness, out var seedToughness).Should().BeTrue(
            $"'{name}' has a fixed integer printed toughness in the seed");

        creature.BasePower.Should().Be(seedPower,
            $"'{name}' base power must match the seed (printed P/T)");
        creature.BaseToughness.Should().Be(seedToughness,
            $"'{name}' base toughness must match the seed (printed P/T)");

        // --- Creature subtypes (order-insensitive set equality vs the parser's
        // enum-backed subtypes; subtypes with no CardSubtype enum value are
        // dropped by the parser and must likewise be absent on the built card). ---
        var expected = parsed.Subtypes.Distinct().OrderBy(s => s).ToList();
        var actual = creature.Subtypes.Distinct().OrderBy(s => s).ToList();
        actual.Should().Equal(expected,
            $"'{name}' printed creature subtypes must match the seed type line");

        // --- Supertypes (Snow / Legendary). Boreal Druid is Snow; Ocelot
        // Pride is NOT Legendary — both must match the seed exactly. ---
        var expectedSupers = parsed.Supertypes.Distinct().OrderBy(s => s).ToList();
        var actualSupers = creature.Supertypes.Distinct().OrderBy(s => s).ToList();
        actualSupers.Should().Equal(expectedSupers,
            $"'{name}' printed supertypes must match the seed type line");
    }

    // ------------------------------------------------------------------
    // Non-creature semantic-parity-tail cards (Instants / Sorceries /
    // Enchantment): full printed-characteristics parity — card types,
    // supertypes, subtypes — vs the seed. Covers the Kindred/Tribal product
    // decision (the engine no longer stamps CardType.Tribal — see the
    // semantic-parity-tail PR) plus the Necrodominance (Legendary) and the
    // Arcane / Elf spell-subtype fixes.
    // ------------------------------------------------------------------
    public static TheoryData<string> FixedNonCreatures() => new()
    {
        // Kindred/Tribal removal — must be a plain Instant/Sorcery/Enchantment.
        "All Is Dust", "Bitterblossom", "Kozilek's Command",
        "Nameless Inversion", "Tarfire",
        // Supertype + spell-subtype fixes.
        "Necrodominance",          // + Legendary supertype
        "Eyeblight's Ending",      // + Elf subtype on the spell
        "Footsteps of the Goryo",  // + Arcane subtype
        "Otherworldly Journey",    // + Arcane subtype
    };

    [Theory]
    [MemberData(nameof(FixedNonCreatures))]
    public void BuiltNonCreature_PrintedTypesSupertypesSubtypes_MatchSeed(string name)
    {
        var entity = Repo.GetByName(name);
        entity.Should().NotBeNull($"'{name}' must be present in the seed");

        var built = NamedCardFactory.Create(name, new Player("Alice", 20));
        var parsed = TypeLineParser.Parse(entity!.TypeLine);

        // Card types — the parser drops the removed Kindred/Tribal type, so
        // the built card must carry exactly the seed's types (no Tribal).
        var expectedTypes = parsed.Types.Distinct().OrderBy(t => t).ToList();
        var actualTypes = built.CardTypes.Distinct().OrderBy(t => t).ToList();
        actualTypes.Should().Equal(expectedTypes,
            $"'{name}' printed card types must match the seed (no removed " +
            "Kindred/Tribal type)");

        var expectedSupers = parsed.Supertypes.Distinct().OrderBy(s => s).ToList();
        var actualSupers = built.Supertypes.Distinct().OrderBy(s => s).ToList();
        actualSupers.Should().Equal(expectedSupers,
            $"'{name}' printed supertypes must match the seed type line");

        var expectedSubs = parsed.Subtypes.Distinct().OrderBy(s => s).ToList();
        var actualSubs = built.Subtypes.Distinct().OrderBy(s => s).ToList();
        actualSubs.Should().Equal(expectedSubs,
            $"'{name}' printed subtypes must match the seed type line");
    }
}
