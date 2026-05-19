using Majik.Core.Abilities;

namespace Majik.Core.Effects;

/// <summary>
/// Read-only catalog of built-in effect implementations.
///
/// Registration is engine-internal and seals on first access — once any
/// public lookup runs, the table is frozen for the process lifetime and
/// further registration throws. This eliminates the mutable-global-state
/// hazard the previous shape carried (any caller could mutate the
/// registry mid-game, with no thread-safety guarantee on writes after
/// init).
/// </summary>
public static class EffectLibrary
{
    private static readonly Dictionary<string, IEffect> _effects = new();
    private static readonly Dictionary<string, EffectMetadata> _metadata = new();
    private static bool _sealed;
    private static readonly object _lock = new();

    /// <summary>
    /// Pre-warm the catalog. Idempotent. Calling explicitly is rarely
    /// necessary — any lookup auto-initialises and seals.
    /// </summary>
    public static void Initialize() => EnsureInitialized();

    /// <summary>
    /// Whether the catalog has been sealed (initial registration
    /// complete). Useful for asserting startup ordering in tests.
    /// </summary>
    public static bool IsSealed => Volatile.Read(ref _sealed);

    private static void EnsureInitialized()
    {
        if (Volatile.Read(ref _sealed)) return;

        lock (_lock)
        {
            if (_sealed) return;
            RegisterBuiltInEffects();
            Volatile.Write(ref _sealed, true);
        }
    }

    /// <summary>
    /// Engine-internal registration. Only callable before the catalog
    /// seals; the built-in initializer is the only legitimate caller.
    /// Friend assemblies may register additional effects at startup
    /// before any lookup occurs.
    /// </summary>
    internal static void Register(string effectId, IEffect effect, EffectMetadata metadata)
    {
        if (Volatile.Read(ref _sealed))
            throw new InvalidOperationException(
                $"EffectLibrary is sealed; cannot register '{effectId}'. Register during startup before any lookup.");

        if (string.IsNullOrWhiteSpace(effectId))
            throw new ArgumentException("Effect ID cannot be null or empty", nameof(effectId));

        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(metadata);

        _effects[effectId] = effect;
        _metadata[effectId] = metadata;
    }

    /// <summary>
    /// Get an effect by its ID.
    /// </summary>
    public static IEffect? GetEffect(string effectId)
    {
        EnsureInitialized();
        return _effects.TryGetValue(effectId, out var effect) ? effect : null;
    }

    /// <summary>
    /// Get effect metadata by ID.
    /// </summary>
    public static EffectMetadata? GetMetadata(string effectId)
    {
        EnsureInitialized();
        return _metadata.TryGetValue(effectId, out var metadata) ? metadata : null;
    }

    /// <summary>
    /// Check if an effect is registered.
    /// </summary>
    public static bool IsRegistered(string effectId)
    {
        EnsureInitialized();
        return _effects.ContainsKey(effectId);
    }

    /// <summary>
    /// Get all registered effect IDs.
    /// </summary>
    public static IEnumerable<string> GetAllEffectIds()
    {
        EnsureInitialized();
        return _effects.Keys;
    }

