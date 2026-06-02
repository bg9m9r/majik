using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Figure of Destiny (Eventide, {R/W}).
/// Creature — Kithkin 1/1.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   "{R/W}: This creature becomes a Kithkin Spirit with base power and
///    toughness 2/2.
///    {R/W}{R/W}{R/W}: If this creature is a Spirit, it becomes a Kithkin
///    Spirit Warrior with base power and toughness 4/4.
///    {R/W}{R/W}{R/W}{R/W}{R/W}{R/W}: If this creature is a Warrior, it
///    becomes a Kithkin Spirit Warrior Avatar with base power and toughness
///    8/8, flying, and first strike."
///
/// A self-pumping "level up by activation" creature. The printed body (1/1
/// Kithkin, mana cost {R/W} — CR 107.4e hybrid pip, parsed by
/// <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/> exactly as Boros
/// Reckoner's {R/W}{R/W}{R/W} cost) is materialised from the embedded JSON
/// definition (<c>figure-of-destiny.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three level-up activated
/// abilities are layered on here — none is expressible in the current JSON
/// <c>AbilityDefinition</c> schema (no "becomes base P/T", no conditional
/// "if this creature is a Spirit/Warrior" gate, no keyword grant), the same
/// posture <see cref="GhituEncampmentFactory"/> takes for its animate ability.
///
/// ## Each level (CR 602 — ordinary activated ability, uses the stack)
/// The three abilities share the manland-style "register continuous effects
/// on resolution" shape, but the effects are PERMANENT (no "until end of
/// turn" — unlike a manland animate), so no <c>ExpiresAtEndOfTurn</c> flag:
///   - <b>{R/W}</b> — becomes a Kithkin Spirit, base P/T 2/2.
///     Layer 4 (<see cref="AddSubtypeEffect"/>) adds <see cref="CardSubtype.Spirit"/>
///     on top of the printed Kithkin (CR 613.1d); Layer 7b
///     (<see cref="BecomesPTEffect"/>) sets base P/T 2/2 (CR 613.7b).
///   - <b>{R/W}{R/W}{R/W}</b> — "If this creature is a Spirit" (CR 603.4-style
///     gate, checked at resolution against the computed subtypes), becomes a
///     Kithkin Spirit Warrior, base P/T 4/4. Adds <see cref="CardSubtype.Warrior"/>
///     + a fresh 4/4 set-base (a later-timestamp Layer 7b set-base overrides
///     the 2/2 — CR 613.7b / dependency rules; same source so timestamp order
///     resolves it).
///   - <b>{R/W}×6</b> — "If this creature is a Warrior", becomes a Kithkin
///     Spirit Warrior Avatar, base P/T 8/8, flying, and first strike. Adds
///     <see cref="CardSubtype.Avatar"/>, an 8/8 set-base, and Flying +
///     First strike as <see cref="KeywordAbility"/> markers (CR 702.9 /
///     702.7) — the same keyword-marker channel Boros Reckoner uses, baked
///     into <see cref="ContinuousEffectsService.Compute"/>'s keyword set.
///
/// ## Conditional gate (CR 608.2c)
/// "If this creature is a Spirit / Warrior" is evaluated at resolution time
/// by reading the source's computed subtypes from the supplied
/// <see cref="ContinuousEffectsService"/>. When no service is wired (shape-
/// only path) the gate cannot be evaluated and the effect is a no-op — the
/// abilities are still attached for inspection. The subtype check is on the
/// effective (post-layer) subtypes so a previously-applied lower level lets
/// the next level through (CR 613 — subtype-adding effects compound).
/// </summary>
[CardName("Figure of Destiny")]
public static class FigureOfDestinyFactory
{
    public const string CardName = "Figure of Destiny";
    public const string Slug = "figure-of-destiny";

