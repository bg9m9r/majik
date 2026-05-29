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
    Dwarf,
    Phyrexian,
    Germ,
    Elf,
    Goblin,
    Dragon,
    /// <summary>Drake creature subtype (Wind Drake, Skywinder Drake). CR 205.3m.</summary>
    Drake,
    Angel,
    Demon,
    /// <summary>Devil creature subtype — Mayhem Devil (War of the Spark),
    /// Hellrider, Devil Token (Bedevil / Maelstrom Pulse adjacent printings).
    /// CR 205.3m.</summary>
    Devil,
    Zombie,
    Skeleton,
    Beast,
    Bird,
    Cat,
    Dog,
    Elemental,
    Bear,
    Insect,
    Spirit,
    Warrior,
    /// <summary>Warlock creature subtype — Witch Enchanter (Wilds of
    /// Eldraine, front face), Withengar Unbound. CR 205.3m.</summary>
    Warlock,
    Wizard,
    /// <summary>Moonfolk creature subtype — Tamiyo, Inquisitive Student
    /// (front face), Soratami creatures of Kamigawa. CR 205.3m.</summary>
    Moonfolk,
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
    /// <summary>Noble creature subtype — Muxus, Goblin Grandee, Goblin
    /// Trashmaster. CR 205.3m. Added to support Muxus's "Goblin Noble"
    /// printed line; pairs naturally with the Goblin subtype.</summary>
    Noble,
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
    /// <summary>Dinosaur creature subtype — Amped Raptor, Ripjaw Raptor,
    /// Ghalta, Primal Hunger. Introduced in Ixalan; renamed from "Lizard"
    /// for the original tribal cycle. CR 205.3m.</summary>
    Dinosaur,
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
    /// <summary>Lizard creature subtype — Rapid Hybridization's 3/3 Frog
    /// Lizard token (Gatecrash). CR 205.3m.</summary>
    Lizard,
    /// <summary>Llama creature subtype — Restless Prairie's animated 3/3
    /// green and white Llama body (Murders at Karlov Manor). CR 205.3m.</summary>
    Llama,
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
    /// <summary>Merfolk creature subtype — Lord of Atlantis, Master of the Pearl Trident,
    /// Harbinger of the Seas, Silvergill Adept. CR 205.3m.</summary>
    Merfolk,
    /// <summary>Kavu creature subtype — Territorial Kavu, Shivan Wurm's body type.
    /// CR 205.3m.</summary>
    Kavu,
    /// <summary>Druid creature subtype — Noble Hierarch, Fyndhorn Elder,
    /// Devoted Druid. CR 205.3m.</summary>
    Druid,
    /// <summary>Ouphe creature subtype — Kitchen Finks (Shadowmoor / Modern
    /// Horizons 2), Witchstalker. CR 205.3m.</summary>
    Ouphe,
    /// <summary>Monk creature subtype — Mantis Rider (Khans of Tarkir),
    /// Monastery Swiftspear. CR 205.3m.</summary>
    Monk,
    /// <summary>Vampire creature subtype — Bloodghast (Zendikar), Vampire Nighthawk,
    /// Olivia Voldaren. CR 205.3m.</summary>
    Vampire,
    /// <summary>Plant creature subtype — Wall of Roots (Mirrodin / many
    /// reprints), Sakura-Tribe Elder's adjacent flora cycle, Dryad cousins.
    /// CR 205.3m.</summary>
    Plant,
    /// <summary>Wall creature subtype — Wall of Roots (Mirrodin), Wall of
    /// Omens, Wall of Blossoms. Walls historically carry the Defender
    /// keyword but the subtype itself is mechanically inert; the Defender
    /// keyword is what blocks combat (CR 702.3). CR 205.3m.</summary>
    Wall,
    /// <summary>Snake creature subtype — Sakura-Tribe Scout (Champions of
    /// Kamigawa), Sakura-Tribe Elder, Lotus Cobra. CR 205.3m.</summary>
    Snake,
    /// <summary>Wolf creature subtype — Young Wolf (Innistrad), Strangleroot Geist,
    /// Master of the Wild Hunt. CR 205.3m.</summary>
    Wolf,
    /// <summary>Werewolf creature subtype (Victim of Night, Mayor of Avabruck,
    /// Huntmaster of the Fells). Innistrad's day/night transform lineage. CR 205.3m.</summary>
    Werewolf,
    /// <summary>Mercenary creature subtype — Slickshot Show-Off (Outlaws of
    /// Thunder Junction), Stormchaser's Talent's 1/1 U/R Mercenary token
    /// (Modern Horizons 3), and the Mercadian Masques cycle. CR 205.3m.</summary>
    Mercenary,
    /// <summary>Jock creature subtype — Slickshot Show-Off (Outlaws of Thunder
    /// Junction). One of the OTJ "outlaw" character class subtypes. CR 205.3m.</summary>
    Jock,
    /// <summary>Mouse creature subtype — Emberheart Challenger (Bloomburrow),
    /// and the rest of the Bloomburrow Mouse "Valiant" lineage. CR 205.3m.</summary>
    Mouse,
    /// <summary>Faerie creature subtype — Sprite Dragon (Ikoria), Spellstutter Sprite,
    /// Bitterblossom tokens. CR 205.3m.</summary>
    Faerie,
    /// <summary>Eye creature subtype — Abhorrent Oculus (Duskmourn: House of Horror).
    /// CR 205.3m.</summary>
    Eye,
    /// <summary>Turtle creature subtype — Kappa Cannoneer (Commander Legends:
    /// Battle for Baldur's Gate), Cosima, God of the Voyage's adjacent
    /// shellfolk. CR 205.3m.</summary>
    Turtle,
    /// <summary>Beholder creature subtype — Hive of the Eye Tyrant (Adventures
    /// in the Forgotten Realms manland cycle). CR 205.3m.</summary>
    Beholder,
    /// <summary>Shark creature subtype — Restless Reef (Outlaws of Thunder
    /// Junction restless-land cycle), Shark Typhoon tokens. CR 205.3m.</summary>
    Shark,
    /// <summary>Robot creature subtype — Pinnacle Emissary (Edge of Eternities)
    /// and the EOE robot lineage. CR 205.3m.</summary>
    Robot,
    /// <summary>God creature subtype — Heliod, Sun-Crowned (Theros Beyond
    /// Death) and the rest of the Theros / Amonkhet / Kaldheim God cycles.
    /// CR 205.3m.</summary>
    God,
    /// <summary>Ranger creature subtype — Ranger-Captain of Eos (Modern
    /// Horizons), Ranger of Eos (Shards of Alara). CR 205.3m.</summary>
    Ranger,
    /// <summary>Drone creature subtype — Pinnacle Emissary's 1/1 colorless
    /// Drone artifact creature tokens (Edge of Eternities). CR 205.3m.</summary>
    Drone,
    /// <summary>Sphinx creature subtype — Quantum Riddler (Edge of
    /// Eternities) and the broader blue Sphinx tribe (Sphinx of the Steel
    /// Wind, Consecrated Sphinx, Sphinx of Foresight). CR 205.3m.</summary>
    Sphinx,
    /// <summary>Assassin creature subtype — Murderous Redcap (Shadowmoor),
    /// Royal Assassin, Garna, Bloodfist of Keral. CR 205.3m.</summary>
    Assassin,
    /// <summary>Homunculus creature subtype — Fblthp, the Lost (War of the
    /// Spark), and the broader homunculus lineage. CR 205.3m.</summary>
    Homunculus,
    /// <summary>Berserker creature subtype — Bloodbraid Elf (Alara Reborn /
    /// Modern Horizons 2), Goblin Berserker, Lovisa Coldeyes. CR 205.3m.</summary>
    Berserker,
    /// <summary>Kithkin creature subtype — Burrenton Forge-Tender (Lorwyn),
    /// Goldmeadow Stalwart, Wizened Cenn. The Lorwyn / Shadowmoor halfling-
    /// adjacent white lineage. CR 205.3m.</summary>
    Kithkin,
    /// <summary>Ape creature subtype — Treetop Village (Urza's Legacy) animated
    /// form, Kird Ape (Arabian Nights / many reprints). CR 205.3m.</summary>
    Ape,
    /// <summary>Serpent creature subtype — Striped Riverwinder (Hour of
    /// Devastation), Lorthos, the Tidemaker, Quest for Ula's Temple
    /// payoffs. CR 205.3m.</summary>
    Serpent,
    /// <summary>Whale creature subtype — Waker of Waves (Magic 2021 / Modern
    /// Horizons 2), Ula's Temple creature type payoff. CR 205.3m.</summary>
    Whale,
    /// <summary>Imp creature subtype — Vault Skirge (New Phyrexia), Ravenous Rats'
    /// adjacent imp lineage (Hypnotic Specter / Mind Twist-adjacent). CR 205.3m.</summary>
    Imp,
    /// <summary>Troll creature subtype — Golgari Grave-Troll (Ravnica: City
    /// of Guilds), Trygon Predator-adjacent Golgari lineage, Phyrexian
    /// Obliterator-pair Phyrexian Troll synergies. CR 205.3m.</summary>
    Troll,
    /// <summary>Thopter artifact-creature subtype — Ornithopter (Antiquities),
    /// Sai, Master Thopterist, Thopter Foundry / Sword of the Meek, Whirler
    /// Rogue tokens. CR 205.3m.</summary>
    Thopter,
    /// <summary>Treefolk creature subtype — Generous Ent (The Lord of the
    /// Rings: Tales of Middle-earth), Treefolk Harbinger, Doran, the
    /// Siege Tower. Historically paired with Forestcycling and
    /// toughness-matters payoffs. CR 205.3m.</summary>
    Treefolk,
    /// <summary>Naga creature subtype — Ramunap Excavator (Hour of Devastation),
    /// Hapatra, Vizier of Poisons, the Amonkhet snake-people lineage. CR 205.3m.</summary>
    Naga,
    /// <summary>Vedalken creature subtype — Master of Etherium (Shards of
    /// Alara), Vedalken Shackles, Trinket Mage's adjacent Mirrodin /
    /// Alara blue-aligned artificer lineage. CR 205.3m.</summary>
    Vedalken,
    /// <summary>Elder creature subtype — Atarka, World Render and the rest
    /// of the Dragons of Tarkir Elder Dragon cycle, Etali, Primal Storm
    /// (Elder Dinosaur), Nicol Bolas's pre-planeswalker printings. CR 205.3m.</summary>
    Elder,
    /// <summary>Arcane spell subtype — Champions of Kamigawa block (Desperate
    /// Ritual, Goryo's Vengeance, Through the Breach, Glacial Ray, …). The
    /// only subtype that exists for instant / sorcery spells in the engine;
    /// gates <see cref="Majik.Core.Costs.SpliceOntoArcaneCost"/> (CR 702.46 —
    /// splice rider may only attach to spells with the Arcane subtype).
    /// CR 205.3k.</summary>
    Arcane,

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
    /// <summary>Curse enchantment subtype — CR 205.3h. Aura subtype shared
    /// by the Innistrad/Shadows-over-Innistrad Curse cycle (Trespasser's
    /// Curse, Curse of Misfortunes, ...). Always paired with the
    /// Enchantment type + Aura subtype.</summary>
    Curse,
    /// <summary>Class enchantment subtype — CR 716. Multi-level enchantment
    /// shape (Stormchaser's Talent, Modern Horizons 3 and the Adventures in
    /// the Forgotten Realms cycle). CR 205.3h.</summary>
    Class,

    // Artifact subtypes (examples)
    Equipment,
    Vehicle,
    Food,
    Treasure,
    Clue,
    Construct,
    /// <summary>Assembly-Worker creature subtype — Mishra's Factory and the
    /// Antiquities Assembly-Worker cycle. CR 205.3m. Engine spelling uses
    /// the unhyphenated identifier; observable card text still reads
    /// "Assembly-Worker".</summary>
    AssemblyWorker,
    /// <summary>Golem creature/artifact subtype — Sundering Titan, Wurmcoil
    /// Engine, Triskelion. Almost always paired with the Artifact type.
    /// CR 205.3g + CR 205.3m.</summary>
    Golem,
    Blood,
    Powerstone,
    /// <summary>Myr creature/artifact subtype — Myr Enforcer, Myr Retriever,
    /// the Mirrodin Myr cycle. Always paired with the Artifact type.
    /// CR 205.3g + CR 205.3m.</summary>
    Myr,
    /// <summary>Servo creature/artifact subtype — Kaladesh Servo tokens
    /// (Animation Module, Whirlermaker, Visionary Augmenter). Always paired
    /// with the Artifact type. CR 205.3g + CR 205.3m.</summary>
    Servo,
    /// <summary>Pest creature subtype — Mirrodin Besieged's Signal Pest,
    /// Strixhaven's Pest tokens. CR 205.3m.</summary>
    Pest,

    // Eldrazi creature subtypes (CR 205.3m)
    Eldrazi,
    Spawn,
    Scion,

    /// <summary>Fungus creature subtype — Sporecrown Thallid, Sowing
    /// Mycospawn (an Eldrazi Fungus from MH3), Thelon of Havenwood,
    /// the Saproling-producing Thallid cycle from Fallen Empires /
    /// Time Spiral. CR 205.3m.</summary>
    Fungus,

    /// <summary>Spike creature subtype — Urza's Saga / Exodus cycle of
    /// counter-shifting creatures (Spike Feeder, Spike Weaver, Spike
    /// Tiller). CR 205.3m.</summary>
    Spike,

    // Planeswalker subtypes (examples)
    Ajani,
    Ashiok,
    Chandra,
    Grist,
    Jace,
    Liliana,
    Narset,
    Garruk,
    Nissa,
    Teferi,
    Karn,
    Ugin,
    Bolas,
    Wrenn,
    Oko,
    /// <summary>Tamiyo planeswalker subtype — Tamiyo, Seasoned Scholar
    /// (back face of Tamiyo, Inquisitive Student). CR 205.3m.</summary>
    Tamiyo,

    /// <summary>Homarid creature subtype — Hedron Crab, Homarid. CR 205.3m.
    /// Crustacean-flavoured creature type that originated in Fallen Empires
    /// and was reused by Zendikar's Hedron Crab.</summary>
    Homarid,

    /// <summary>Crab creature subtype — Ancient Crab (Amonkhet), Horseshoe
    /// Crab (Tempest), Returned Pastcaller (adjacent Theros crustacean flavour).
    /// CR 205.3m.</summary>
    Crab,

    /// <summary>Centaur creature subtype — Loxodon Smiter (Return to Ravnica),
    /// Territorial Centaur, Centaur Courser. CR 205.3m.</summary>
    Centaur,
    /// <summary>Djinn creature subtype — Mahamoti Djinn (Alpha/Beta/many reprints),
    /// Serendib Djinn, Dungeon Geists. CR 205.3m.</summary>
    Djinn,
    /// <summary>Elephant creature subtype — Elephant token (from Creeping
    /// Renaissance, Trumpet Blast), Trained Armodon. CR 205.3m.</summary>
    Elephant,
    /// <summary>Griffin creature subtype — Griffin Sentinel (Magic 2012),
    /// Numot, the Devastator, Pride of the Clouds. CR 205.3m.</summary>
    Griffin,
    /// <summary>Ogre creature subtype — Marauding Ogre (Core sets), Hellhole
    /// Flailer (Return to Ravnica), Shattergang Brothers. CR 205.3m.</summary>
    Ogre,
    /// <summary>Ox creature subtype — Ox of Agonas (Theros Beyond Death),
    /// Yoked Ox (Theros). CR 205.3m.</summary>
    Ox,
    /// <summary>Rat creature subtype — Pack Rat (Return to Ravnica), Rat Colony
    /// (Dominaria), Swarm of Rats. CR 205.3m.</summary>
    Rat,
    /// <summary>Spider creature subtype — Giant Spider (Core sets), Sentinel
    /// Spider (Magic 2013), Ishkanah, Grafwidow. CR 205.3m.</summary>
    Spider,

    /// <summary>Archon creature subtype — Archon of Cruelty (Modern Horizons 2),
    /// Archon of Justice, Archon of the Triumvirate. CR 205.3m.</summary>
    Archon,

    /// <summary>Utrom creature subtype — Krang, Master Mind (Universes Beyond:
    /// Teenage Mutant Ninja Turtles). The Utroms are an alien brain-shaped
    /// species from the TMNT universe; Krang is the most prominent. CR 205.3m.</summary>
    Utrom,

    /// <summary>Shapeshifter creature subtype — Mutable Explorer, Cryptoplasm,
    /// Mirror Entity. The flavour-canonical home of the Changeling keyword
    /// (CR 702.73) and Mutavault's "every creature type" animate. CR 205.3m.</summary>
    Shapeshifter,

    /// <summary>Blinkmoth creature subtype — Blinkmoth Nexus (Darksteel /
    /// Modern Masters) animates into a "1/1 Blinkmoth artifact creature",
    /// and its third ability pumps "target Blinkmoth creature" (so the
    /// subtype is that ability's target filter). Also Blinkmoth Well,
    /// Blinkmoth token producers in the Mirrodin lineage. CR 205.3m.</summary>
    Blinkmoth,
}
