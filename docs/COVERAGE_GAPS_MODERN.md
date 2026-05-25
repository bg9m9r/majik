# Coverage gaps — mechanic-cluster backlog

- **Scope:** format=modern, dedup-by-name
- **Generated:** 2026-05-25 03:13 UTC
- **Unimplemented total:** 15868
- **Min cluster size:** 5
- **Clusters ≥ threshold:** 205 (rendering top 50)
- **Cards in above-threshold clusters:** 2176 (13.7% of unimplemented)
- **Cards in rendered top-50:** 1190 (7.5% of unimplemented)
- **Long-tail cards (below threshold):** 13692

## Ranked clusters

| Rank | Count | Suggested binder | Signature |
|---:|---:|---|---|
| 1 | 98 | _(none)_ | `equipped creature gets +n/+n` |
| 2 | 92 | ActivatedGenericBinder | `{cost}: add {cost}` |
| 3 | 76 | ActivatedSelfPumpBinder | `{cost}: ~ gets +n/+n until end of turn` |
| 4 | 47 | ActivatedGenericBinder | `{cost}: add one mana of any color` |
| 5 | 42 | _(none)_ | `this spell can't be countered` |
| 6 | 41 | ActivatedGenericBinder | `{cost}: add {cost} or {cost}` |
| 7 | 39 | _(none)_ | `enchant creature enchanted creature gets +n/+n` |
| 8 | 33 | _(none)_ | `~ can't block` |
| 9 | 32 | _(none)_ | `~ enters tapped` |
| 10 | 31 | _(none)_ | `flying {cost}: ~ gets +n/+n until end of turn` |
| 11 | 29 | EtbGainLifeTriggerBinder | `when ~ enters, you gain n life` |
| 12 | 29 | _(none)_ | `~ can't be blocked` |
| 13 | 26 | _(none)_ | `creatures you control get +n/+n until end of turn` |
| 14 | 26 | EtbGenericTriggerBinder | `when ~ enters, you get {cost}` |
| 15 | 24 | EtbDrawCardTriggerBinder | `when ~ enters, draw a card` |
| 16 | 24 | _(none)_ | `{cost}, {cost}: tap target creature` |
| 17 | 20 | ActivatedGenericBinder | `{cost}: regenerate ~` |
| 18 | 19 | _(none)_ | `enchant creature enchanted creature gets -n/-n` |
| 19 | 19 | EtbScryTriggerBinder | `when ~ enters, scry n` |
| 20 | 19 | _(none)_ | `~ attacks each combat if able` |
| 21 | 19 | _(none)_ | `~ enters with x +n/+n counters on it` |
| 22 | 18 | _(none)_ | `enchant creature enchanted creature can't attack or block` |
| 23 | 18 | _(none)_ | `this artifact enters tapped` |
| 24 | 18 | ActivatedGenericBinder | `{cost}: add {cost}, {cost}, or {cost}` |
| 25 | 17 | _(none)_ | `enchant creature when this aura enters, tap enchanted creature` |
| 26 | 17 | _(none)_ | `if ~ is in your opening hand, you may begin the game with it on the battlefield` |
| 27 | 17 | _(none)_ | `when this equipment enters, attach it to target creature you control` |
| 28 | 17 | _(none)_ | `you may exert ~ as it attacks` |
| 29 | 16 | EtbGenericTriggerBinder | `when ~ enters, mill three cards` |
| 30 | 16 | _(none)_ | `you may look at the top card of your library any time` |
| 31 | 15 | _(none)_ | `flying ~ can block only creatures with flying` |
| 32 | 15 | ActivatedGenericBinder | `{cost}: ~ gains flying until end of turn` |
| 33 | 14 | _(none)_ | `creatures you control get +n/+n` |
| 34 | 14 | _(none)_ | `enchant creature enchanted creature gets +n/+n and has flying` |
| 35 | 14 | _(none)_ | `flash when this equipment enters, attach it to target creature you control` |
| 36 | 14 | EtbCounterTriggerBinder | `when ~ enters, put a +n/+n counter on target creature` |
| 37 | 13 | _(none)_ | `enchant creature you control enchanted creature` |
| 38 | 13 | _(none)_ | `when this artifact enters, draw a card` |
| 39 | 13 | EtbCreateTokenTriggerBinder | `when ~ enters, create a food token` |
| 40 | 13 | ActivatedGenericBinder | `{cost}: adapt n` |
| 41 | 12 | _(none)_ | `enchant creature when this aura enters, draw a card` |
| 42 | 12 | _(none)_ | `hexproof` |
| 43 | 12 | _(none)_ | `{cost}, {cost}: draw a card, then discard a card` |
| 44 | 11 | _(none)_ | `flying ~ can't block` |
| 45 | 11 | _(none)_ | `gain control of target creature until end of turn` |
| 46 | 11 | _(none)_ | `kinship — at the beginning of your upkeep, you may look at the top card of your…` |
| 47 | 11 | EtbCreateTokenTriggerBinder | `when ~ enters, create a treasure token` |
| 48 | 11 | _(none)_ | `{cost}, {cost}: add {cost}` |
| 49 | 11 | _(none)_ | `~ doesn't untap during your untap step` |
| 50 | 11 | _(none)_ | `~ enters with a +n/+n counter on it` |

## Cluster detail

### 1. 98 cards — `equipped creature gets +n/+n`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** +2 Mace

  > Equipped creature gets +2/+2. Equip {3}

