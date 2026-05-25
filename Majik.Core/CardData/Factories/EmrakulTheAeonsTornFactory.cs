using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emrakul, the Aeons Torn (Rise of the Eldrazi,
/// {15}).
///
/// Legendary Creature — Eldrazi 15/15. Oracle text (Scryfall, verified):
///   "Emrakul, the Aeons Torn can't be countered.
///    When you cast this spell, take an extra turn after this one.
///    Flying, protection from coloured spells, annihilator 6."
///
/// ## Implemented (v1)
/// - 15/15 Legendary Creature — Eldrazi at {15}.
/// - <b>Flying (CR 702.9)</b>: <see cref="KeywordAbility"/>("Flying")
///   marker — combat code reads via
///   <see cref="Majik.Core.Combat.CombatAbilities"/>, same wiring shape
///   as every other named factory.
/// - <b>Annihilator 6 (CR 702.86)</b>: shipped via
///   <see cref="AnnihilatorFactory.Build"/> (PR #496). Discoverability
///   <see cref="KeywordAbility"/>("Annihilator", arg: 6) marker stamped
///   alongside so keyword scans see it (mirrors
///   <see cref="UlamogsCrusherFactory"/>'s posture).
/// - <b>Cast-uncounterable (CR 701.5b)</b>: structural — the card carries
///   a <see cref="KeywordAbility"/>("Uncounterable") marker that
///   <see cref="SpellCastFlow"/> reads at cast time to stamp
///   <see cref="Majik.Core.Spells.Spell.CannotBeCountered"/> on the
///   resolving spell. <see cref="OracleSpellBinder.RemoveFromStack"/>
///   short-circuits the pop when the flag is set (returning false), and
///   every counter-effect path (Fx.Counter + counter templates) gates the
///   "card → graveyard" tail on that return so the spell stays on the
///   stack and resolves normally.
/// - <b>Protection from coloured spells (CR 702.16)</b>: shipped as a
///   <see cref="ProtectionAbility"/> carrying a
///   <see cref="ProtectionAbility.SpellPredicate"/> closure
///   <c>spell => CardColors.GetColors(spell.Card).Count > 0</c>.
///   Targeting / damage / blocking gates that hold a live spell handle
///   consult <see cref="Majik.Core.Rules.Protection.HasProtectionFromSpell"/>;
///   the legacy colour-string surface
///   (<see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>)
///   is untouched. Quality string "coloured spells" is the marker /
///   discoverability label.
/// - <b>Cast trigger — take an extra turn (CR 603.6a / CR 603.10)</b>:
///   triggered ability over <see cref="SpellCastEvent"/> filtered to
///   <c>e.Spell.Card == card</c> (same self-cast detection pattern as
///   <see cref="UlamogTheCeaselessHungerFactory"/>); on resolution
///   <see cref="TurnManager.AddExtraTurn"/> enqueues an extra turn for
///   the spell's controller. <see cref="ZoneType.Stack"/> active zone —
///   the cast trigger lands on the stack while Emrakul is itself still
///   on the stack as a spell.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. All ability markers +
///   the cast trigger + protection predicate are attached; the trigger
///   isn't registered with any <see cref="TriggerManager"/>; the
///   extra-turn enqueue is a no-op without a <see cref="TurnManager"/>.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, TurnManager?, TriggerManager?, Func{Player, IPlayerAgent?}?)"/>
///   — fully wired. The cast trigger registers with the trigger bus;
///   extra-turn resolution calls <paramref name="turns"/>.AddExtraTurn;
///   the Annihilator trigger consults <paramref name="agentSelector"/>
///   for the defender's sacrifice picks.
/// </summary>
[CardName("Emrakul, the Aeons Torn")]
public static class EmrakulTheAeonsTornFactory
{
    public const string CardName = "Emrakul, the Aeons Torn";
    public const string PrintedManaCost = "{15}";
    public const int Power = 15;
    public const int Toughness = 15;
    public const int AnnihilatorValue = 6;

    /// <summary>
    /// Construct Emrakul with no live wiring. All markers + the cast
    /// trigger + protection predicate are attached; nothing registers
    /// with a trigger bus or turn manager. Suitable for dispatcher /
    /// structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, turns: null, triggers: null, agentSelector: null);

    /// <summary>
    /// Construct Emrakul with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="turns">When supplied, the cast trigger's resolution
    /// enqueues an extra turn for Emrakul's controller via
    /// <see cref="TurnManager.AddExtraTurn"/>.</param>
    /// <param name="triggers">When supplied, both the cast trigger and
    /// the Annihilator trigger register with the bus so attack /
    /// cast events automatically place the abilities on the stack
    /// (CR 603.2).</param>
    /// <param name="agentSelector">When supplied, the Annihilator
    /// trigger consults the defender's
    /// <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> for sacrifice
    /// picks; null falls back to deterministic first-N-permanents.</param>
    public static Creature Create(
        Player owner,
        TurnManager? turns,
        TriggerManager? triggers,
        Func<Player, IPlayerAgent?>? agentSelector)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker. Combat-side reads via
        // CombatAbilities; the marker keeps the keyword scan surface
        // uniform.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 701.5b — cast-uncounterable marker. Read at cast time by
        // SpellCastFlow to stamp Spell.CannotBeCountered on the
        // resolving spell. Marker form keeps the surface symmetric
        // with the rest of the keyword pipeline (Trample, Haste, …) so
        // future "uncounterable spells" (Apocalypse Hydra) can share
        // the same trigger.
        card.AddAbility(new KeywordAbility("Uncounterable", card, owner));

        // CR 702.16 — Protection from coloured spells. The predicate
        // closes over CardColors so the same spell instance flowing
        // through targeting / damage / blocking gates is evaluated
        // against its live colour identity (CR 105 — colour can be
        // mutated by continuous effects; we read off the card at gate
        // time, not at cast time). The quality string "coloured spells"
        // is the discoverability marker — Rules.Protection.HasProtectionFromColor
        // is intentionally NOT extended to recognise it because the
        // legacy colour-string surface is for single-colour predicates;
        // ProtectionAbility.SpellPredicate is the canonical surface for
        // "any coloured spell" / future multi-colour clauses.
        card.AddAbility(new ProtectionAbility(
            "coloured spells",
            spellPredicate: spell => CardColors.GetColors(spell.Card).Count > 0));

        // CR 702.86 — Annihilator 6. Marker for discoverability + the
        // wired trigger (AnnihilatorFactory.Build) so attacks fire
        // through the bus. Same posture as Ulamog's Crusher.
        card.AddAbility(new KeywordAbility(
            "Annihilator", card, owner, arg: AnnihilatorValue));
        var annihilator = AnnihilatorFactory.Build(
            source: card,
            n: AnnihilatorValue,
            agentSelector: agentSelector);
        card.AddAbility(annihilator);
        triggers?.RegisterTriggeredAbility(annihilator);

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.6a / CR 603.10.
        //   "When you cast this spell, take an extra turn after this one."
        // Same self-cast detection pattern as Ulamog, the Ceaseless
        // Hunger — filter SpellCastEvent on e.Spell.Card == card,
        // active in the Stack zone (Emrakul is on the stack at cast
        // time). The resolution enqueues an extra turn for the spell's
        // controller via TurnManager.AddExtraTurn; without a TurnManager
        // the resolution is a no-op (shape-only path).
        // ----------------------------------------------------------------
        Player? capturedController = null;
        var castCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) =>
            {
                if (!ReferenceEquals(e.Spell.Card, card)) return false;
                capturedController = e.Spell.Controller;
                return true;
            });

        var castEffect = new Effect(
            $"{CardName}: take an extra turn after this one (cast trigger)",
            () =>
            {
                var controller = capturedController ?? card.Controller;
                if (controller == null) return;
                turns?.AddExtraTurn(controller);
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
