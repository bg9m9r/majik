using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Players;

namespace Majik.Core.CardData.Parsing;

/// <summary>
/// Registry of Magic: The Gathering keywords and their ability implementations.
/// Maps keyword names to their corresponding abilities and layer information.
/// </summary>
public static class KeywordRegistry
{
    private static readonly Dictionary<string, KeywordInfo> _keywords = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized = false;
    private static readonly object _lock = new();

    /// <summary>
    /// Initialize the keyword registry with built-in keywords.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        lock (_lock)
        {
            if (_initialized)
                return;

            RegisterBuiltInKeywords();
            _initialized = true;
        }
    }

    /// <summary>
    /// Register a keyword.
    /// </summary>
    public static void Register(string keyword, KeywordInfo info)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            throw new ArgumentException("Keyword cannot be null or empty", nameof(keyword));
        
        if (info == null)
            throw new ArgumentNullException(nameof(info));

        _keywords[keyword] = info;
    }

    /// <summary>
    /// Get keyword information.
    /// </summary>
    public static KeywordInfo? GetKeywordInfo(string keyword)
    {
        Initialize();
        return _keywords.TryGetValue(keyword, out var info) ? info : null;
    }

    /// <summary>
    /// Check if a keyword is registered.
    /// </summary>
    public static bool IsRegistered(string keyword)
    {
        Initialize();
        return _keywords.ContainsKey(keyword);
    }

    /// <summary>
    /// Get all registered keywords.
    /// </summary>
    public static IEnumerable<string> GetAllKeywords()
    {
        Initialize();
        return _keywords.Keys;
    }

    private static void RegisterBuiltInKeywords()
    {
        // Static Abilities (Layer 6 - Ability Adding)
        
        // Evasion abilities
        Register("flying", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can't be blocked except by creatures with flying and/or reach.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Flying",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Flying is handled by combat system */ }) as object));

        Register("reach", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can block creatures with flying.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Reach",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Reach is handled by combat system */ }) as object));

        Register("shadow", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can block or be blocked by only creatures with shadow.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Shadow",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Shadow is handled by combat system */ }) as object));

        // Combat abilities
        Register("trample", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can deal excess combat damage to the player or planeswalker it's attacking.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Trample",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Trample is handled by combat system */ }) as object));

        Register("first strike", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature deals combat damage before creatures without first strike.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "First Strike",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* First strike is handled by combat system */ }) as object));

        Register("double strike", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature deals both first-strike and regular combat damage.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Double Strike",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Double strike is handled by combat system */ }) as object));

        Register("deathtouch", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "Any amount of damage this deals to a creature is enough to destroy it.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Deathtouch",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Deathtouch is handled by damage system */ }) as object));

        Register("lifelink", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "Damage dealt by this creature also causes you to gain that much life.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Lifelink",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Lifelink is handled by damage system */ }) as object));

        Register("vigilance", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "Attacking doesn't cause this creature to tap.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Vigilance",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Vigilance is handled by combat system */ }) as object));

        Register("haste", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can attack and {T} as soon as it comes under your control.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Haste",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Haste removes summoning sickness */ }) as object));

        Register("flash", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "You may cast this spell any time you could cast an instant.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Flash",
                isActiveCheck: () => true, // Flash applies to spells
                applyEffect: () => { /* Flash is handled by spell casting rules */ }) as object));

        // Protection abilities
        Register("hexproof", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This permanent can't be the target of spells or abilities your opponents control.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Hexproof",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Hexproof is handled by targeting system */ }) as object));

        Register("shroud", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This permanent can't be the target of spells or abilities.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Shroud",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Shroud is handled by targeting system */ }) as object));

        // Other static abilities
        Register("indestructible", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "Effects that say 'destroy' don't destroy this permanent. A creature with indestructible can't be destroyed by damage.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Indestructible",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Indestructible is handled by destruction system */ }) as object));

        // Triggered Abilities
        
        Register("prowess", new KeywordInfo(
            KeywordType.Triggered,
            layer: null,
            description: "Whenever you cast a noncreature spell, this creature gets +1/+1 until end of turn.",
            createAbility: (source, controller) =>
            {
                if (source is not Cards.Creature creature) return null;
                return new Abilities.TriggeredAbility(
                    creature, controller,
                    Abilities.Triggers.OnNonCreatureSpellCastByController(controller),
                    effects: new Abilities.IEffect[]
                    {
                        new Abilities.Effect("prowess +1/+1 EOT", () =>
                        {
                            creature.ActiveEffects?.Register(
                                new Effects.ProwessPumpEffect(creature));
                        }),
                    });
            }));

        Register("landfall", new KeywordInfo(
            KeywordType.Triggered,
            layer: null,
            description: "Whenever a land enters the battlefield under your control...",
            createAbility: (source, controller) => null)); // Placeholder

        // Activated Abilities
        
        Register("cycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Draw a card.",
            createAbility: (source, controller) => null)); // Placeholder - cycling has variable costs

        // Additional Static Abilities
        
        Register("defender", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can't attack.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Defender",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Defender is handled by combat system */ }) as object));

        Register("menace", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can't be blocked except by two or more creatures.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Menace",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Menace is handled by combat system */ }) as object));

        Register("skulk", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can't be blocked by creatures with greater power.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Skulk",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Skulk is handled by combat system */ }) as object));

        Register("fear", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can't be blocked except by artifact creatures and/or black creatures.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Fear",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Fear is handled by combat system */ }) as object));

        Register("intimidate", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can't be blocked except by artifact creatures and/or creatures that share a color with it.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Intimidate",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Intimidate is handled by combat system */ }) as object));

        Register("horsemanship", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature can't be blocked except by creatures with horsemanship.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Horsemanship",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Horsemanship is handled by combat system */ }) as object));

        // Landwalk abilities (static, layer 6)
        Register("islandwalk", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature is unblockable as long as defending player controls an Island.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Islandwalk",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Islandwalk is handled by combat system */ }) as object));

        Register("swampwalk", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature is unblockable as long as defending player controls a Swamp.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Swampwalk",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Swampwalk is handled by combat system */ }) as object));

        Register("forestwalk", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature is unblockable as long as defending player controls a Forest.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Forestwalk",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Forestwalk is handled by combat system */ }) as object));

        Register("mountainwalk", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature is unblockable as long as defending player controls a Mountain.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Mountainwalk",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Mountainwalk is handled by combat system */ }) as object));

        Register("plainswalk", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature is unblockable as long as defending player controls a Plains.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Plainswalk",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Plainswalk is handled by combat system */ }) as object));

        Register("landwalk", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature is unblockable as long as defending player controls a land of the chosen type.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Landwalk",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Landwalk is handled by combat system */ }) as object));

        Register("desertwalk", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature is unblockable as long as defending player controls a Desert.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Desertwalk",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Desertwalk is handled by combat system */ }) as object));

        // Additional Triggered Abilities
        
        Register("exalted", new KeywordInfo(
            KeywordType.Triggered,
            layer: null,
            description: "Whenever a creature you control attacks alone, that creature gets +1/+1 until end of turn.",
            createAbility: (source, controller) => null)); // Placeholder - needs triggered ability implementation

        Register("enrage", new KeywordInfo(
            KeywordType.Triggered,
            layer: null,
            description: "Whenever this creature is dealt damage, [effect].",
            createAbility: (source, controller) => null)); // Placeholder

        // Additional Activated Abilities
        
        Register("equip", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}: Attach this Equipment to target creature you control. Activate only as a sorcery.",
            createAbility: (source, controller) => null)); // Placeholder - equip has variable costs

        // Cycling variants
        Register("basic landcycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a basic land card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("forestcycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a Forest card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("islandcycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for an Island card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("mountaincycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a Mountain card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("plainscycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a Plains card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("swampcycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a Swamp card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("landcycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a land card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("typecycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a [type] card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("slivercycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a Sliver card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        Register("wizardcycling", new KeywordInfo(
            KeywordType.Activated,
            layer: null,
            description: "{Cost}, Discard this card: Search your library for a Wizard card, reveal it, put it into your hand, then shuffle.",
            createAbility: (source, controller) => null));

        // Additional common keywords from the database
        
        Register("infect", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature deals damage to creatures in the form of -1/-1 counters and to players in the form of poison counters.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Infect",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Infect is handled by damage system */ }) as object));

        Register("wither", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature deals damage to creatures in the form of -1/-1 counters.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Wither",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Wither is handled by damage system */ }) as object));

        Register("toxic", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "This creature deals damage to players in the form of poison counters.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Toxic",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Toxic is handled by damage system */ }) as object));

        Register("decayed", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "A creature with decayed can't block. When it attacks, sacrifice it at end of combat.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Decayed",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Decayed is handled by combat system */ }) as object));

        Register("ward", new KeywordInfo(
            KeywordType.Triggered,
            layer: null,
            description: "Whenever this permanent becomes the target of a spell or ability an opponent controls, counter it unless that player pays {cost}.",
            createAbility: (source, controller) => null)); // Placeholder - ward has variable costs

        Register("protection", new KeywordInfo(
            KeywordType.Static,
            layer: 6,
            description: "Protection from [quality] means this permanent can't be targeted, dealt damage, enchanted, equipped, fortified, or blocked by anything with that quality.",
            createAbility: (source, controller) => new StaticAbility(
                source,
                controller,
                "Protection",
                isActiveCheck: () => source is Cards.Permanent p && p.Zone == Zones.ZoneType.Battlefield,
                applyEffect: () => { /* Protection is handled by targeting/damage system */ }) as object));

        // Note: Many keywords have variable parameters (e.g., "ward {2}", "protection from X", "hexproof from X")
        // These are handled by KeywordParser.ParseKeyword() which extracts the base keyword and parameters
        // Many entries in the CSV are card names or custom abilities, not official keywords
        // KeywordParser.IsLikelyRealKeyword() helps filter out card names
    }
}

/// <summary>
/// Information about a keyword.
/// </summary>
public class KeywordInfo
{
    public KeywordType Type { get; }
    public int? Layer { get; }  // For static abilities
    public string Description { get; }
    public Func<object, Player, object?> CreateAbility { get; }  // Returns IStaticAbility, IActivatedAbility, etc.

    public KeywordInfo(
        KeywordType type,
        int? layer,
        string description,
        Func<object, Player, object?> createAbility)
    {
        Type = type;
        Layer = layer;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        CreateAbility = createAbility ?? throw new ArgumentNullException(nameof(createAbility));
    }
}

/// <summary>
/// Type of keyword ability.
/// </summary>
public enum KeywordType
{
    Static = 0,
    Triggered = 1,
    Activated = 2,
    Replacement = 3
}