- **Example cards (up to 20):**
  - +2 Mace
  - Andúril, Flame of the West
  - Argentum Armor
  - Atomic Microsizer
  - Barbed Battlegear
  - Barrow-Blade
  - Bespoke Battlegarb
  - Bladed Bracers
  - Bloodthorn Flail
  - Bone Saw
  - Bonesplitter
  - Bride's Gown
  - Bronze Sword
  - Buster Sword
  - Butcher's Cleaver
  - Captain's Claws
  - Concealed Weapon
  - Cultist's Staff
  - Dancing Sword
  - Deathrender

### 2. 92 cards — `{cost}: add {cost}`

- **Suggested binder:** `ActivatedGenericBinder` — Activated ability — catch-all
- **Trigger signature:** `{cost}:`
- **Canonical example:** Automated Artificer

  > {T}: Add {C}. Spend this mana only to activate an ability or cast an artifact spell.

- **Example cards (up to 20):**
  - Automated Artificer
  - Avacyn's Pilgrim
  - Boreal Druid
  - Carnelian Orb of Dragonkind
  - Chandra's Embercat
  - Chronatog Totem
  - Copper Myr
  - Creeping Peeper
  - Cursed Mirror
  - Dalakos, Crafter of Wonders
  - Devoted Druid
  - Dreamstone Hedron
  - Druid of the Cowl
  - Dungeon Map
  - Elfhame Druid
  - Elves of Deep Shadow
  - Elvish Mystic
  - Fabrication Foundry
  - Fanatic of Rhonas
  - Foriysian Totem

### 3. 76 cards — `{cost}: ~ gets +n/+n until end of turn`

- **Suggested binder:** `ActivatedSelfPumpBinder` — Activated — self pump EOT
- **Trigger signature:** `{cost}:`
- **Effect verb:** `pump (+N/+N EOT)`
- **Canonical example:** Augmenting Automaton

  > {1}{B}: This creature gets +1/+1 until end of turn.

- **Example cards (up to 20):**
  - Augmenting Automaton
  - Bellows Lizard
  - Blazing Rootwalla
  - Bold Impaler
  - Boreal Centaur
  - Canyon Crab
  - Cavern Thoctar
  - Darkthicket Wolf
  - Devkarin Dissident
  - Dread Shade
  - Dross Ripper
  - Evernight Shade
  - Fathom Fleet Firebrand
  - Fiery Hellhound
  - Flamekin Brawler
  - Flowstone Crusher
  - Flowstone Shambler
  - Foxfire Oak
  - Frilled Oculus
  - Frilled Sandwalla

### 4. 47 cards — `{cost}: add one mana of any color`

- **Suggested binder:** `ActivatedGenericBinder` — Activated ability — catch-all
- **Trigger signature:** `{cost}:`
- **Canonical example:** Accomplished Alchemist

  > {T}: Add one mana of any color. {T}: Add X mana of any one color, where X is the amount of life you gained this turn.

- **Example cards (up to 20):**
  - Accomplished Alchemist
  - All-Fates Scroll
  - Alloy Myr
  - Atzocan Seer
  - Barrels of Blasting Jelly
  - Blitzball
  - Coalition Relic
  - Cultivator's Caravan
  - Draconic Disciple
  - Fountain of Ichor
  - Gravestone Strider
  - Guy in the Chair
  - Hardbristle Bandit
  - Herd Heirloom
  - Hermitic Herbalist
  - Honored Heirloom
  - Humble Naturalist
  - Ilysian Caryatid
  - Intrepid Paleontologist
  - Ixalli's Lorekeeper

### 5. 42 cards — `this spell can't be countered`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Absolute Virtue

  > This spell can't be countered. Flying You have protection from each of your opponents.

- **Example cards (up to 20):**
  - Absolute Virtue
  - Akroma, Angel of Fury
  - Altered Ego
  - Balustrade Wurm
  - Carnage Tyrant
  - Caught Red-Handed
  - Chandra, Awakened Inferno
  - Curator of Destinies
  - Dragonlord Dromoka
  - Emrakul, the Aeons Torn
  - Frenzied Baloth
  - Gaea's Revenge
  - Great Sable Stag
  - Hexing Squelcher
  - Inferno of the Star Mounts
  - Isao, Enlightened Bushi
  - Koma, Cosmos Serpent
  - Koma, World-Eater
  - Last March of the Ents
  - Lightning Mare

### 6. 41 cards — `{cost}: add {cost} or {cost}`

- **Suggested binder:** `ActivatedGenericBinder` — Activated ability — catch-all
- **Trigger signature:** `{cost}:`
- **Canonical example:** Atarka Monument

  > {T}: Add {R} or {G}. {4}{R}{G}: This artifact becomes a 4/4 red and green Dragon artifact creature with flying until end of turn.

- **Example cards (up to 20):**
  - Atarka Monument
  - Avid Reclaimer
  - Azorius Cluestone
  - Azorius Keyrune
  - Azorius Locket
  - Boros Cluestone
  - Boros Keyrune
  - Boros Locket
  - Deathcap Cultivator
  - Dimir Cluestone
  - Dimir Keyrune
  - Dimir Locket
  - Dromoka Monument
  - Golgari Cluestone
  - Golgari Keyrune
  - Golgari Locket
  - Gruul Cluestone
  - Gruul Keyrune
  - Gruul Locket
  - Haunted Screen

### 7. 39 cards — `enchant creature enchanted creature gets +n/+n`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Abzan Runemark

  > Enchant creature Enchanted creature gets +2/+2. Enchanted creature has vigilance as long as you control a black or green permanent.

