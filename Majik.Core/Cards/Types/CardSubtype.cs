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
    Spirit,
    Warrior,
    Wizard,
    Cleric,
    Rogue,
    Knight,
    Soldier,
    Shaman,

    // Land subtypes (examples)
    Forest,
    Island,
    Mountain,
    Plains,
    Swamp,
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

    // Planeswalker subtypes (examples)
    Ajani,
    Chandra,
    Jace,
    Liliana,
    Garruk,
    Nissa,
    Teferi,
    Karn,
    Ugin,
    Bolas
}
