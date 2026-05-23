using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Earthshaker Khenra (Hour of Devastation,
/// Creature — Minotaur Warrior {1}{R}).
///
/// Oracle text:
///   "Haste.
///    When Earthshaker Khenra enters, target creature with power 2 or
///    less can't block this turn.
///    Eternalize {5}{R}{R}"
///
/// ## Implemented (v1)
/// - 2/1 Creature — Minotaur Warrior, mana cost {1}{R}, owner/controller
///   wired. (User brief said "Jackal Warrior" but the printed oracle is
///   "Minotaur Warrior"; <see cref="CardSubtype"/> has Minotaur and the
///   comp rules are canonical — CLAUDE.md "Rules authority".)
/// - <b>Haste</b> (CR 702.10) wired as a <see cref="KeywordAbility"/>
///   marker on the card; <c>CombatAbilities.HasHaste</c> reads it. Same
///   shape as Goblin Chieftain's printed Haste.
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature
///   enters, target creature with power 2 or less can't block this
///   turn." Wired via <see cref="Triggers.OnEnterBattlefieldSelf"/> with
///   a single 1..1 <see cref="TargetRequest"/> ("target creature with
///   power 2 or less"). On resolution the effect reads the trigger's
///   <see cref="TriggeredAbility.ChosenTargets"/>, validates the chosen
///   Creature is still on the battlefield with power ≤ 2 at resolution
///   (CR 608.2b illegal-target check), and registers a
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBlock"/> targeting that
///   creature against the creature's
///   <see cref="ContinuousEffect.ActiveEffects"/> (looked up on the
///   target Creature's <see cref="Creature.ActiveEffects"/> handle).
///   The restriction is EOT-scoped (<c>expiresAtEndOfTurn: true</c>) —
///   the default for <see cref="CombatRestrictionEffect"/>, matching
///   the printed "this turn" rider. Same restriction shape used by
///   <see cref="Majik.Core.CardData.SpellTemplates.Templates.Misc"/>
///   bind templates (e.g. UpToNCantBlock / Falter).
///
/// ## Deferred (v1 gaps)
/// - <b>Eternalize {5}{R}{R}</b> (CR 702.117 — exile from graveyard,
///   return as a 4/4 black Zombie Minotaur Warrior token copy with no
///   mana cost): not implemented. Eternalize parallels Unearth — needs
///   an alternative-cost / cast-from-graveyard-as-token pipeline
///   (sibling of the cast-from-exile / Priest-of-Fell-Rites unearth
///   path). When that pipeline ships, this factory grows an
///   <c>BuildEternalizeCost</c> + <c>BuildEternalizeToken</c> helper
///   matching <c>BonecrusherGiantFactory.BuildAdventureSpell</c>'s
///   helper shape.
/// - <b>"Power 2 or less" target legality at choose-time</b>: the
///   <see cref="TargetRequest.LegalCandidates"/> list is left empty so
///   the engine's targeting prompt accepts any Creature (same posture
///   as Solitude / Kraul Harpooner). The resolution-time check rejects
///   targets whose <see cref="Creature.GetPower"/> exceeds 2 at
///   resolution (CR 608.2b) — production callers wanting agent-side
///   filtering should populate <see cref="TargetRequest.LegalCandidates"/>
///   themselves.
/// </summary>
public static class EarthshakerKhenraFactory
{
    public const string CardName = "Earthshaker Khenra";
    public const string PrintedManaCost = "{1}{R}";
    public const int Power = 2;
    public const int Toughness = 1;
    public const int CannotBlockPowerThreshold = 2;

    /// <summary>
    /// Construct Earthshaker Khenra owned and controlled by
    /// <paramref name="owner"/>. The Haste keyword is attached and the
    /// ETB triggered ability is attached to the card shape with a
    /// 1..1 "target creature with power 2 or less" <see cref="TargetRequest"/>.
    ///
    /// On ETB resolution, the chosen target is gated by a power ≤ 2
    /// recheck (CR 608.2b) and a still-on-battlefield check; if both
    /// pass, a <see cref="CombatRestrictionEffect"/> with
    /// <see cref="CombatRestriction.CannotBlock"/> is registered against
    /// the target creature's <see cref="Creature.ActiveEffects"/>. When
    /// the target has no live <see cref="ContinuousEffectsService"/>
    /// wired (shape-only tests) the restriction registration is a
    /// no-op and the effect body exits cleanly.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Minotaur, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste. KeywordAbility marker; CombatAbilities.HasHaste
        // reads it. Same shape as the printed Haste keyword on Goblin
        // Chieftain / Goblin Rabblemaster.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 603.6a — ETB triggered ability with target.
        // "When Earthshaker Khenra enters, target creature with power 2
        //  or less can't block this turn." Same pattern as
        //  SolitudeFactory's ETB exile-target-creature trigger: declare
        //  a 1..1 TargetRequest, read the chosen target out of
        //  ChosenTargets at resolution, and apply the effect.
        TriggeredAbility? etbTrigger = null;
        var etbEffect = new Effect(
            "Earthshaker Khenra — target creature with power 2 or less can't block this turn",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b
                // CR 608.2b — recheck "power 2 or less" at resolution.
                if (target.GetPower() > CannotBlockPowerThreshold) return;

                // CR 509.1c — register CannotBlock restriction scoped to
                // the target. Default ExpiresAtEndOfTurn matches the
                // printed "this turn" rider (CR 514.2). The restriction
                // lives on the target creature's ContinuousEffectsService
                // (Creature.ActiveEffects) — the combat validator queries
                // there. When ActiveEffects is null (shape tests), the
                // grant silently no-ops.
                if (target.ActiveEffects == null) return;
                target.ActiveEffects.Register(
                    new CombatRestrictionEffect(CombatRestriction.CannotBlock, target));
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
                    Description: "target creature with power 2 or less",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