- **Example cards (up to 20):**
  - Abzan Runemark
  - Bestial Bloodline
  - Boar Umbra
  - Boon of Emrakul
  - Celestial Mantle
  - Conviction
  - Demonic Vigor
  - Eland Umbra
  - Equestrian Skill
  - Face of Divinity
  - Holy Strength
  - Illusionary Armor
  - Immolation
  - Jeskai Runemark
  - Knight's Pledge
  - Mantle of the Wolf
  - Mardu Runemark
  - Mogis's Favor
  - Moldervine Cloak
  - Oakenform

### 8. 33 cards — `~ can't block`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Ashenmoor Gouger

  > This creature can't block.

- **Example cards (up to 20):**
  - Ashenmoor Gouger
  - Auntie's Snitch
  - Blood Hypnotist
  - Bloodbraid Marauder
  - Bloodsoaked Champion
  - Bog Hoodlums
  - Bojuka Brigand
  - Carrion Feeder
  - Clattering Augur
  - Despoiler of Souls
  - Forsaken Miner
  - Fretwork Colony
  - Goblin Raider
  - Gravecrawler
  - Hagra Crocodile
  - Headstrong Brute
  - Hulking Cyclops
  - Norin, Swift Survivalist
  - Ogre Taskmaster
  - Postmortem Professor

### 9. 32 cards — `~ enters tapped`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Alirios, Enraptured

  > Alirios enters tapped. Alirios doesn't untap during your untap step if you control a Reflection. When Alirios enters, create a 3/2 blue Reflection creature token.

- **Example cards (up to 20):**
  - Alirios, Enraptured
  - Alley Assailant
  - Boseiju, Who Shelters All
  - Brackish Trudge
  - Clay Revenant
  - Crooked Custodian
  - Cult Conscript
  - Deep-Slumber Titan
  - Diregraf Ghoul
  - Dread Wanderer
  - Dungeon Crawler
  - Embraal Bruiser
  - Eternal Taskmaster
  - Forgotten Sentinel
  - Gutterbones
  - Hall of the Bandit Lord
  - Karn's Sylex
  - Magus of the Disk
  - Mardu Skullhunter
  - Nyx Lotus

### 10. 31 cards — `flying {cost}: ~ gets +n/+n until end of turn`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Trigger signature:** `flying {cost}:`
- **Effect verb:** `pump (+N/+N EOT)`
- **Canonical example:** Arcbound Whelp

  > Flying {R}: This creature gets +1/+0 until end of turn. Modular 2

- **Example cards (up to 20):**
  - Arcbound Whelp
  - Aven Flock
  - Blistering Dieflyn
  - Chilling Shade
  - Darklit Gargoyle
  - Deathknell Kami
  - Dragon Hatchling
  - Dragon Whelp
  - Drifting Shade
  - Furnace Whelp
  - Hellkite Punisher
  - Inkrise Infiltrator
  - Marble Gargoyle
  - Metropolis Sprite
  - Moltensteel Dragon
  - Moonwing Moth
  - Mordant Dragon
  - Nightwing Shade
  - Paragon of Modernity
  - Pardic Dragon

### 11. 29 cards — `when ~ enters, you gain n life`

- **Suggested binder:** `EtbGainLifeTriggerBinder` — ETB triggered ability — controller gains N life
- **Trigger signature:** `when ~ enters,`
- **Effect verb:** `gain life`
- **Canonical example:** Arashin Cleric

  > When this creature enters, you gain 3 life.

- **Example cards (up to 20):**
  - Arashin Cleric
  - Bulwark Giant
  - Cathedral Sanctifier
  - Centaur Healer
  - Centaur Nurturer
  - Circuit Mender
  - Dawnhart Rejuvenator
  - Filigree Familiar
  - Germinating Wurm
  - Healer of the Glade
  - Hill Giant Herdgorger
  - Honey Mammoth
  - Inspiring Cleric
  - Lone Missionary
  - Loxodon Hierarch
  - Mindful Biomancer
  - Oasis Gardener
  - Obstinate Baloth
  - Oil-Gorger Troll
  - Peace Strider

### 12. 29 cards — `~ can't be blocked`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Azorius Herald

  > This creature can't be blocked. When this creature enters, you gain 4 life. When this creature enters, sacrifice it unless {U} was spent to cast it.

- **Example cards (up to 20):**
  - Azorius Herald
  - Deep-Sea Kraken
  - Dimir Infiltrator
  - Elusive Krasis
  - Etrata, the Silencer
  - Ferropede
  - Ghastlord of Fugue
  - Gudul Lurker
  - Hunted Phantasm
  - Jhessian Infiltrator
  - Keymaster Rogue
  - Latch Seeker
  - Mercurial Spelldancer
  - Mist-Cloaked Herald
  - Mystic of the Hidden Way
  - Neurok Invisimancer
  - Phantom Ninja
  - Phantom Warrior
  - Plasma Elemental
  - River Sneak

### 13. 26 cards — `creatures you control get +n/+n until end of turn`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Effect verb:** `pump (+N/+N EOT)`
- **Canonical example:** Banners Raised

  > Creatures you control get +1/+0 until end of turn.

- **Example cards (up to 20):**
  - Banners Raised
  - Bar the Door
  - Break of Day
  - Burn Bright
  - Charge
  - Ethereal Guidance
  - Glorious Charge
  - Great Teacher's Decree
  - Inspired Charge
  - Kytheon's Tactics
  - Noxious Assault
  - Outlaws' Fury
  - Path of Anger's Flame
  - Predatory Rampage
  - Rally the Peasants
  - Rally to Battle
  - Rallying Roar
  - Righteous Charge
  - Sanctified Charge
  - Solidarity