    /// <summary>
    /// Construct Figure of Destiny with no <see cref="ContinuousEffectsService"/>
    /// wired. The printed 1/1 Kithkin body (from JSON) + the three level-up
    /// activated abilities are attached so the card surface is complete; the
    /// layer effects are not registered (each ability is a no-op on
    /// resolution). This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Figure of Destiny with an optional continuous-effects service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="effects">Continuous-effects service used to (a) read the
    /// source's current subtypes for the "If this creature is a Spirit /
    /// Warrior" gates and (b) register the Layer-4 subtype + Layer-7b set-base
    /// effects for each resolved level. May be null — the abilities still
    /// resolve but no continuous effects are recorded and the gates short-
    /// circuit to no-op.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature type,
        // Kithkin subtype, {R/W} cost, 1/1). The three level-up abilities are
        // layered on below — none is expressible in the JSON ability schema.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {R/W}: becomes a Kithkin Spirit, base P/T 2/2.  (No gate.)
        // ----------------------------------------------------------------
        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{R/W}") },
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: becomes a Kithkin Spirit with base P/T 2/2 (CR 613.1d / 613.7b)",
                    () => ApplyLevel(
                        effects, card,
                        requiredSubtype: null,
                        addedSubtype: CardSubtype.Spirit,
                        power: 2, toughness: 2,
                        keywords: null)),
            }));

        // ----------------------------------------------------------------
        // {R/W}{R/W}{R/W}: If this creature is a Spirit, becomes a Kithkin
        // Spirit Warrior, base P/T 4/4.
        // ----------------------------------------------------------------
        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{R/W}{R/W}{R/W}") },
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: if a Spirit, becomes a Kithkin Spirit Warrior with base P/T 4/4 (CR 608.2c)",
                    () => ApplyLevel(
                        effects, card,
                        requiredSubtype: CardSubtype.Spirit,
                        addedSubtype: CardSubtype.Warrior,
                        power: 4, toughness: 4,
                        keywords: null)),
            }));

        // ----------------------------------------------------------------
        // {R/W}{R/W}{R/W}{R/W}{R/W}{R/W}: If this creature is a Warrior,
        // becomes a Kithkin Spirit Warrior Avatar, base P/T 8/8, flying, and
        // first strike.
        // ----------------------------------------------------------------
        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{R/W}{R/W}{R/W}{R/W}{R/W}{R/W}"),
            },
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: if a Warrior, becomes a Kithkin Spirit Warrior Avatar with base P/T 8/8, flying, first strike (CR 608.2c / 702.9 / 702.7)",
                    () => ApplyLevel(
                        effects, card,
                        requiredSubtype: CardSubtype.Warrior,
                        addedSubtype: CardSubtype.Avatar,
                        power: 8, toughness: 8,
                        keywords: new[] { "Flying", "First strike" })),
            }));

        return card;
    }

    /// <summary>
    /// Shared level-up resolution. When <paramref name="effects"/> is null the
    /// gate cannot be evaluated and this is a no-op (shape-only path). When
    /// <paramref name="requiredSubtype"/> is non-null the "If this creature is
    /// a &lt;subtype&gt;" gate (CR 608.2c) is checked against the source's
    /// computed subtypes; a miss is a legal no-op. On a pass: registers the
    /// Layer-4 subtype add (CR 613.1d), the Layer-7b set-base P/T (CR 613.7b),
    /// and attaches any granted keywords as <see cref="KeywordAbility"/>
    /// markers (CR 702 — surfaced by Compute's keyword set).
    /// </summary>
    private static void ApplyLevel(
        ContinuousEffectsService? effects,
        Creature card,
        CardSubtype? requiredSubtype,
        CardSubtype addedSubtype,
        int power,
        int toughness,
        string[]? keywords)
    {
        if (effects == null) return; // no service wired — shape-only path

        // CR 608.2c — evaluate "If this creature is a Spirit / Warrior" against
        // the effective (post-layer) subtypes so a prior level lets this one
        // through. A miss is a legal no-op (the ability still resolved).
        if (requiredSubtype.HasValue)
        {
            var current = effects.Compute(card);
            if (!current.Subtypes.Contains(requiredSubtype.Value)) return;
        }

        // Layer 4 — add the new subtype on top of the printed Kithkin and any
        // previously-added subtypes (CR 613.1d — additive).
        effects.Register(new AddSubtypeEffect(card, addedSubtype));

        // Layer 7b — set base P/T. A later-timestamp set-base from the same
        // source overrides an earlier one (CR 613.7b).
        effects.Register(new BecomesPTEffect(card, power, toughness));

        // CR 702 — keyword grants are attached as KeywordAbility markers,
        // the channel ContinuousEffectsService.Compute bakes into the
        // effective keyword set (same as Boros Reckoner's First strike).
        if (keywords != null)
        {
            foreach (var kw in keywords)
            {
                card.AddAbility(new KeywordAbility(kw, source: card, controller: card.Controller));
            }
        }
    }
}
