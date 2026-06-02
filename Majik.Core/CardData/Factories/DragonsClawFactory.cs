using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dragon's Claw (8th Edition, {2}).
///
/// Artifact. Oracle text (verified against Scryfall):
///   "Whenever a player casts a red spell, you may gain 1 life."
///
/// Dragon's Claw is the artifact member of the Claw cycle (Dragon's /
/// Demon's / Angel's / Kraken's / Wurm's Claw). It carries the EXACT same
/// red-spell-cast lifegain clause as <see cref="KorFirewalkerFactory"/>
/// (Worldwake), so the trigger is mirrored from that already-shipped
/// factory; Dragon's Claw drops Kor Firewalker's protection-from-red and
/// its creature body — it is a plain colourless artifact.
///
/// The base shape (name, Artifact, {2}, colourless) is materialised from
/// the embedded JSON definition (<c>dragons-claw.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same JSON-backed posture as
/// the mana-rock artifacts — Star Compass, Fellwar Stone, etc.). The
/// red-spell-cast trigger is layered on here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express the SpellCastEvent /
/// colour-predicate trigger shape.
///
/// ## Implemented (v1)
///
/// - Colourless <see cref="Artifact"/> at {2}, owner / controller wired.
/// - <b>Red-spell-cast lifegain trigger (CR 603.1)</b> over
///   <see cref="SpellCastEvent"/>:
///     * Fires for ANY player's spell (controller's own included —
///       oracle "a player" is unrestricted, same posture as
///       <see cref="KorFirewalkerFactory"/>).
///     * Gated on the cast spell being red: the predicate reads the
///       spell's colours via <see cref="CardColors.GetColors"/> (CR 105.2
///       — a card is each colour of its mana-cost pips / color
///       indicator) and fires iff <see cref="ManaColor.Red"/> is present.
///       A multicolour spell with a red pip (e.g. {R}{W}) is a red spell.
///     * Resolution gains Dragon's Claw's controller 1 life via
///       <see cref="Fx.GainLife"/> (CR 119.3 — life gain routes through
///       <see cref="Player.GainLife"/> so LifeGainedThisTurn / Ajani
///       Pridemate-style observers see the gain).
///
/// ## "you may" — optional clause (v1 simplification)
///
/// The oracle reads "you <b>may</b> gain 1 life". Gaining life is purely
/// beneficial with no downside, and the engine's
/// <see cref="TriggeredAbility"/> ctor has no per-trigger "may decline"
/// agent prompt surface (the same modal-yes/no choice that other "may"
/// triggers defer). v1 always takes the gain. A rational agent never
/// declines a free life point, so this is behaviour-equivalent for every
/// game state; the only observable difference would be a contrived
/// interaction with "if you gained life this turn" / "whenever you gain
/// life" payoffs the controller wished to suppress, which v1 does not
/// model. Identical posture to <see cref="KorFirewalkerFactory"/>; wiring
/// the optional prompt is deferred to the shared may-trigger choice
/// infrastructure.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only; the trigger is
///   attached for structural observability but not registered with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / shape tests.
///   This is the overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired; the
///   trigger is registered so any <see cref="SpellCastEvent"/> for a red
///   spell automatically queues the 1-life gain.
/// </summary>
[CardName("Dragon's Claw")]
public static class DragonsClawFactory
{
    public const string CardName = "Dragon's Claw";
    public const string Slug = "dragons-claw";
    public const int LifeGainAmount = 1;

    /// <summary>
    /// Construct Dragon's Claw with no live TriggerManager wiring. The
    /// trigger is attached to the card shape so dispatcher tests see it;
    /// pass the (owner, triggers) overload to register it for live
    /// <see cref="SpellCastEvent"/> dispatch.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Dragon's Claw with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the red-spell
    /// lifegain trigger is registered for live dispatch.
    /// </summary>
    public static Artifact Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact,
        // {2}, colourless). The JSON carries no abilities — the red-spell
        // lifegain trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Artifact)CardDefinitionFactory.Build(definition, owner);

        // CR 603.1 — "Whenever a player casts a red spell, you may gain 1
        // life." Predicate fires for any player's spell whose colours
        // (CR 105.2, via CardColors.GetColors) include red.
        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (e.Spell?.Card is not { } spellCard) return false;
            return CardColors.GetColors(spellCard).Contains(ManaColor.Red);
        });

        // Resolution: gain Dragon's Claw's controller 1 life. "you" = the
        // ability's controller (CR 109.5 / 603.1). v1 always takes the
        // optional gain (see class xmldoc). Read the live controller off
        // the card so a control-change effect points "you" at the current
        // controller.
        var gainEffect = new Effect(
            $"{CardName}: gain {LifeGainAmount} life (controller)",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var you = card.Controller ?? owner;
                if (you.HasLost) return;

                // CR 119.3 — life gain through Player.GainLife so
                // LifeGainedThisTurn / "whenever you gain life" observers
                // see it.
                Fx.GainLife(you, LifeGainAmount);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { gainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