### 14. 26 cards — `when ~ enters, you get {cost}`

- **Suggested binder:** `EtbGenericTriggerBinder` — ETB triggered ability — catch-all
- **Trigger signature:** `when ~ enters,`
- **Canonical example:** Aether Herder

  > When this creature enters, you get {E}{E} . Whenever this creature attacks, you may pay {E}{E}. If you do, create a 1/1 colorless Servo artifact creature token.

- **Example cards (up to 20):**
  - Aether Herder
  - Aether Theorist
  - Aethertorch Renegade
  - Bristling Hydra
  - Conduit Goblin
  - Consul's Shieldguard
  - Electrostatic Pummeler
  - Emissary of Soulfire
  - Hexgold Slith
  - Janjeet Sentry
  - Maulfist Doorbuster
  - Minister of Inquiries
  - Multiform Wonder
  - Sage of Shaila's Claim
  - Servant of the Conduit
  - Shipwreck Moray
  - Solstice Zealot
  - Spontaneous Artist
  - Tempest Harvester
  - Thriving Grubs

### 15. 24 cards — `when ~ enters, draw a card`

- **Suggested binder:** `EtbDrawCardTriggerBinder` — ETB triggered ability — draw one card
- **Trigger signature:** `when ~ enters,`
- **Effect verb:** `draw a card`
- **Canonical example:** Conciliator's Duelist

  > When this creature enters, draw a card. Each player loses 1 life. Repartee — Whenever you cast an instant or sorcery spell that targets a creature, exile up to one target creature. Return that card to the battlefield under its owner's control at the beginning of the next end step.

- **Example cards (up to 20):**
  - Conciliator's Duelist
  - Didact Echo
  - Elvish Visionary
  - Errand-Rider of Gondor
  - Fblthp, the Lost
  - Gallant Citizen
  - Generous Stray
  - Haru-Onna
  - Helpful Hunter
  - Joraga Visionary
  - Kavu Climber
  - Llanowar Visionary
  - Masked Admirers
  - Merchant of Secrets
  - Nimble Innovator
  - Pond Prophet
  - Proft's Eidetic Memory
  - Rhox Oracle
  - Sarulf's Packmate
  - Shaman of Spring

### 16. 24 cards — `{cost}, {cost}: tap target creature`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Effect verb:** `tap target`
- **Canonical example:** Akroan Jailer

  > {2}{W}, {T}: Tap target creature.

- **Example cards (up to 20):**
  - Akroan Jailer
  - Akroan Mastiff
  - Blinding Mage
  - Blinding Souleater
  - Celestial Enforcer
  - Checkpoint Officer
  - Crown of Empires
  - Elite Arrester
  - Fan Bearer
  - Frostbridge Guard
  - Gavony Trapper
  - Gideon's Lawkeeper
  - Goldmeadow Harrier
  - Holy Justiciar
  - Hylda's Crown of Winter
  - Innocence Kami
  - Loxodon Mystic
  - Master Decoy
  - Nebelgast Beguiler
  - Ostiary Thrull

### 17. 20 cards — `{cost}: regenerate ~`

- **Suggested binder:** `ActivatedGenericBinder` — Activated ability — catch-all
- **Trigger signature:** `{cost}:`
- **Canonical example:** Ancient Silverback

  > {G}: Regenerate this creature.

- **Example cards (up to 20):**
  - Ancient Silverback
  - Asphodel Wanderer
  - Augur of Skulls
  - Cudgel Troll
  - Drudge Skeletons
  - Dutiful Thrull
  - Horned Troll
  - Kin-Tree Warden
  - Marang River Skeleton
  - Odious Trow
  - Pewter Golem
  - Revered Dead
  - Rimebound Dead
  - Selesnya Sentry
  - Skeletal Wurm
  - Tangle Hulk
  - Tel-Jilad Exile
  - Twisted Abomination
  - Uthden Troll
  - Votary of the Conclave

### 18. 19 cards — `enchant creature enchanted creature gets -n/-n`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Agoraphobia

  > Enchant creature Enchanted creature gets -5/-0. {2}{U}: Return this Aura to its owner's hand.

- **Example cards (up to 20):**
  - Agoraphobia
  - Chant of the Skifsang
  - Clinging Darkness
  - Cryoshatter
  - Dead Weight
  - Debilitating Injury
  - Defensive Stance
  - Dwindle
  - Enfeeblement
  - Failed Conversion
  - Mire's Grasp
  - Pin to the Earth
  - Screams from Within
  - Sensory Deprivation
  - Shattered Ego
  - Stab Wound
  - Weakness
  - Weight of the Underworld
  - World-Weary

### 19. 19 cards — `when ~ enters, scry n`

- **Suggested binder:** `EtbScryTriggerBinder` — ETB triggered ability — scry N
- **Trigger signature:** `when ~ enters,`
- **Effect verb:** `scry`
- **Canonical example:** April O'Neil, Kunoichi Trainee

  > When April O'Neil enters, scry 2. April O'Neil can't be blocked by creatures with power 3 or greater.

