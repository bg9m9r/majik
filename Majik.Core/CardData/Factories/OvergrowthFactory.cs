using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Overgrowth (Battlebond / Secret Lair, {2}{G}).
///
/// Enchantment — Aura. Oracle text:
///   "Enchant land
///    Whenever enchanted land is tapped for mana, its controller adds an
///    additional {G}{G}."
///
/// ## Implementation
///
/// Card identity (Enchantment — Aura, {2}{G}, green color indicator) is
/// loaded from <c>Majik.Core/CardData/Cards/overgrowth.json</c> through
/// <see cref="CardDefinitionFactory"/>, matching the JSON-driven Aura cards
/// (Utopia Sprawl et al.).
///
/// The mana clause is a triggered <b>mana</b> ability (CR 605.1b — it
/// triggers on mana being produced and itself produces mana). It is modelled
/// as a <see cref="TriggeredAbility"/> subscribing to
/// <see cref="ManaAbilityActivatedEvent"/> (published by
/// <see cref="Majik.Core.Services.ManaAbilityActivator"/> after the
/// activator's pool is topped up — the same surface Utopia Sprawl consumes).
/// The condition matches when the tapped source is exactly the enchanted land
/// (this Aura's <see cref="Permanent.AttachedTo"/> slot). The effect adds
/// {G}{G} to the enchanted land's controller's pool via
/// <see cref="Player.AddManaToPool"/> — CR 106.6 / 605.1b: the controller of
/// the permanent (the player who tapped it) receives the bonus mana, which
/// can differ from the Aura's controller after a control-change ("its
/// controller").
///
/// Unlike Utopia Sprawl there is no "choose a color" clause and no Forest
/// restriction: Overgrowth enchants any land and always adds a fixed {G}{G}.
///
/// ## Enchant land
///
/// <see cref="BuildSpellDefinition"/> derives the cast-time target predicate
/// from the printed "Enchant land" clause via
/// <see cref="AuraSpellDefinitionBuilder.ForAuraFromOracle"/>
/// (<see cref="AuraEnchantClauseParser"/> recognises the bare "land" noun —
/// CR 702.5b / 303.4c). On resolution the Aura attaches to the chosen land
/// (CR 303.4f), same flow as <see cref="UtopiaSprawlFactory"/>.
/// </summary>
[CardName("Overgrowth")]
public static class OvergrowthFactory
{
    public const string CardName = "Overgrowth";
    public const string Cost = "{2}{G}";

    /// <summary>Printed oracle text — kept for documentation parity.</summary>
    public const string OracleText =
        "Enchant land\n" +
        "Whenever enchanted land is tapped for mana, its controller adds " +
        "an additional {G}{G}.";

    /// <summary>Fixed bonus mana — Overgrowth always adds {G}{G}.</summary>
    private static readonly ManaCost BonusMana = ManaCost.Parse("GG");

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("overgrowth");

    /// <summary>
    /// Construct Overgrowth with correct card identity only (no live
    /// mana-trigger wiring). Suitable for factory-shape / dispatcher tests.
    /// </summary>
    public static Enchantment Create(Player owner) =>
        (Enchantment)CardDefinitionFactory.Build(Definition, owner);

    /// <summary>
    /// Construct a fully-wired Overgrowth. The triggered mana ability is
    /// attached to the card's <see cref="Card.Abilities"/> collection; when
    /// <paramref name="triggers"/> is supplied it is also registered with the
    /// <see cref="TriggerManager"/> so it surfaces as pending end-to-end.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Optional live trigger manager for end-to-end
    /// firing.</param>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = Create(owner);

        // "Whenever enchanted land is tapped for mana, its controller adds an
        // additional {G}{G}." (CR 605.1b / 603.2.) Closure-captured payload:
        // the player who tapped the enchanted land (CR 603.7c — bound at
        // trigger time).
        Player? pendingController = null;

        var condition = new EventTriggerCondition<ManaAbilityActivatedEvent>((e, _) =>
        {
            // The bonus only fires for THIS Aura's enchanted land. AttachedTo
            // is the enchanted permanent (null until the Aura is attached).
            var enchanted = card.AttachedTo;
            if (enchanted is null) return false;
            if (!ReferenceEquals(e.Source, enchanted)) return false;
            pendingController = e.Player;
            return true;
        });

        var addManaEffect = new Effect(
            "Overgrowth — add {G}{G} to the controller of the enchanted land",
            () =>
            {
                var controller = pendingController;
                pendingController = null;
                controller?.AddManaToPool(BonusMana);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { addManaEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// Build the cast-time <see cref="SpellDefinition"/> for Overgrowth —
    /// "Enchant land" → single Land target via the printed oracle clause. The
    /// Aura attaches to the chosen land on resolution (CR 303.4f), so when
    /// <see cref="Majik.Core.Services.StackResolver"/> moves the Aura to the
    /// battlefield the trigger's <see cref="Permanent.AttachedTo"/> gate is
    /// already populated.
    /// </summary>
    /// <param name="aura">The Overgrowth permanent being cast.</param>
    /// <param name="battlefield">Current battlefield permanents — the
    /// candidate pool is filtered to Lands.</param>
    public static SpellDefinition BuildSpellDefinition(
        Enchantment aura,
        IEnumerable<Permanent> battlefield)
    {
        ArgumentNullException.ThrowIfNull(aura);
        ArgumentNullException.ThrowIfNull(battlefield);

        // CR 702.5b / 303.4c — "Enchant land" restricts the legal target to a
        // Land. AuraEnchantClauseParser recognises the bare "land" noun.
        return AuraSpellDefinitionBuilder.ForAuraFromOracle(
            aura,
            OracleText,
            battlefield,
            intent: BotIntent.None);
    }
}
