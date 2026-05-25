using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Foundation Breaker (Modern Horizons 2, {2}{G}).
///
/// Creature — Elemental 3/2. Oracle text:
///   "When this creature enters, you may destroy target artifact or
///    enchantment.
///    Evoke {1}{G}"
///
/// ## Implemented (v1)
/// - 3/2 Elemental, mana cost {2}{G}.
/// - Evoke keyword marker via <see cref="KeywordAbility"/> ("Evoke") so the
///   data-driven importer surface lines up with the named factory.
/// - Pure-mana evoke alt-cost wired via
///   <see cref="Majik.Core.Costs.EvokeAlternativeCost"/> at the call site
///   (mirrors classic Lorwyn evokers like Mulldrifter — non-pitch). The
///   evoke sacrifice trigger (CR 702.74b) is attached here via
///   <see cref="EvokeFactory.Build"/>.
/// - <b>ETB triggered ability</b>: 0..1 "target artifact or enchantment"
///   <see cref="TargetRequest"/> (the printed "you may" collapses to
///   MinTargets = 0, MaxTargets = 1 — the controller declines by picking
///   no target, mirroring <see cref="SkyclaveApparitionFactory"/>). On
///   resolution, if a target is present and still a legal artifact or
///   enchantment on the battlefield, it is destroyed via
///   <see cref="Fx.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — Indestructible
///   per CR 702.12 cancels; regeneration per CR 701.15 consumed).
///
/// ## Deferred (v1 gaps)
/// - <b>Target-legality filter in ActionValidator</b>: the validator does
///   not yet restrict ETB target picks to "artifact or enchantment" —
///   resolution-time guard handles illegal targets (CR 608.2b). Same
///   posture as Caustic Caterpillar / Aether Spellbomb.
/// - <b>"You may" decline prompt</b>: the engine has no first-class
///   yes/no prompt on ETB triggers, so the "may" is modelled by the
///   MinTargets = 0 ceiling — the controller picks 0 targets to decline.
///   Same pattern as Skyclave Apparition.
/// </summary>
[CardName("Foundation Breaker")]
public static class FoundationBreakerFactory
{
    public const string CardName = "Foundation Breaker";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>Construct Foundation Breaker owned and controlled by
    /// <paramref name="owner"/>. Attaches the Evoke keyword marker, the
    /// evoke-sacrifice trigger (<see cref="EvokeFactory"/>), and the
    /// printed ETB "destroy target artifact or enchantment" trigger.</summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Keyword markers — CR 702.74 (Evoke). Attach inline so the
        // NamedCardFactory path matches the data-driven KeywordBinder
        // result (same shape as the MH2 incarnation cycle factories).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Evoke", card, owner));

        // ----------------------------------------------------------------
        // Printed evoke sacrifice trigger (CR 702.74b).
        //   "When this creature enters, if its evoke cost was paid,
        //    sacrifice it."
        // Pure-mana evoke ({1}{G}) — the alt-cost is announced at cast
        // time via Majik.Core.Costs.EvokeAlternativeCost(ManaCost.Parse("{1}{G}"))
        // and OnResolved flips Creature.EvokeWasPaid which the intervening-if
        // below reads. See EvokeFactory.Build for the trigger body.
        // ----------------------------------------------------------------
        card.AddAbility(EvokeFactory.Build(card));

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.6a).
        //   "When this creature enters, you may destroy target artifact
        //    or enchantment."
        // 0..1 target — "you may" maps to MinTargets = 0. Resolution
        // guards on still-legal artifact/enchantment on the battlefield
        // (CR 608.2b) and routes through the Destroy reason so
        // indestructible / regeneration interact correctly.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: may destroy target artifact or enchantment",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;

                // "May decline" — 0 targets chosen → clean no-op (CR 601.2c).
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — resolution-time legality check.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact)
                    && !target.HasType(CardType.Enchantment))
                {
                    return;
                }

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // active regeneration shield (CR 701.15) is consumed.
                Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or enchantment",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etb);

        return card;
    }
}