    private static void RegisterBuiltInEffects()
    {
        // Damage effects
        Register("damage_target", 
            new Effect("Deal damage to target", () => { /* Implementation will be parameterized */ }),
            new EffectMetadata("damage_target", "Deal Damage", "Deal X damage to target", EffectType.Damage, 
                new Dictionary<string, string> { { "amount", "int" }, { "target", "string" } }));

        Register("damage_any",
            new Effect("Deal damage to any target", () => { }),
            new EffectMetadata("damage_any", "Deal Damage (Any)", "Deal X damage to any target", EffectType.Damage,
                new Dictionary<string, string> { { "amount", "int" } }));

        // Life effects
        Register("gain_life",
            new Effect("Gain life", () => { }),
            new EffectMetadata("gain_life", "Gain Life", "Gain X life", EffectType.Life,
                new Dictionary<string, string> { { "amount", "int" } }));

        Register("lose_life",
            new Effect("Lose life", () => { }),
            new EffectMetadata("lose_life", "Lose Life", "Lose X life", EffectType.Life,
                new Dictionary<string, string> { { "amount", "int" } }));

        // Draw effects
        Register("draw_cards",
            new Effect("Draw cards", () => { }),
            new EffectMetadata("draw_cards", "Draw Cards", "Draw X cards", EffectType.Draw,
                new Dictionary<string, string> { { "amount", "int" } }));

        // Token effects
        Register("create_token",
            new Effect("Create token", () => { }),
            new EffectMetadata("create_token", "Create Token", "Create a token", EffectType.Token,
                new Dictionary<string, string> { { "token_type", "string" }, { "power", "string?" }, { "toughness", "string?" } }));

        // Counter effects
        Register("put_counter",
            new Effect("Put counter on target", () => { }),
            new EffectMetadata("put_counter", "Put Counter", "Put X +1/+1 counters on target", EffectType.Counter,
                new Dictionary<string, string> { { "amount", "int" }, { "target", "string" } }));

        // Destroy effects
        Register("destroy_target",
            new Effect("Destroy target", () => { }),
            new EffectMetadata("destroy_target", "Destroy Target", "Destroy target permanent", EffectType.Destroy,
                new Dictionary<string, string> { { "target", "string" } }));

        // Exile effects
        Register("exile_target",
            new Effect("Exile target", () => { }),
            new EffectMetadata("exile_target", "Exile Target", "Exile target permanent", EffectType.Exile,
                new Dictionary<string, string> { { "target", "string" } }));

        // Return effects
        Register("return_to_hand",
            new Effect("Return to hand", () => { }),
            new EffectMetadata("return_to_hand", "Return to Hand", "Return target permanent to its owner's hand", EffectType.Return,
                new Dictionary<string, string> { { "target", "string" }, { "zone", "string" } }));

        // Search effects
        Register("search_library",
            new Effect("Search library", () => { }),
            new EffectMetadata("search_library", "Search Library", "Search library for a card", EffectType.Search,
                new Dictionary<string, string> { { "card_type", "string?" }, { "put_zone", "string" } }));

        // Tap/Untap effects
        Register("tap_target",
            new Effect("Tap target", () => { }),
            new EffectMetadata("tap_target", "Tap Target", "Tap target permanent", EffectType.Tap,
                new Dictionary<string, string> { { "target", "string" } }));

        Register("untap_target",
            new Effect("Untap target", () => { }),
            new EffectMetadata("untap_target", "Untap Target", "Untap target permanent", EffectType.Tap,
                new Dictionary<string, string> { { "target", "string" } }));

        // P/T modification effects (for layer system)
        Register("modify_pt",
            new Effect("Modify power/toughness", () => { }),
            new EffectMetadata("modify_pt", "Modify P/T", "Gets +X/+Y", EffectType.ModifyPT,
                new Dictionary<string, string> { { "power_modifier", "int" }, { "toughness_modifier", "int" }, { "target", "string?" } }));

        // Add ability effects (for layer system)
        Register("add_ability",
            new Effect("Add ability", () => { }),
            new EffectMetadata("add_ability", "Add Ability", "Gains [ability]", EffectType.AddAbility,
                new Dictionary<string, string> { { "ability", "string" }, { "target", "string?" } }));
    }
}

/// <summary>
/// Metadata about an effect.
/// </summary>
public class EffectMetadata
{
    public string EffectId { get; }
    public string Name { get; }
    public string Description { get; }
    public EffectType Type { get; }
    public Dictionary<string, string> Parameters { get; }

    public EffectMetadata(string effectId, string name, string description, EffectType type, Dictionary<string, string> parameters)
    {
        EffectId = effectId ?? throw new ArgumentNullException(nameof(effectId));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Type = type;
        Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
    }
}

/// <summary>
/// Type of effect (matches EffectType enum from database).
/// </summary>
public enum EffectType
{
    Damage = 0,
    Life = 1,
    Draw = 2,
    Token = 3,
    Counter = 4,
    Destroy = 5,
    Exile = 6,
    Return = 7,
    Search = 8,
    Tap = 9,
    ModifyPT = 10,
    AddAbility = 11,
    ChangeControl = 12,
    ChangeType = 13,
    ChangeColor = 14,
    Other = 99
}
