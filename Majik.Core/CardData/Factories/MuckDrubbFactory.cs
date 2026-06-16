using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Muck Drubb (Eldritch Moon, <c>{3}{B}{B}</c>).
///
/// Creature — Beast 3/3. Oracle text (Scryfall):
///   "Flash
///    When this creature enters, change the target of target spell that
///    targets only a single creature to this creature.
///    Madness {2}{B} (If you discard this card, discard it into exile. When
///    you do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Mechanic paid down
/// This factory closes the <c>muck-drubb-change-spell-target-redirection</c>
/// deferral: the engine previously had no seam to retarget a spell ALREADY on
/// the stack in place (distinct from the spell-copy retarget rider closed in
/// #2722, which only rewrites a fresh copy's targets — CR 707.10a). The new
/// primitive is <see cref="SpellTargetRedirector"/> (CR 114.6 — "change the
/// target(s) of a spell"), and the new targeting predicate is
/// <see cref="TargetFilters.SpellTargetingSingleCreatureRequest"/> /
/// <see cref="TargetFilters.SpellTargetsSingleCreature"/>.
///
/// ## Implemented (v1)
/// - 3/3 Creature — Beast at {3}{B}{B} built in code (no JSON shape def — same
///   in-code posture as <see cref="TishanasTidebinderFactory"/>).
/// - <b>Flash</b> (CR 702.8) keyword marker.
/// - <b>ETB triggered ability</b> (CR 603.6a) via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> declaring a 1..1
///   <see cref="TargetFilters.SpellTargetingSingleCreatureRequest"/>: the only
///   legal targets are spells on the stack whose chosen targets are EXACTLY one
///   creature.
/// - <b>Resolve</b>: re-checks legality (CR 608.2b) and changes the chosen
///   spell's lone target to Muck Drubb itself via
///   <see cref="SpellTargetRedirector.RedirectSingleTarget"/> (CR 114.6 — the
///   new target is forced by the printed text, not a player choice). Because the
///   redirected spell reads its <c>ChosenTargets</c> live when IT resolves (the
///   redirection ability resolves first, on top of the stack), the redirect
///   takes effect.
/// - <b>Madness {2}{B}</b> (CR 702.35) — Muck Drubb is in
///   <see cref="Keywords.MadnessCatalog"/>, so madness is recognized
///   intrinsically; the fully-wired overload registers the discard → exile
///   <see cref="MadnessReplacement"/> on the supplied
///   <see cref="ReplacementBus"/>, same posture as
///   <see cref="AsylumVisitorFactory"/>.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (ETB trigger attached for
///   observability; the redirect is a no-op without a live stack).
/// - <see cref="Create(Player, Majik.Core.Stack.Stack?)"/> — redirect wired to
///   a live stack.
/// - <see cref="Create(Player, Majik.Core.Stack.Stack?, ReplacementBus?)"/> —
///   redirect + Madness discard-replacement.
/// </summary>
[CardName("Muck Drubb")]
public static class MuckDrubbFactory
{
    public const string CardName = "Muck Drubb";
    public const string PrintedManaCost = "{3}{B}{B}";
    public const string MadnessCost = "{2}{B}";
    public const int Power = 3;
    public const int Toughness = 3;

    /// <summary>The madness alternative cost for casting from exile (CR 702.35).</summary>
    public static Costs.MadnessAlternativeCost MadnessAltCost { get; } =
        new(ManaCost.Parse(MadnessCost));

    /// <summary>Shape only — no live stack / replacement bus.</summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, replacements: null);

    /// <summary>Redirect wired to a live stack (no Madness replacement bus).</summary>
    public static Creature Create(Player owner, Majik.Core.Stack.Stack? stack) =>
        Create(owner, stack, replacements: null);

    /// <summary>
    /// Construct Muck Drubb with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Live stack — required for the ETB redirect to rewrite
    /// the chosen spell's target. <see langword="null"/> in pure-shape tests; the
    /// redirect becomes a clean no-op.</param>
    /// <param name="replacements">Replacement bus — when supplied, the Madness
    /// discard → exile replacement (CR 702.35) registers so discarding this card
    /// sends it to exile (castable for {2}{B}) instead of the graveyard.</param>
    public static Creature Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash keyword marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // ----------------------------------------------------------------
        // CR 603.6a — ETB triggered ability. 1..1 "target spell that targets
        // only a single creature" (CR 114.6 redirection).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName} — change the target of target spell to this creature",
            () =>
            {
                if (etbTrigger == null) return;
                if (stack == null) return;

                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                // CR 608.2b — recheck legality at resolution: the chosen object
                // must still be a spell on the stack that targets only a single
                // creature.
                var raw = chosen[0][0];
                if (!TargetFilters.SpellTargetsSingleCreature(raw)) return;
                if (raw is not Majik.Core.Spells.Spell targetSpell) return;

                // CR 114.6 — change the spell's lone target to THIS creature.
                SpellTargetRedirector.RedirectSingleTarget(stack, targetSpell, card);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[] { TargetFilters.SpellTargetingSingleCreatureRequest() });

        card.AddAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Madness {2}{B} — CR 702.35. Register the discard → exile replacement
        // so discarding this card sends it to exile (castable for {2}{B})
        // instead of the graveyard. Madness recognition itself is intrinsic via
        // MadnessCatalog (Muck Drubb is catalogued).
        // ----------------------------------------------------------------
        replacements?.Register<ZoneMoveIntent>(new MadnessReplacement(card));

        return card;
    }
}
