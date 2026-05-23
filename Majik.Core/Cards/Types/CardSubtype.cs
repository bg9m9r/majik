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
    Advisor,
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
    /// <summary>Avatar creature subtype — Death's Shadow, Akroma. CR 205.3m.</summary>
    Avatar,
    /// <summary>Wurm creature subtype — Wurmcoil Engine, Penumbra Wurm. CR 205.3m.</summary>
    Wurm,
    /// <summary>Nightmare creature subtype — Lurrus of the Dream-Den, Nightmare Lash. CR 205.3m.</summary>
    Nightmare,
    /// <summary>Rhino creature subtype — Crashing Footfalls Rhino tokens, Siege Rhino. CR 205.3m.</summary>
    Rhino,
    /// <summary>Giant creature subtype — Primeval Titan, Hill Giant. CR 205.3m.</summary>
    Giant,
    /// <summary>Dauthi creature subtype — Tempest shadow creatures (Dauthi
    /// Voidwalker, Dauthi Slayer, Dauthi Horror). CR 205.3m.</summary>
    Dauthi,
    /// <summary>Monkey creature subtype — Ragavan, Nimble Pilferer. CR 205.3m.</summary>
    Monkey,
    /// <summary>Pirate creature subtype — Ragavan, Nimble Pilferer; Captain Lannery Storm. CR 205.3m.</summary>
    Pirate,
    /// <summary>Scout creature subtype — Tireless Tracker, Joraga Treespeaker. CR 205.3m.</summary>
    Scout,
    /// <summary>Illusion creature subtype — Phantasmal Image, Phantasmal Bear,
    /// Lord of the Unreal. CR 205.3m.</summary>
    Illusion,
    /// <summary>Nymph creature subtype — Sythis, Harvest's Hand; Theros Beyond
    /// Death constellation cycle. CR 205.3m.</summary>
    Nymph,
    /// <summary>Minotaur creature subtype — Boros Reckoner. CR 205.3m.</summary>
    Minotaur,
    /// <summary>Praetor creature subtype — Sheoldred, the Apocalypse; the New
    /// Phyrexia / Phyrexia: All Will Be One praetor cycles. CR 205.3m.</summary>
    Praetor,
    /// <summary>Elk creature subtype — Oko, Thief of Crowns' +1 token target
    /// type and the Elk creature tokens it implies. CR 205.3m.</summary>
    Elk,
    /// <summary>Bard creature subtype — Nadu, Winged Wisdom; Adventures in
    /// the Forgotten Realms Bard cycle. CR 205.3m.</summary>
    Bard,
    /// <summary>Frog creature subtype — Psychic Frog, Sakura-Tribe Scout's
    /// sibling cycle. CR 205.3m.</summary>
    Frog,
    /// <summary>Mutant creature subtype — Psychic Frog, Mutavault's
    /// changeling base type. CR 205.3m.</summary>
    Mutant,
    /// <summary>Horror creature subtype — Spellskite (New Phyrexia),
    /// Phyrexian Obliterator. CR 205.3m.</summary>
    Horror,
    /// <summary>Phoenix creature subtype — Arclight Phoenix, Rekindling
    /// Phoenix, Skarrgan Pit-Skulk-adjacent. CR 205.3m.</summary>
    Phoenix,
    /// <summary>Fish creature subtype — Gurmag Angler, Tatsumasa, the Dragon's Fang's
    /// Dragon Fish token. CR 205.3m.</summary>
    Fish,

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
    Wrenn,
    Oko
}