- **Example cards (up to 20):**
  - April O'Neil, Kunoichi Trainee
  - Automatic Librarian
  - Cavern Stomper
  - Chrome Cat
  - Cloudspire Coordinator
  - Fortune, Loyal Steed
  - Galadhrim Guide
  - Haunt of the Dead Marshes
  - Inga Rune-Eyes
  - Inquisitive Puppet
  - Lost Legion
  - Mardu Devotee
  - Myr Custodian
  - Octoprophet
  - Omenspeaker
  - Prophet of the Peak
  - Rumbling Sentry
  - Sage's Row Savant
  - Veteran Motorist

### 20. 19 cards — `~ attacks each combat if able`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Berserkers of Blood Ridge

  > This creature attacks each combat if able.

- **Example cards (up to 20):**
  - Berserkers of Blood Ridge
  - Bloodcrazed Neonate
  - Bloodrock Cyclops
  - Crazed Goblin
  - Deathbellow Raider
  - Frontline Rebel
  - Galvanic Juggernaut
  - Goblin Brigand
  - Insatiable Gorgers
  - Juggernaut
  - Kill-Suit Cultist
  - Monstrous Carabid
  - Phyrexian Snowcrusher
  - Ramroller
  - Rubblebelt Recluse
  - Sabertooth Alley Cat
  - Sprinting Warbrute
  - Tattermunge Maniac
  - Underworld Rage-Hound

### 21. 19 cards — `~ enters with x +n/+n counters on it`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Apocalypse Hydra

  > This creature enters with X +1/+1 counters on it. If X is 5 or more, it enters with an additional X +1/+1 counters on it. {1}{R}, Remove a +1/+1 counter from this creature: It deals 1 damage to any target.

- **Example cards (up to 20):**
  - Apocalypse Hydra
  - Broodguard Elite
  - Contortionist Troupe
  - Endless One
  - Feral Hydra
  - Hangarback Walker
  - Hooded Hydra
  - Hungering Hydra
  - Ivy Elemental
  - Maga, Traitor to Mortals
  - Magma Pummeler
  - Marketback Walker
  - Mikaeus, the Lunarch
  - Primordial Hydra
  - Protean Hydra
  - Steelbane Hydra
  - Ugin's Conjurant
  - Vastwood Hydra
  - Wildwood Scourge

### 22. 18 cards — `enchant creature enchanted creature can't attack or block`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Bound in Silence

  > Enchant creature Enchanted creature can't attack or block.

- **Example cards (up to 20):**
  - Bound in Silence
  - Cage of Hands
  - Captured by Lagacs
  - Caught in the Brights
  - Choking Restraints
  - Compulsory Rest
  - Cooped Up
  - Crystallization
  - Detained by Legionnaires
  - Dreadful Apathy
  - Luminous Bonds
  - Pacifism
  - Path to Redemption
  - Pillory of the Sleepless
  - Recumbent Bliss
  - Sigarda's Imprisonment
  - Uneasy Alliance
  - Utopia Vow

### 23. 18 cards — `this artifact enters tapped`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Altar of the Lost

  > This artifact enters tapped. {T}: Add two mana in any combination of colors. Spend this mana only to cast spells with flashback from a graveyard.

- **Example cards (up to 20):**
  - Altar of the Lost
  - Coldsteel Heart
  - Corrupted Grafstone
  - Deadlock Trap
  - Door to Nothingness
  - Elixir
  - Firemind Vessel
  - Guardian Idol
  - Nevinyrral's Disk
  - Renegade Map
  - Solar Transformer
  - Spare Supplies
  - Star Compass
  - Terrarion
  - Warden of the Wall
  - White Lotus Tile
  - Wizard's Rockets
  - Worn Powerstone

### 24. 18 cards — `{cost}: add {cost}, {cost}, or {cost}`

- **Suggested binder:** `ActivatedGenericBinder` — Activated ability — catch-all
- **Trigger signature:** `{cost}:`
- **Canonical example:** Abzan Banner

  > {T}: Add {W}, {B}, or {G}. {W}{B}{G}, {T}, Sacrifice this artifact: Draw a card.

- **Example cards (up to 20):**
  - Abzan Banner
  - Abzan Devotee
  - Druid of the Anima
  - Indatha Crystal
  - Jeskai Banner
  - Ketria Crystal
  - Mardu Banner
  - Obelisk of Bant
  - Obelisk of Esper
  - Obelisk of Grixis
  - Obelisk of Jund
  - Obelisk of Naya
  - Rattleclaw Mystic
  - Raugrin Crystal
  - Savai Crystal
  - Sultai Banner
  - Temur Banner
  - Zagoth Crystal

### 25. 17 cards — `enchant creature when this aura enters, tap enchanted creature`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Bewitching Leechcraft

  > Enchant creature When this Aura enters, tap enchanted creature. Enchanted creature has "If this creature would untap during your untap step, remove a +1/+1 counter from it instead. If you do, untap it."

- **Example cards (up to 20):**
  - Bewitching Leechcraft
  - Bind the Monster
  - Bitter Chill
  - Castaway's Despair
  - Charmed Sleep
  - Claustrophobia
  - Colossification
  - Dramatic Accusation
  - Melancholy
  - Ringing Strike Mastery
  - Singing Bell Strike
  - Sleep Magic
  - Sleep Paralysis
  - Starlight Snare
  - Stay Hidden, Stay Silent
  - Waterknot
  - Winter's Rest

### 26. 17 cards — `if ~ is in your opening hand, you may begin the game with it on the battlefield`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Leyline Axe

  > If this card is in your opening hand, you may begin the game with it on the battlefield. Equipped creature gets +1/+1 and has double strike and trample. Equip {3}

