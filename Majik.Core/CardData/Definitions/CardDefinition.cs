using System.Text.Json.Serialization;
using Majik.Core.Cards.Types;

namespace Majik.Core.CardData.Definitions;

/// <summary>
/// Data-only card description loaded from a JSON file. Replaces hand-coded
/// per-card C# factories for cards whose abilities fit the supported
/// schema. Phase-3 scope (this seam): names, types, P/T, mana abilities.
/// Activated/triggered/static abilities will land in follow-up PRs as
/// the schema grows.
///
/// Authoring rules:
/// - Authoritative — when this file disagrees with C# code, the JSON wins.
///   The C# factory for any migrated card becomes a thin wrapper that
///   loads its JSON and calls <see cref="CardDefinitionFactory"/>.
/// - Stable identifiers — string enums (CardType, CardSubtype) are
///   serialized by name to keep diffs reviewable.
/// - JSON-serializable only — no closures, no references to engine
///   services. The factory binds those at construction time.
/// </summary>
public sealed class CardDefinition
{
    /// <summary>Canonical card name (matches the Scryfall <c>name</c>
    /// field). Used as the cache + lookup key.</summary>
    public string Name { get; set; } = "";

    /// <summary>Card types, e.g. <c>["Land"]</c>, <c>["Artifact", "Creature"]</c>.
    /// Must contain at least one entry.</summary>
    public List<string> Types { get; set; } = new();

    /// <summary>Optional supertype list, e.g. <c>["Basic"]</c>, <c>["Legendary"]</c>.
    /// </summary>
    public List<string> Supertypes { get; set; } = new();

    /// <summary>Optional subtype list, e.g. <c>["Construct"]</c>,
    /// <c>["Phyrexian", "Human", "Cleric"]</c>.</summary>
    public List<string> Subtypes { get; set; } = new();

    /// <summary>Bracketed mana cost like <c>"{1}{G}"</c> or unbracketed
    /// <c>"1G"</c>. Empty string for cards with no cost (lands).</summary>
    public string ManaCost { get; set; } = "";

    /// <summary>Creature power. Null for non-creatures.</summary>
    public int? Power { get; set; }

    /// <summary>Creature toughness. Null for non-creatures.</summary>
    public int? Toughness { get; set; }

    /// <summary>Starting loyalty. Null for non-planeswalkers.</summary>
    public int? Loyalty { get; set; }

    /// <summary>
    /// CR 202.2c — printed color indicator on this card (the round dot to
    /// the left of the type line). Single-letter Scryfall codes (W/U/B/R/G).
    /// Optional; null/empty means "no color indicator printed — color is
    /// derived from the mana cost alone". Set to <c>["G"]</c> for cards
    /// like Dryad Arbor (no mana cost, green color indicator) so the
    /// runtime <see cref="Majik.Core.Cards.CardColors.GetColors"/> reports
    /// them as green and color-matters tutors (Green Sun's Zenith,
    /// Summoner's Pact, Chord of Calling) find them.
    /// </summary>
    public List<string> Colors { get; set; } = new();

    /// <summary>Ability list. Each entry is a discriminated union via
    /// the <c>kind</c> JSON property.</summary>
    public List<AbilityDefinition> Abilities { get; set; } = new();

