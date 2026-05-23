namespace Majik.Core.Cards.Types;

/// <summary>
/// Common card subtypes as defined in Magic: The Gathering rules (Rule 205.3).
/// Subtypes are type-specific (e.g., Creature subtypes, Land subtypes).
/// This is a simplified list; full implementation would have separate enums per type.
/// </summary>
public enum CardSubtype
{
    // Creature subtypes (examples)
    Human,
    Dryad,
    Phyrexian,
    Elf,
    Goblin,
    Dragon,
    Angel,
    Demon,
    Zombie,
    Beast,
    Bird,
    Cat,
    Dog,
    Elemental,
    Bear,
    Insect,
    Spirit,
    Warrior,
    Wizard,
    Cleric,
    Rogue,
    Knight,
    Soldier,
    Shaman,
    Halfling,
    Citizen,
    Orc,
    Archer,
    Army,
    /// <summary>Modern Horizons 2 incarnation cycle (Solitude, Endurance,
    /// Fury, Grief, Subtlety). CR 205.3m — creature subtype.</summary>
    Incarnation,
    /// <summary>Lhurgoyf creature subtype — Tarmogoyf, Mortivore. CR 205.3m.</summary>
    Lhurgoyf,
    /// <summary>Kor creature subtype — Stoneforge Mystic, Kor Outfitter. CR 205.3m.</summary>
    Kor,
    /// <summary>Artificer creature subtype — Stoneforge Mystic, Goblin Engineer. CR 205.3m.</summary>
    Artificer,
    /// <summary>Ooze creature subtype — Scavenging Ooze, Acidic Slime. CR 205.3m.</summary>
    Ooze,

    // Land subtypes (examples)
    Forest,
    Island,
    Mountain,
    Plains,
    Swamp,
    Wastes,
    Desert,
    Gate,
    Lair,
    Locus,
    Mine,
    PowerPlant,
    Tower,
    Urzas,

    // Enchantment subtypes (examples)
    Aura,
    Saga,
    Shrine,

    // Artifact subtypes (examples)
    Equipment,
    Vehicle,
    Food,
    Treasure,
    Clue,
    Construct,
    Blood,
    Powerstone,

    // Eldrazi creature subtypes (CR 205.3m)
    Eldrazi,
    Spawn,
    Scion,

    // Planeswalker subtypes (examples)
    Ajani,
    Ashiok,
    Chandra,
    Grist,
    Jace,
    Liliana,
    Garruk,
    Nissa,
    Teferi,
    Karn,
    Ugin,
    Bolas,
    Wrenn
}
