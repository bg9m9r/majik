using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Desolation Twin (Battle for Zendikar, {10}).
///
/// Creature — Eldrazi 10/10 (colorless). Oracle text (verified against
/// Scryfall):
///   "When you cast this spell, create a 10/10 colorless Eldrazi creature
///    token."
///
/// The card's base shape (name, Creature, Eldrazi subtype, {10}, 10/10) is
/// materialised from the embedded JSON definition (<c>desolation-twin.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed cast trigger is
/// layered on top here — the JSON <c>AbilityDefinition</c> schema doesn't yet
/// express cast triggers, so it lives in the factory (same posture as
/// <see cref="ConduitOfRuinFactory"/> and the other JSON-backed cast-trigger
/// Eldrazi).
///
/// ## Implemented (v1)
///
/// - <b>Cast trigger (CR 603.6e / CR 603.2e)</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> filtered
///   to <c>ReferenceEquals(e.Spell.Card, card)</c> — the canonical
///   self-cast-detection pattern shared with <see cref="ConduitOfRuinFactory"/>
///   / Emrakul, the Aeons Torn. ActiveZones = { <see cref="ZoneType.Stack"/> }
///   so the trigger lands while Desolation Twin is itself the spell on the
///   stack (a "when you cast" trigger fires from the stack, NOT on ETB —
///   CR 603.2e). On resolution the effect mints one 10/10 colorless Eldrazi
///   creature token (CR 111.10) under the caster's control via
///   <see cref="TokenFactory.CreateOnBattlefield"/> with an explicit empty
///   colour set (CR 111.4 — the token is colorless). The token has no
///   subtypes beyond Eldrazi and no abilities — the printed token is a vanilla
///   10/10 colorless Eldrazi (distinct from the Eldrazi Spawn / Scion token
///   primitives, which carry a sac-for-{C} mana ability).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. The cast trigger is attached
///   for shape observability; not registered with any
///   <see cref="TriggerManager"/>, no <see cref="ZoneService"/> wiring. This is
///   the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully wired.
///   The trigger registers with <paramref name="triggers"/>; the token's ETB
///   routes through <paramref name="zones"/> so its own
///   <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (downstream ETB
///   subscribers see the token arrival).
/// </summary>
[CardName("Desolation Twin")]
public static class DesolationTwinFactory
{
    public const string CardName = "Desolation Twin";
    public const string Slug = "desolation-twin";

    /// <summary>The minted token's name, power, and toughness — a 10/10
    /// colorless Eldrazi (CR 111.4).</summary>
    public const string TokenName = "Eldrazi";
    public const int TokenPower = 10;
    public const int TokenToughness = 10;

    /// <summary>
    /// Construct Desolation Twin with no live wiring. The cast trigger is
    /// attached structurally (correct card shape for factory-shape / dispatch
    /// tests) but NOT registered with a <see cref="TriggerManager"/>; the token
    /// mints with a raw zone move (null <see cref="ZoneService"/>). This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>
    /// Construct Desolation Twin with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the minted token's ETB routes through
    /// <see cref="ZoneService.MoveCardTo"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes for any
    /// zone-change subscribers.</param>
    /// <param name="triggers">When supplied, the cast trigger registers with
    /// the bus so the corresponding <see cref="SpellCastEvent"/> lands the
    /// ability on the stack automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi subtype, {10}, 10/10). The JSON carries no abilities — the
        // cast trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.6e / CR 603.2e / CR 111 (Token).
        //   "When you cast this spell, create a 10/10 colorless Eldrazi
        //    creature token."
        // A "when you cast this spell" trigger fires while the spell is on
        // the stack (NOT an ETB trigger — CR 603.2e). Self-cast detection
        // follows Conduit of Ruin's posture: filter SpellCastEvent on
        // ReferenceEquals(e.Spell.Card, card) and capture the actual caster
        // so the token is created under the right controller (a stolen /
        // copied cast mints the token for whoever cast it). ActiveZones =
        // Stack so the trigger is alive while Desolation Twin is itself the
        // cast spell.
        // ----------------------------------------------------------------
        Player? capturedCaster = null;
        var castCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) =>
            {
                if (!ReferenceEquals(e.Spell.Card, card)) return false;
                capturedCaster = e.Spell.Controller;
                return true;
            });

        var castEffect = new Effect(
            $"{CardName}: create a {TokenPower}/{TokenToughness} colorless Eldrazi creature token",
            () =>
            {
                var controller = capturedCaster ?? card.Controller ?? owner;

                // CR 111.4 / CR 111.10 — a vanilla 10/10 colorless Eldrazi
                // creature token (no subtypes beyond Eldrazi, no abilities).
                // Explicit empty colour set stamps the token as colorless
                // (CR 111.4) rather than inferring from a non-existent mana
                // cost.
                var spec = new TokenFactory.TokenSpec(
                    Name: TokenName,
                    Power: TokenPower,
                    Toughness: TokenToughness,
                    Subtypes: new[] { CardSubtype.Eldrazi },
                    Colors: Array.Empty<ManaColor>());

                TokenFactory.CreateOnBattlefield(spec, controller, zones);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