    /// <summary>
    /// Deserialize this JSON model into the canonical fluent
    /// <see cref="CardDef"/> (PLAN 03 S2). The JSON
    /// <see cref="CardDefinition"/> schema is a serialization of the same
    /// declarative shape the DSL emits; this method is the bridge. Types,
    /// P/T, loyalty, supertypes/subtypes, colour indicator and the ability
    /// union (mana / activated / triggered) all map onto the
    /// <see cref="CardDef"/> via <see cref="CardDefBuilder"/>. The runtime
    /// materializer (<see cref="CardDefRuntime.Build"/>) then turns the
    /// <see cref="CardDef"/> into a live <see cref="Majik.Core.Cards.ICard"/>
    /// — the single interpreter for both declarative systems.
    /// </summary>
    public CardDef ToCardDef()
    {
        if (Types.Count == 0)
            throw new ArgumentException($"Card '{Name}' has no types.", nameof(Types));

        var supertypes = Supertypes.Select(ParseSupertype).ToArray();
        var subtypes = Subtypes.Select(ParseSubtype).ToArray();
        var primary = ParseType(Types[0]);

        // Pick the matching builder entry point + thread the required stat.
        // ManaCost passed verbatim — JSON authors decide bracketing.
        var builder = primary switch
        {
            CardType.Land => CardDef.Land(Name),
            CardType.Creature => CardDef.Creature(
                Name, ManaCost,
                Power ?? throw MissingStat(Name, "power"),
                Toughness ?? throw MissingStat(Name, "toughness")),
            CardType.Artifact => CardDef.Artifact(Name, ManaCost),
            CardType.Enchantment => CardDef.Enchantment(Name, ManaCost),
            CardType.Instant => CardDef.Instant(Name, ManaCost),
            CardType.Sorcery => CardDef.Sorcery(Name, ManaCost),
            CardType.Planeswalker => CardDef.Planeswalker(
                Name, ManaCost,
                Loyalty ?? throw MissingStat(Name, "loyalty")),
            _ => throw new NotSupportedException(
                $"Card '{Name}' primary type '{Types[0]}' is not supported by CardDefinition.ToCardDef()."),
        };

        // Supertypes / subtypes (the CardDef.* entry points only set the
        // primary type; everything else stacks via the builder).
        foreach (var s in supertypes) builder.WithSupertype(s);
        foreach (var s in subtypes) builder.WithSubtype(s);

        // Additional types (Artifact Creature, …) — skip the primary at [0].
        for (var i = 1; i < Types.Count; i++)
        {
            builder.WithType(ParseType(Types[i]));
        }

        // CR 202.2c — colour indicator (Dryad Arbor).
        foreach (var letter in Colors)
        {
            builder.WithColorIndicator(letter);
        }

        // Ability union → canonical CardDefAbility entries. The mappers
        // (ToCardDefAbility) close over the JSON-effect/cost/trigger builders
        // in CardDefRuntime so the runtime card is byte-identical to the
        // legacy direct-build path.
        foreach (var ability in Abilities)
        {
            builder.WithAbility(ability.ToCardDefAbility());
        }

        return builder.Build();
    }

    private static CardType ParseType(string raw) =>
        Enum.TryParse<CardType>(raw, ignoreCase: true, out var t)
            ? t
            : throw new ArgumentException($"Unknown card type '{raw}'.", nameof(raw));

    private static CardSupertype ParseSupertype(string raw) =>
        Enum.TryParse<CardSupertype>(raw, ignoreCase: true, out var s)
            ? s
            : throw new ArgumentException($"Unknown card supertype '{raw}'.", nameof(raw));

    private static CardSubtype ParseSubtype(string raw) =>
        Enum.TryParse<CardSubtype>(raw, ignoreCase: true, out var s)
            ? s
            : throw new ArgumentException($"Unknown card subtype '{raw}'.", nameof(raw));

    private static ArgumentException MissingStat(string cardName, string stat) =>
        new($"Card '{cardName}' is missing required '{stat}'.");
}

/// <summary>
/// Discriminated union for ability shapes. Polymorphism is wired with
/// <see cref="JsonPolymorphicAttribute"/> so deserializers pick the
/// right concrete type from the <c>kind</c> property.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ManaAbilityDefinition), "mana")]
[JsonDerivedType(typeof(ActivatedAbilityDefinition), "activated")]
[JsonDerivedType(typeof(TriggeredAbilityDefinition), "triggered")]
public abstract class AbilityDefinition
{
    /// <summary>
    /// Map this JSON ability onto the canonical <see cref="CardDefAbility"/>
    /// representation carried by <see cref="CardDef.Abilities"/> (PLAN 03 S2).
    /// The returned ability defers its cost / effect / trigger construction to
    /// the <see cref="CardDefRuntime"/> JSON-ability builders (which route
    /// through the shared <c>Costs.*</c> / <c>Fx.*</c> / <c>Triggers.*</c>
    /// vocabulary), so the live ability is byte-identical to the legacy
    /// direct build.
    /// </summary>
    public abstract CardDefAbility ToCardDefAbility();
}

