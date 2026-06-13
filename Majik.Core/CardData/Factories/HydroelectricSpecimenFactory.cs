using Majik.Core.Abilities;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Hydroelectric Specimen // Hydroelectric Laboratory (Modern Horizons 3).
///
/// Creature — Weird 1/4. Oracle text (front, verified against Scryfall):
///   "Flash
///    When this creature enters, you may change the target of target instant
///    or sorcery spell with a single target to this creature."
///
/// Back face — <see cref="HydroelectricLaboratoryFactory"/> (Land — "As this
/// land enters, you may pay 3 life. If you don't, it enters tapped." /
/// "{T}: Add {U}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6) — real cast-either-face
///
/// Cast-either-face is modelled exactly like the structurally identical
/// creature-front // tapland-back MDFC <see cref="SkyclaveClericFactory"/> /
/// <see cref="SkyclaveBasilicaFactory"/>: two independent
/// <c>[CardName]</c>-dispatched factories. The front-face creature built here
/// carries an <see cref="MdfcState"/> with a castable <see cref="MdfcFace.Land"/>
/// back-face descriptor; at cast time <see cref="Majik.Core.Game.MdfcCastFlow"/>
/// offers the controller a face choice and, when the back (land) face is
/// chosen, materializes a fresh <see cref="HydroelectricLaboratoryFactory"/>
/// land instance with no stack (CR 305). No transform happens (CR 712.4).
///
/// ## Identity + abilities built in code
///
/// Unlike the JSON-driven <see cref="SkyclaveClericFactory"/> body (a simple
/// gain-life ETB the declarative schema models), the ETB here is a
/// change-the-target effect that needs a custom resolve-time closure + live
/// stack access — the same shape as <see cref="TishanasTidebinderFactory"/>'s
/// counter-an-ability ETB. So identity (Weird 1/4 {2}{U}), the Flash keyword,
/// and the redirect trigger are all wired in code here.
///
/// ## Implemented (v1)
///
/// - 1/4 Creature — Weird, mana cost {2}{U}, mono-blue (one {U} pip per CR
///   202.2c), owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Hydroelectric Specimen",
///   back = "Hydroelectric Laboratory") with a castable
///   <see cref="MdfcFace.Land"/> back face; starts on the front face.
/// - <b>Flash</b> (CR 702.8) — <see cref="KeywordAbility"/> marker, same
///   wiring as <see cref="TishanasTidebinderFactory"/>.
/// - <b>ETB redirect</b> (CR 603.6a + CR 114.6) — "When this creature enters,
///   you may change the target of target instant or sorcery spell with a
///   single target to this creature." Wired as an ETB
///   <see cref="TriggeredAbility"/> (<see cref="Triggers.OnEnterBattlefieldSelf"/>)
///   declaring a 0..1 "target instant or sorcery spell with a single target"
///   <see cref="TargetRequest"/> ("you may" → <c>MinTargets = 0</c>, same
///   modal-zero shape as Tishana's "up to one"). On resolution: re-check
///   legality (CR 608.2b — the chosen target must still be a <see cref="Spell"/>
///   whose <see cref="Spell.ChosenTargets"/> has exactly one entry, and whose
///   card is an instant or sorcery), then rewrite that single chosen target to
///   THIS creature — the same <see cref="Spell.ChosenTargets"/> rewrite
///   <see cref="SpellskiteFactory"/> uses (CR 114.6).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Lossy redirect semantics</b>: same documented stub as
///   <see cref="SpellskiteFactory"/> / <see cref="Majik.Core.Services.SpellRedirector"/>
///   — the pre-built effect closures of the redirected spell baked in their
///   original target at cast time, so v1 rewrites the spell's
///   <see cref="Spell.ChosenTargets"/> bookkeeping (visible to the CR 608.2b
///   legality recheck) without flipping the actual damage / counter / destroy
///   landing site. A future <see cref="Majik.Core.Game.SpellCastFlow"/> +
///   <c>StackResolver</c> revision can promote the stub to real semantics
///   without touching this factory.
/// - <b>Production stack wiring</b>: like
///   <see cref="TishanasTidebinderFactory"/>, the ETB redirect needs a live
///   stack to find the target spell. The
///   <see cref="NamedCardFactory"/> production dispatch builds the creature
///   with no stack, so the redirect is a clean no-op in production today
///   (the trigger still fires harmlessly) and is exercised via the
///   stack-aware <see cref="Create(Player, Majik.Core.Stack.Stack?)"/>
///   overload in tests — the same posture Tishana ships with.
///
/// ## References
///
/// - <see cref="SkyclaveClericFactory"/> — companion creature-front //
///   tapland-back MDFC with the same castable-land-back MdfcState shape.
/// - <see cref="TishanasTidebinderFactory"/> — the Flash creature whose ETB
///   targets a stack object; this factory mirrors its Flash + ETB-target
///   wiring (redirect instead of counter).
/// - <see cref="SpellskiteFactory"/> — the change-target-to-this-permanent
///   <see cref="Spell.ChosenTargets"/> rewrite this ETB reuses.
/// </summary>
[CardName("Hydroelectric Specimen")]
public static class HydroelectricSpecimenFactory
{
    public const string CardName = "Hydroelectric Specimen";
    public const string BackName = "Hydroelectric Laboratory";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 1;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Hydroelectric Specimen owned and controlled by
    /// <paramref name="owner"/>. The Flash keyword marker, the ETB redirect
    /// trigger, and the castable land back face are attached in code. This is
    /// the overload <see cref="NamedCardFactory"/> dispatches to (no stack —
    /// the redirect is a harmless no-op in production, same as Tishana).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, stack: null);

    /// <summary>
    /// Construct Hydroelectric Specimen with an optional live
    /// <see cref="Majik.Core.Stack.Stack"/> for the ETB redirect effect.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Live stack — required for the redirect effect to
    /// find + rewrite the target spell. <see langword="null"/> in pure-shape
    /// tests / production dispatch; the redirect becomes a no-op (the trigger
    /// still fires and resolves harmlessly).</param>
    public static Creature Create(Player owner, Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Weird });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash keyword marker.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at cast time and materializes a fresh
        // back-face land instance (wired to its pay-3-life-or-tapped ETB
        // replacement via the supplied ReplacementBus) when chosen. No
        // transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                HydroelectricLaboratoryFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        // ----------------------------------------------------------------
        // CR 603.6a — ETB triggered ability. 0..1 target instant or sorcery
        // spell with a single target ("you may" → MinTargets = 0). On
        // resolution, rewrite that spell's single chosen target to THIS
        // creature (CR 114.6).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName} — change the target of target single-target instant/sorcery to {CardName}",
            () =>
            {
                if (etbTrigger == null) return;
                if (stack == null) return;

                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0)
                {
                    // "you may" — controller chose no target. Clean no-op
                    // (CR 700.2 — optional target selection).
                    return;
                }

                var raw = chosen[0][0];

                // CR 608.2b — recheck legality at resolution. Legal target:
                // a Spell still on the stack whose card is an instant or
                // sorcery and which has EXACTLY one chosen target.
                if (raw is not Spell spell) return;
                if (!stack.GetAll().Contains(spell)) return;
                if (!spell.Card.HasType(CardType.Instant)
                    && !spell.Card.HasType(CardType.Sorcery)) return;
                if (spell.ChosenTargets.Count != 1) return;

                // CR 114.6 — change the target to this creature. Same
                // ChosenTargets rewrite Spellskite uses.
                spell.ChosenTargets[0] = card;
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery spell with a single target",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Protection),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