- **Example cards (up to 20):**
  - Leyline Axe
  - Leyline of Abundance
  - Leyline of Anticipation
  - Leyline of Combustion
  - Leyline of Hope
  - Leyline of Lifeforce
  - Leyline of Lightning
  - Leyline of Mutation
  - Leyline of Punishment
  - Leyline of Resonance
  - Leyline of Sanctity
  - Leyline of Singularity
  - Leyline of Transformation
  - Leyline of Vitality
  - Leyline of the Guildpact
  - Leyline of the Meek
  - Leyline of the Void

### 27. 17 cards — `when this equipment enters, attach it to target creature you control`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Trigger signature:** `when this equipment enters,`
- **Canonical example:** Baseball Bat

  > When this Equipment enters, attach it to target creature you control. Equipped creature gets +1/+1. Whenever equipped creature attacks, tap up to one target creature. Equip {3}

- **Example cards (up to 20):**
  - Baseball Bat
  - Biorganic Carapace
  - Bramble Armor
  - Cliffhaven Kitesail
  - Hookblade
  - Hunter's Bow
  - Maul of the Skyclaves
  - Meltstrider's Gear
  - Mind Carver
  - Novel Nunchaku
  - Piston Sledge
  - Ravager's Mace
  - Relic Axe
  - Scavenged Blade
  - Skyclave Pick-Axe
  - Thunder Lasso
  - Utility Knife

### 28. 17 cards — `you may exert ~ as it attacks`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Ahn-Crop Champion

  > You may exert this creature as it attacks. When you do, untap all other creatures you control.

- **Example cards (up to 20):**
  - Ahn-Crop Champion
  - Battlefield Scavenger
  - Bitterblade Warrior
  - Champion of Rhonas
  - Devoted Crop-Mate
  - Emberhorn Minotaur
  - Glory-Bound Initiate
  - Gust Walker
  - Hooded Brawler
  - Hydra Trainer
  - Oketra's Avenger
  - Resolute Survivors
  - Rhet-Crop Spearmaster
  - Rhonas's Stalwart
  - Trueheart Twins
  - Vizier of the True
  - Watchful Naga

### 29. 16 cards — `when ~ enters, mill three cards`

- **Suggested binder:** `EtbGenericTriggerBinder` — ETB triggered ability — catch-all
- **Trigger signature:** `when ~ enters,`
- **Canonical example:** Aftermath Analyst

  > When this creature enters, mill three cards. {3}{G}, Sacrifice this creature: Return all land cards from your graveyard to the battlefield tapped.

- **Example cards (up to 20):**
  - Aftermath Analyst
  - Ainok Wayfarer
  - Blanchwood Prowler
  - Dreadhound
  - Eerie Soultender
  - Fallaji Archaeologist
  - Glowspore Shaman
  - Necromancer's Assistant
  - Ostrich-Horse
  - Paramecia Coloniex
  - Patient Naturalist
  - Ravenous Gigamole
  - Seedship Broodtender
  - Tomakul Scrapsmith
  - Undead Butler
  - Wick's Patrol

### 30. 16 cards — `you may look at the top card of your library any time`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Assemble the Players

  > You may look at the top card of your library any time. Once each turn, you may cast a creature spell with power 2 or less from the top of your library.

- **Example cards (up to 20):**
  - Assemble the Players
  - Augur of Autumn
  - Bolas's Citadel
  - Crystal Skull, Isu Spyglass
  - Eladamri, Korvecdal
  - Elven Chorus
  - Experimental Frenzy
  - Johann, Apprentice Sorcerer
  - Madame Web, Clairvoyant
  - Mystic Forge
  - One with the Multiverse
  - Precognition Field
  - The Reality Chip
  - Traveling Chocobo
  - Vivien, Monsters' Advocate
  - Vizier of the Menagerie

### 31. 15 cards — `flying ~ can block only creatures with flying`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Cloud Elemental

  > Flying This creature can block only creatures with flying.

- **Example cards (up to 20):**
  - Cloud Elemental
  - Cloud Sprite
  - Etherium Pteramander
  - Hoverguard Observer
  - Long-Finned Skywhale
  - Scrapskin Drake
  - Shacklegeist
  - Skywinder Drake
  - Stormbound Geist
  - Stormcloud Djinn
  - Stratozeppelid
  - Tattered Haunter
  - Vaporkin
  - Wanderlight Spirit
  - Welkin Tern

### 32. 15 cards — `{cost}: ~ gains flying until end of turn`

- **Suggested binder:** `ActivatedGenericBinder` — Activated ability — catch-all
- **Trigger signature:** `{cost}:`
- **Canonical example:** Cobalt Golem

  > {1}{U}: This creature gains flying until end of turn.

- **Example cards (up to 20):**
  - Cobalt Golem
  - Dragon Whisperer
  - Dukhara Peafowl
  - Goblin Balloon Brigade
  - Goblin Bird-Grabber
  - Gust-Skimmer
  - Kor Sky Climber
  - Leaping Master
  - Mantis Engine
  - Patagia Golem
  - Rakdos Pit Dragon
  - Roofstalker Wight
  - Sarcomite Myr
  - Steeple Creeper
  - Stream Hopper

### 33. 14 cards — `creatures you control get +n/+n`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Anthem of Champions

  > Creatures you control get +1/+1.

- **Example cards (up to 20):**
  - Anthem of Champions
  - Collective Blessing
  - Domri, Anarch of Bolas
  - Ethereal Absolution
  - Fortifying Provisions
  - Gaea's Anthem
  - Glorious Anthem
  - In the Trenches
  - Lumithread Field
  - Mirari's Wake
  - Raiders' Spoils
  - Spear of Heliod
  - War Effort
  - Warleader's Call

