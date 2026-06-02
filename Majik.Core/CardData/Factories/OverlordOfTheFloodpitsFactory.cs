using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Overlord of the Floodpits (Duskmourn: House of
/// Horror, {3}{U}{U}). Enchantment Creature — Avatar Horror 5/3. Oracle
/// text (verified against Scryfall):
///   "Impending 4—{1}{U}{U} (If you cast this spell for its impending cost,
///    it enters with four time counters and isn't a creature until the last
///    is removed. At the beginning of your end step, remove a time counter
///    from it.)
///    Flying
///    Whenever this permanent enters or attacks, draw two cards, then
///    discard a card."
///
/// The card's base shape (name, Enchantment + Creature types, Avatar +
/// Horror subtypes, {3}{U}{U}, 5/3) is materialised from the embedded JSON
/// definition (<c>overlord-of-the-floodpits.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The three printed behaviours
/// (the Impending marker keyword, the Flying marker keyword, and the
/// enters-or-attacks trigger) are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers or the
/// draw-then-discard "loot" effect, so they live in the factory (same
/// posture as <see cref="OverlordOfTheBalemurkFactory"/> and the other
/// JSON-backed cards whose behaviour outgrows the schema).
///
/// ## Implemented (v1)
/// - <b>Flying (CR 702.9)</b> — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>
///   and the block-legality checks surface the evasion (same marker idiom as
///   <see cref="StormscaleScionFactory"/>).
/// - <b>Enters-or-attacks trigger (CR 603.1 ETB + CR 508.1f attack)</b>:
///   two <see cref="TriggeredAbility"/> instances sharing one effect body —
///   one gated on <see cref="Triggers.OnEnterBattlefieldSelf"/>, one on
///   <see cref="Triggers.OnAttackSelf"/> (same dual-trigger shape as
///   <see cref="OverlordOfTheBalemurkFactory"/>'s "enters or attacks"). On
///   resolution: draw two cards (CR 120), then discard a card (CR 701.16),
///   via <see cref="Fx.DrawCards"/> + <see cref="Fx.Discard"/>. Discard is
///   the engine's deterministic first-card-in-hand pick (agent-driven
///   which-card-to-discard choice is the same v1 gap as Faithless Looting).
///
/// ## Impending — modelled as a marker keyword (deferred mechanic)
/// "Impending 4—{1}{U}{U}" is an alternative-cost keyword (Duskmourn). The
/// engine does not yet have a first-class Impending alt-cost / "isn't a
/// creature until the last time counter is removed" path. Following the
/// established marker-keyword precedent (Impending on
/// <see cref="OverlordOfTheBalemurkFactory"/>, Delve, Suspend), Impending is
/// wired as a <see cref="KeywordAbility"/> marker with <c>Arg = 4</c> so
/// introspection (UI, bots, the alt-cost probe stream) can see the keyword +
/// counter count on the card. The full Impending mechanic — casting for the
/// impending cost with four Time counters (CR 122.1), the Layer-4 "isn't a
/// creature" type-strip while counters remain (CR 613), and the end-step
/// "remove a time counter" delayed trigger — is deferred. The card's printed
/// gameplay payload (Flying + the enters-or-attacks loot trigger) is fully
/// implemented; only the alternate way to pay for it is the deferred part.
/// When cast for its normal {3}{U}{U} cost the card behaves completely.
/// </summary>
[CardName("Overlord of the Floodpits")]
public static class OverlordOfTheFloodpitsFactory
{
    public const string CardName = "Overlord of the Floodpits";
    public const string Slug = "overlord-of-the-floodpits";

    /// <summary>Impending counter count — "Impending 4".</summary>
    public const int ImpendingCount = 4;

    /// <summary>Cards drawn by the enters-or-attacks trigger.</summary>
    public const int DrawCount = 2;

    /// <summary>Cards discarded by the enters-or-attacks trigger.</summary>
    public const int DiscardCount = 1;

    /// <summary>
    /// Construct Overlord of the Floodpits with no live TriggerManager
    /// wiring. Flying + the two enters-or-attacks triggers + the Impending
    /// marker are attached for shape inspection. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Overlord of the Floodpits with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the ETB + attack triggers are
    /// registered so the matching events land their abilities on the stack
    /// automatically.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Avatar + Horror subtypes, {3}{U}{U}, 5/3). The
        // JSON carries no abilities — the Impending + Flying markers and the
        // enters-or-attacks trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Impending 4 — marker keyword (mechanic deferred; see class
        // remarks). Arg carries the printed counter count.
        card.AddAbility(new KeywordAbility("Impending", card, owner, arg: ImpendingCount));

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities
        // surfaces evasion / block-legality.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Shared effect body: "draw two cards, then discard a card."
        // (CR 120 draw + CR 701.16 discard.)
        // ----------------------------------------------------------------
        IEffect BuildTriggerEffect(string label) =>
            new Effect(label, _ =>
            {
                Fx.DrawCards(owner, DrawCount);
                Fx.Discard(owner, DiscardCount);
                return default;
            });

        // ETB trigger — CR 603.1.
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { BuildTriggerEffect($"{CardName}: enters — draw 2, discard 1") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Attack trigger — CR 508.1f.
        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new[] { BuildTriggerEffect($"{CardName}: attacks — draw 2, discard 1") },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }
}