/// <summary>
/// Tap-to-produce-mana ability. <see cref="Produces"/> is a Scryfall
/// single-letter color code or short cost — the same string accepted by
/// <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> (e.g. "G", "B",
/// "WU" for either-or hybrid handled elsewhere).
///
/// CR 605.1 — mana abilities don't use the stack. The runtime factory
/// builds a <see cref="Majik.Core.Abilities.ManaAbility"/> with the
/// parsed cost; no priority round is incurred when activated.
///
/// <see cref="Cost"/> is an OPTIONAL additional mana cost paid alongside
/// the implicit {T} when the ability is activated — the mana-rock
/// "signet" shape <c>{1}, {T}: Add {U}{R}</c>. When present it is parsed
/// with <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> and threaded
/// through the additional-cost overload of
/// <see cref="Majik.Core.Abilities.ManaAbility"/> — the same path the
/// filter-land cycle uses (deduct the extra mana from the pool, gate
/// activation on affordability + untapped state). Null / empty means the
/// vanilla "{T}: Add &lt;Produces&gt;" shape with no extra cost. CR 605.1 —
/// the extra mana cost is part of activation, the ability still does not
/// use the stack.
/// </summary>
public sealed class ManaAbilityDefinition : AbilityDefinition
{
    public string Produces { get; set; } = "";

    /// <summary>Optional additional mana cost paid alongside {T}
    /// (e.g. "1" for the signet shape). Null/empty = no extra cost.</summary>
    public string? Cost { get; set; }

    /// <inheritdoc />
    public override CardDefAbility ToCardDefAbility() =>
        new CardDefManaAbility((card, controller) =>
            CardDefRuntime.BuildJsonManaAbility(this, card, controller));
}

/// <summary>
/// Non-mana activated ability — pay all <see cref="Costs"/>, then
/// resolve every entry in <see cref="Effects"/> in order. Uses the
/// stack (mana abilities do not — CR 605.1). Cost / effect variants
/// live under <see cref="CostDefinition"/> + <see cref="EffectDefinition"/>.
///
/// <see cref="SorcerySpeed"/>: CR 117.1a / 307.5 — "Activate only as a
/// sorcery" rider. Threaded onto the runtime
/// <see cref="Majik.Core.Abilities.ActivatedAbility.IsSorcerySpeed"/> by
/// <see cref="CardDefRuntime"/> (via <see cref="CardDefActivatedAbility"/>);
/// <c>ActionValidator</c> rejects activations outside the controller's main
/// phase / empty-stack window.
/// </summary>
public sealed class ActivatedAbilityDefinition : AbilityDefinition
{
    public List<CostDefinition> Costs { get; set; } = new();
    public List<EffectDefinition> Effects { get; set; } = new();
    public bool SorcerySpeed { get; set; } = false;

    /// <inheritdoc />
    public override CardDefAbility ToCardDefAbility()
    {
        var costBuilders = Costs.Select(c => c.ToCost()).ToArray();
        // PLAN 01 (Slice F) — pair each effect's resolve-builder with the
        // TargetRequest it targets through (null for untargeted effects).
        var effectSpecs = Effects
            .Select(e => new CardDefEffectSpec(e.ToTargetRequest(), e.ToResolveEffect()))
            .ToArray();
        return new CardDefActivatedAbility(costBuilders, effectSpecs, SorcerySpeed);
    }
}

/// <summary>
/// Triggered ability — <see cref="Trigger"/> picks the condition;
/// <see cref="Effects"/> resolve in order when it fires. Uses the stack
/// (intervening-if + delayed triggers handled by the engine elsewhere).
/// </summary>
public sealed class TriggeredAbilityDefinition : AbilityDefinition
{
    public TriggerDefinition Trigger { get; set; } = new EnterBattlefieldSelfTriggerDef();
    public List<EffectDefinition> Effects { get; set; } = new();

    /// <inheritdoc />
    public override CardDefAbility ToCardDefAbility()
    {
        var triggerBuilder = Trigger.ToTrigger();
        var effectSpecs = Effects
            .Select(e => new CardDefEffectSpec(e.ToTargetRequest(), e.ToResolveEffect()))
            .ToArray();
        return new CardDefTriggeredAbility(triggerBuilder, effectSpecs);
    }
}