### 34. 14 cards — `enchant creature enchanted creature gets +n/+n and has flying`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Arcane Flight

  > Enchant creature Enchanted creature gets +1/+1 and has flying.

- **Example cards (up to 20):**
  - Arcane Flight
  - Drake Umbra
  - Elder Mastery
  - Ghostly Wings
  - Griffin Guide
  - Gryff's Boon
  - Magefire Wings
  - Nimbus Wings
  - One With the Wind
  - Shiv's Embrace
  - Skyblade's Boon
  - Smoke Shroud
  - Spectral Flight
  - Wingspan Stride

### 35. 14 cards — `flash when this equipment enters, attach it to target creature you control`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Bladed Battle-Fan

  > Flash When this Equipment enters, attach it to target creature you control. That creature gains indestructible until end of turn. Equipped creature gets +1/+0. Equip {1}

- **Example cards (up to 20):**
  - Bladed Battle-Fan
  - Celestial Armor
  - Coral Sword
  - Dueling Rapier
  - Galadhrim Bow
  - Hidden Blade
  - Hidden Footblade
  - Illvoi Light Jammer
  - Malamet Scythe
  - Mirran Banesplitter
  - Paladin's Shield
  - Quick-Draw Dagger
  - Squire's Lightblade
  - Twin Blades

### 36. 14 cards — `when ~ enters, put a +n/+n counter on target creature`

- **Suggested binder:** `EtbCounterTriggerBinder` — ETB triggered ability — put counters / counter spells
- **Trigger signature:** `when ~ enters,`
- **Canonical example:** Backup Agent

  > When this creature enters, put a +1/+1 counter on target creature.

- **Example cards (up to 20):**
  - Backup Agent
  - Bond Beetle
  - Dauntless Survivor
  - Drix Fatemaker
  - Dueling Coach
  - Duskshell Crawler
  - Ironpaw Aspirant
  - Ironshell Beetle
  - Jeong Jeong's Deserters
  - Obsessive Skinner
  - Sage of the Fang
  - Satyr Grovedancer
  - Tenured Inkcaster
  - Timberland Guide

### 37. 13 cards — `enchant creature you control enchanted creature`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Biting Tether

  > Enchant creature You control enchanted creature. At the beginning of your upkeep, put a -1/-1 counter on enchanted creature.

- **Example cards (up to 20):**
  - Biting Tether
  - Coerced to Kill
  - Corrupted Conscience
  - Domestication
  - Duskmourn's Domination
  - Enslave
  - Illusory Gains
  - Mark of the Oni
  - Mind Control
  - Persuasion
  - Soul Ransom
  - Spirit Away
  - Vapor Snare

### 38. 13 cards — `when this artifact enters, draw a card`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Trigger signature:** `when this artifact enters,`
- **Effect verb:** `draw a card`
- **Canonical example:** Alchemist's Vial

  > When this artifact enters, draw a card. {1}, {T}, Sacrifice this artifact: Target creature can't attack or block this turn.

- **Example cards (up to 20):**
  - Alchemist's Vial
  - Eerie Gravestone
  - Elsewhere Flask
  - Energy Refractor
  - Golden Egg
  - Guidelight Matrix
  - Guild Globe
  - Kaleidostone
  - Omni-Cheese Pizza
  - Potion of Healing
  - Prophetic Prism
  - Sleeper Dart
  - Wedding Invitation

### 39. 13 cards — `when ~ enters, create a food token`

- **Suggested binder:** `EtbCreateTokenTriggerBinder` — ETB triggered ability — token creation
- **Trigger signature:** `when ~ enters,`
- **Effect verb:** `create token`
- **Canonical example:** Bakersbane Duo

  > When this creature enters, create a Food token. Whenever you expend 4, this creature gets +1/+1 until end of turn.

- **Example cards (up to 20):**
  - Bakersbane Duo
  - Cat Collector
  - Eastfarthing Farmer
  - Experimental Confectioner
  - Greta, Sweettooth Scourge
  - Iroh, Tea Master
  - Pizza Face, Gastromancer
  - Provisions Merchant
  - Spider-Ham, Peter Porker
  - Sweettooth Witch
  - Tempting Witch
  - Tough Cookie
  - Unlucky Cabbage Merchant

### 40. 13 cards — `{cost}: adapt n`

- **Suggested binder:** `ActivatedGenericBinder` — Activated ability — catch-all
- **Trigger signature:** `{cost}:`
- **Canonical example:** Benthic Biomancer

  > {1}{U}: Adapt 1. Whenever one or more +1/+1 counters are put on this creature, draw a card, then discard a card.

- **Example cards (up to 20):**
  - Benthic Biomancer
  - Cursed Wombat
  - Evolution Witness
  - Expanding Ooze
  - Fetid Gargantua
  - Growth-Chamber Guardian
  - Knighted Myr
  - Sauroform Hybrid
  - Sharktocrab
  - Skatewing Spy
  - Skitter Eel
  - Temperamental Oozewagg
  - Trollbred Guardian

### 41. 12 cards — `enchant creature when this aura enters, draw a card`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Effect verb:** `draw a card`
- **Canonical example:** Angelic Gift

  > Enchant creature When this Aura enters, draw a card. Enchanted creature has flying.

