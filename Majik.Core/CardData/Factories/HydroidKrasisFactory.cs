using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hydroid Krasis (Ravnica Allegiance,
/// <c>{X}{G}{U}</c>). Creature — Jellyfish Hydra Beast 0/0.
///
/// Oracle text (Scryfall-verified):
///   "When you cast this spell, you gain half X life and draw half X cards.
///    Round down each time.
///    Flying, trample
///    This creature enters with X +1/+1 counters on it."
///
/// The base shape (name, Creature, Jellyfish + Hydra + Beast subtypes,
/// <c>{X}{G}{U}</c>, 0/0) is materialised from the embedded JSON definition
/// (<c>hydroid-krasis.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flying, Trample, the cast
/// trigger, and the "enters with X +1/+1 counters" rider are layered on as
/// described below (the JSON <c>AbilityDefinition</c> schema doesn't express
/// keyword markers, a variable-X cast trigger, or variable-X ETB counters —
/// same posture as <see cref="TheGooseMotherFactory"/>).
///
/// ## Why this card closes the cast-pipeline X-fold deferral
/// Hydroid Krasis is the canonical "variable-X SPELL cast through the live
/// dispatcher" payoff: its <b>cast trigger</b> (CR 601.2i / 608.2h — "When you
/// cast this spell, …") reads the chosen X to scale the life-gain + draw. For
/// that to pay off, the dispatcher (<see cref="Majik.Core.Game.TurnDriver"/>'s
/// / <see cref="Majik.Core.Api.GameFacade"/>'s DispatchCast) must prompt
/// <c>ChooseXAsync</c> AHEAD of the mana payment, fold X into the cost
/// (<see cref="ValueObjects.ManaCost.AddGenericCost"/>), and stamp it on
/// <see cref="Card.PendingCastX"/> — exactly the cast-pipeline X-fold that
/// PR #2652 wired (and which <see cref="Majik.Core.Game.SpellCastFlow"/>
/// threads via <c>preChosenX</c>). The cast trigger here reads that same
/// stamped X (snapshotted at first fire). An underpaid / un-folded X would
/// leave <see cref="Card.PendingCastX"/> at 0 and Hydroid would gain no life /
/// draw no cards — so this card exercises the deferral end-to-end.
///
/// ## Implemented (v1)
/// <list type="bullet">
///   <item><b>Flying + Trample (CR 702.9 / 702.19)</b> — attached as
///   <see cref="KeywordAbility"/> markers so combat surfaces observe them.</item>
///
///   <item><b>Cast trigger (CR 601.2i / 608.2h)</b> — "When you cast this
///   spell, you gain half X life and draw half X cards. Round down each time."
///   A <see cref="TriggeredAbility"/> over <see cref="Triggers.OnCastSelf"/>.
///   On resolve it reads the cast-time X off <see cref="Card.PendingCastX"/>
///   (stamped by <see cref="Majik.Core.Game.SpellCastFlow"/> right after the
///   dispatcher's <c>ChooseXAsync</c>) and applies floor(X / 2) life-gain +
///   floor(X / 2) cards drawn. "Half X rounded DOWN" = <c>X / 2</c> in integer
///   arithmetic (CR 107.16). The trigger resolves BEFORE Hydroid enters the
///   battlefield (it goes on the stack above the creature spell — CR 601.2i),
///   so it reads X while the stamp is still live; it deliberately does NOT
///   clear the stamp, so the <see cref="EntersWithCountersBinder"/> still reads
///   the same X for the +1/+1 counters at battlefield entry.</item>
///
///   <item><b>Enters with X +1/+1 counters (CR 614.1d / CR 202.3b)</b> — NOT
///   wired here. Identical posture to <see cref="TheGooseMotherFactory"/>: the
///   generic <see cref="EntersWithCountersBinder"/> matches the oracle text
///   ("enters with X +1/+1 counters on it"), reads the chosen X off
///   <see cref="Card.PendingCastX"/>, and stamps the ETB counter intent so the
///   permanent enters WITH the counters (no transient 0/0 window) and Hardened
///   Scales / Doubling Season compose (CR 614). The factory deliberately does
///   NOT <c>MarkSelfManagesEntersWithCounters()</c> and attaches no ETB-counter
///   trigger — self-managing would suppress the binder on the prod Approach-B
///   route (which calls <see cref="NamedCardFactory.Create(string, Player)"/>
///   with no TriggerManager) and yield ZERO counters (the Walking Ballista /
///   Goose Mother bug, #2635).</item>
/// </list>
///
/// ## Wiring overloads
/// <list type="bullet">
///   <item><see cref="Create(Player)"/> — shape only; the cast trigger is
///   attached for shape / dispatcher tests but not registered with any
///   <see cref="TriggerManager"/>.</item>
///   <item><see cref="Create(Player, TriggerManager?)"/> — registers the cast
///   trigger so the matching <see cref="SpellCastEvent"/> lands it on the stack
///   automatically (CR 603.2).</item>
/// </list>
/// </summary>
[CardName("Hydroid Krasis")]
public static class HydroidKrasisFactory
{
    public const string CardName = "Hydroid Krasis";
    public const string Slug = "hydroid-krasis";

    private const string FlyingKeyword = "Flying";
    private const string TrampleKeyword = "Trample";

    /// <summary>
    /// Construct Hydroid Krasis with no live wiring. The cast trigger is
    /// attached for shape observability but not registered with any
    /// <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Hydroid Krasis with an optional <see cref="TriggerManager"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the cast trigger registers so the
    /// matching <see cref="SpellCastEvent"/> lands the ability on the stack
    /// automatically (CR 603.2).</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Jellyfish + Hydra + Beast subtypes, {X}{G}{U}, 0/0). The JSON carries
        // no abilities — Flying, Trample, and the cast trigger are layered on
        // below; the "enters with X +1/+1 counters" rider is owned by the
        // generic EntersWithCountersBinder on the prod route (see xmldoc).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner, replacements: null);

        card.SetController(owner);

        // CR 702.9 / 702.19 — Flying + Trample. KeywordAbility markers only;
        // consumed by the combat surfaces (CombatAbilities.HasFlying /
        // HasTrample) so block-legality + trample assignment observe them.
        card.AddAbility(new KeywordAbility(FlyingKeyword, card, owner));
        card.AddAbility(new KeywordAbility(TrampleKeyword, card, owner));

        // ----------------------------------------------------------------
        // Cast trigger — CR 601.2i / 608.2h.
        //   "When you cast this spell, you gain half X life and draw half X
        //    cards. Round down each time."
        // floor(X / 2) = X / 2 in integer arithmetic (CR 107.16 — round DOWN).
        // Reads the cast-time X off Card.PendingCastX (stamped by the
        // dispatcher's X-fold, PR #2652). Snapshot once at first fire so a
        // later non-cast re-entry can't pick up a stale value, and do NOT clear
        // the stamp — the EntersWithCountersBinder still needs it to read the
        // same X for the ETB counters when Hydroid enters (this trigger
        // resolves first, above the creature spell on the stack).
        // ----------------------------------------------------------------
        var castEffect = new Effect(
            $"{CardName}: gain half X life and draw half X cards, round down (CR 601.2i)",
            () =>
            {
                var x = card.PendingCastX ?? 0;
                var half = x / 2; // floor(X / 2)
                var controller = card.Controller ?? owner;
                if (half > 0)
                {
                    Fx.GainLife(controller, half);
                    Fx.DrawCards(controller, half);
                }
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnCastSelf(card),
            effects: new IEffect[] { castEffect },
            // CR 601.2i — "When you cast this spell" fires while the card is on
            // the stack as a spell; keep the ability live in the Stack zone (the
            // creature has not entered the battlefield yet when this resolves).
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
