using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Merfolk Trickster (Dominaria, {U}{U}).
///
/// Creature — Merfolk Wizard 2/2. Oracle text:
///   "Flash
///    When Merfolk Trickster enters, tap target creature an opponent
///    controls. It loses all abilities until end of turn."
///
/// ## Implemented (v1)
/// - 2/2 Merfolk Wizard with mana cost {U}{U}, owner/controller wired.
/// - <b>Flash</b> (CR 702.8) wired as a <see cref="KeywordAbility"/> marker.
///   Same shape as Spell Queller / Solitude / Snapcaster Mage / Dress Down.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When Merfolk Trickster
///   enters, tap target creature an opponent controls. It loses all
///   abilities until end of turn." Wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> with a single 1..1
///   <see cref="TargetRequest"/> (Intent: <see cref="BotIntent.Removal"/>).
///   On resolution the effect reads the trigger's
///   <see cref="TriggeredAbility.ChosenTargets"/>, validates the chosen
///   Creature is still on the battlefield and controlled by anyone other
///   than the Trickster's controller (CR 608.2b — clean no-op on fizzle),
///   taps the target via <see cref="Permanent.Tap"/> (guarded — already-
///   tapped targets only get the lose-abilities rider; the printed "tap"
///   is a no-op when the target is already tapped), and registers a
///   <see cref="LoseAllAbilitiesEffect"/> against the target creature's
///   <see cref="Creature.ActiveEffects"/> with
///   <c>expiresAtEndOfTurn: true</c>. The Layer 6 strip (CR 613.6) scopes
///   to the target via reference-equality predicate and expires at the
///   cleanup step (CR 514.2) — same EOT scope as Oko's +1 sub-effect
///   modulo Oko's "as-long-as-Oko-is-on-the-battlefield" duration.
/// - "Creature an opponent controls" filter: <see cref="TargetRequest.LegalCandidates"/>
///   left empty so the targeting prompt accepts any Creature (same posture
///   as Solitude / Earthshaker Khenra / Plague Engineer); the resolve-time
///   recheck enforces opponent-control + battlefield-zone.
///
/// ## Deferred (v1 gaps)
/// - <b>Choose-time legality filter</b>: <see cref="TargetRequest.LegalCandidates"/>
///   is empty — production callers wanting agent-side filtering populate it
///   themselves (same posture as Solitude / Earthshaker Khenra).
/// - <b>Single-arg dispatcher fallback</b>: when the target has no live
///   <see cref="ContinuousEffectsService"/> wired (shape-only tests), the
///   lose-abilities registration silently no-ops — same posture as
///   Earthshaker Khenra's <see cref="CombatRestrictionEffect"/> grant.
/// </summary>
[CardName("Merfolk Trickster")]
public static class MerfolkTricksterFactory
{
    public const string CardName = "Merfolk Trickster";
    public const string PrintedManaCost = "{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Merfolk Trickster owned and controlled by
    /// <paramref name="owner"/>. The Flash keyword is attached and the ETB
    /// triggered ability is attached to the card shape with a 1..1
    /// "target creature an opponent controls" <see cref="TargetRequest"/>.
    ///
    /// On ETB resolution, the chosen target is gated by a still-on-
    /// battlefield + opponent-control recheck (CR 608.2b). When both pass:
    /// (1) the target is tapped via <see cref="Permanent.Tap"/> (guarded
    /// against already-tapped state — the printed "tap" is a no-op when
    /// the target is already tapped); (2) a <see cref="LoseAllAbilitiesEffect"/>
    /// scoped to the target with <c>expiresAtEndOfTurn: true</c> is
    /// registered against the target's
    /// <see cref="Creature.ActiveEffects"/>. When the target has no live
    /// <see cref="ContinuousEffectsService"/> wired (shape-only tests),
    /// the lose-abilities grant silently no-ops.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. KeywordAbility marker; the cast-flow consults
        // it for instant-speed casting. Same shape as Spell Queller /
        // Solitude / Snapcaster Mage / Dress Down.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 603.6a — ETB triggered ability with target.
        //   "When Merfolk Trickster enters, tap target creature an opponent
        //    controls. It loses all abilities until end of turn."
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            "Merfolk Trickster — tap target creature an opponent controls; it loses all abilities until end of turn",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Creature target) return;

                // CR 608.2b — illegal-target check at resolution.
                if (target.Zone != ZoneType.Battlefield) return;

                // "Creature an opponent controls" — re-validate the
                // controller relationship at resolution (CR 608.2b). The
                // target must be controlled by anyone OTHER than Merfolk
                // Trickster's controller.
                var myController = card.Controller ?? owner;
                if (target.Controller is null) return;
                if (ReferenceEquals(target.Controller, myController)) return;

                // Tap the target (CR 701.20 — "tap" zone-state mutation).
                // Permanent.Tap throws when the permanent is already tapped,
                // so guard the call — an already-tapped target still gets
                // the lose-abilities rider (the printed "tap" half is a
                // no-op in that case).
                if (!target.IsTapped)
                {
                    target.Tap();
                }

                // CR 613.6 / CR 514.2 — Layer 6 ability-removing effect
                // scoped to the chosen creature, expiring in the cleanup
                // step. Registered against the target's own
                // ContinuousEffectsService (Creature.ActiveEffects) —
                // when null (shape-only path), silently no-op (same
                // posture as Earthshaker Khenra's CombatRestrictionEffect
                // grant).
                if (target.ActiveEffects == null) return;
                target.ActiveEffects.Register(new LoseAllAbilitiesEffect(
                    source: card,
                    pool: new[] { target },
                    predicate: c => ReferenceEquals(c, target),
                    expiresAtEndOfTurn: true));
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