- **Example cards (up to 20):**
  - Angelic Gift
  - Chosen by Heliod
  - Dragon Mantle
  - Eternity Snare
  - Fate Foretold
  - Grisly Transformation
  - Karametra's Favor
  - Kenrith's Transformation
  - Scourgemark
  - Sheltering Boughs
  - Shielding Plax
  - Stratus Walk

### 42. 12 cards — `hexproof`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Benthic Giant

  > Hexproof

- **Example cards (up to 20):**
  - Benthic Giant
  - Cold-Water Snapper
  - Conifer Strider
  - Gladecover Scout
  - Humongulus
  - Plated Slagwurm
  - Primal Huntbeast
  - Rubbleback Rhino
  - Sacred Wolf
  - Scaled Behemoth
  - Slippery Bogle
  - Wardscale Crocodile

### 43. 12 cards — `{cost}, {cost}: draw a card, then discard a card`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Effect verb:** `draw a card`
- **Canonical example:** Bloodfire Mentor

  > {2}{U}, {T}: Draw a card, then discard a card.

- **Example cards (up to 20):**
  - Bloodfire Mentor
  - Captain of Umbar
  - Collector's Vault
  - Erratic Visionary
  - Facet Reader
  - Grixis Battlemage
  - Qiqirn Merchant
  - Raving Visionary
  - Research Assistant
  - Soothsayer Adept
  - Teferi's Protege
  - Zephyr Scribe

### 44. 11 cards — `flying ~ can't block`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Aesthir Glider

  > Flying This creature can't block.

- **Example cards (up to 20):**
  - Aesthir Glider
  - Bloodfeather Phoenix
  - Daggerclaw Imp
  - Falkenrath Forebear
  - Flameskull
  - Goblin Glider
  - Nightshade Stinger
  - Olivia's Bloodsworn
  - Reckless Imp
  - Vampire Interloper
  - Vampire Soulcaller

### 45. 11 cards — `gain control of target creature until end of turn`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Act of Treason

  > Gain control of target creature until end of turn. Untap that creature. It gains haste until end of turn.

- **Example cards (up to 20):**
  - Act of Treason
  - Awaken the Sleeper
  - Bond of Passion
  - Goatnap
  - Mark of Mutiny
  - Price of Loyalty
  - Shackles of Treachery
  - Traitorous Greed
  - Traitorous Instinct
  - Unexpected Request
  - Unwilling Recruit

### 46. 11 cards — `kinship — at the beginning of your upkeep, you may look at the top card of your library`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Ink Dissolver

  > Kinship — At the beginning of your upkeep, you may look at the top card of your library. If it shares a creature type with this creature, you may reveal it. If you do, each opponent mills three cards.

- **Example cards (up to 20):**
  - Ink Dissolver
  - Kithkin Zephyrnaut
  - Leaf-Crowned Elder
  - Mudbutton Clanger
  - Pyroclast Consul
  - Sensation Gorger
  - Squeaking Pie Grubfellows
  - Wandering Graybeard
  - Waterspout Weavers
  - Winnower Patrol
  - Wolf-Skull Shaman

### 47. 11 cards — `when ~ enters, create a treasure token`

- **Suggested binder:** `EtbCreateTokenTriggerBinder` — ETB triggered ability — token creation
- **Trigger signature:** `when ~ enters,`
- **Effect verb:** `create token`
- **Canonical example:** Brazen Freebooter

  > When this creature enters, create a Treasure token.

- **Example cards (up to 20):**
  - Brazen Freebooter
  - Burdened Aerialist
  - Corsair Captain
  - Kalain, Reclusive Painter
  - Plundering Pirate
  - Professional Wrestler
  - Prosperous Innkeeper
  - Redcap Thief
  - Sailor of Means
  - Skullport Merchant
  - Wily Goblin

### 48. 11 cards — `{cost}, {cost}: add {cost}`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Azorius Signet

  > {1}, {T}: Add {W}{U}.

- **Example cards (up to 20):**
  - Azorius Signet
  - Boros Signet
  - Dimir Signet
  - Golgari Signet
  - Gruul Signet
  - Izzet Signet
  - Knotvine Mystic
  - Orzhov Signet
  - Rakdos Signet
  - Selesnya Signet
  - Simic Signet

### 49. 11 cards — `~ doesn't untap during your untap step`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Battered Golem

  > This creature doesn't untap during your untap step. Whenever an artifact enters, you may untap this creature.

- **Example cards (up to 20):**
  - Battered Golem
  - Famished Paladin
  - Farmstead Gleaner
  - Goblin War Wagon
  - Lurking Roper
  - Mage-Ring Responder
  - Merieke Ri Berit
  - Nettle Sentinel
  - Phyrexian Colossus
  - Phyrexian Ironfoot
  - Slumbering Cerberus

### 50. 11 cards — `~ enters with a +n/+n counter on it`

- **Suggested binder:** _(no registry hit — add a new template)_
- **Canonical example:** Barkhide Troll

  > This creature enters with a +1/+1 counter on it. {1}, Remove a +1/+1 counter from this creature: This creature gains hexproof until end of turn.

- **Example cards (up to 20):**
  - Barkhide Troll
  - District Mascot
  - Dockworker Drone
  - Festercreep
  - Iron Apprentice
  - Monoskelion
  - Selfless Police Captain
  - Servant of the Scale
  - Star Pupil
  - Swarm Shambler
  - Zack Fair

---

Generated by `dotnet run --project Majik.Console -- coverage-gaps`. Clusterer source: `Majik.Core/CardData/Coverage/CoverageGapClusterer.cs`.
