using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mistbind Clique (Lorwyn, {3}{U}{U}).
///
/// Creature — Faerie Wizard 4/4. Oracle text:
///   "Flash
///    Flying
///    Champion a Faerie (When this enters, sacrifice it unless you exile
///    another Faerie you control. When this leaves the battlefield, that
///    card returns to the battlefield.)
///    When Mistbind Clique enters, tap all lands target player controls."
///
/// ## Implemented (v1)
/// - 4/4 Creature — Faerie Wizard at {3}{U}{U} with Flash (CR 702.8) +
///   Flying (CR 702.9) keyword markers.
/// - <b>ETB triggered ability</b> (CR 603.6a) — declares a 1..1
///   <see cref="TargetRequest"/> for "target player". On resolution, every
///   <see cref="Land"/> on the target player's battlefield is tapped (CR
///   701.20 — "tap" sets the permanent's tapped status). Already-tapped
///   lands are left alone (no-op via <see cref="Permanent.IsTapped"/>
///   guard); illegal target (player left the game) → no-op per CR 608.2b.
///
/// ## Champion a Faerie (CR 702.71) — DEFERRED
/// The engine has no Champion primitive yet. Per Comp Rules 702.71a:
///   "Champion an [object] means 'When this permanent enters, sacrifice it
///    unless you exile another [object] you control. When this permanent
///    leaves the battlefield, return the exiled card to the battlefield
///    under its owner's control.'"
/// Both halves are linked-ability replacement-style triggers requiring a
/// shared "captured exile" slot — same shape as Spell Queller's ETB/LTB
/// pair, but with a sacrifice-unless-you-exile-from-battlefield ETB cost
/// rather than a stack-target. This factory ships the simpler half of
/// Mistbind (ETB tap-all-lands) and explicitly defers Champion to a
/// follow-up. The bot/AI will play Mistbind without paying the Champion
/// cost; this matches the directive's fallback ("ship Mistbind without
/// Champion ETB-self-exile rider + just tap-all-lands"). The card still
/// counts as a Faerie for Spellstutter Sprite / Scion of Oona purposes.
///
/// ## Target gathering at choose time
/// The <see cref="TargetRequest.CandidateGatherer"/> enumerates all players
/// in the game from <see cref="GameContext.AllPlayers"/>. The bot can
/// rationally pick an opponent — Mistbind's tap-all-lands ETB is a
/// classic UB Faeries upkeep-tempo play.
/// </summary>
[CardName("Mistbind Clique")]
public static class MistbindCliqueFactory
{
    public const string CardName = "Mistbind Clique";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Mistbind Clique with no <see cref="TriggerManager"/>. The
    /// ETB triggered ability is attached but not registered. Suitable for
    /// shape / dispatcher tests; the ETB effect can still be driven
    /// manually via <see cref="TriggeredAbility.SetChosenTargets"/> +
    /// <see cref="IEffect.Execute"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Mistbind Clique with optional <see cref="TriggerManager"/>.
    /// When supplied, the ETB triggered ability is registered so the
    /// enter-the-battlefield event lands it on the stack automatically.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager to register the ETB ability
    /// against. May be null — the ability is still attached to the card.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 702.9 — Flying. Combat blocking restriction.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.20 (Tap).
        //   "When Mistbind Clique enters, tap all lands target player
        //    controls."
        // Target is supplied via TriggeredAbility.SetChosenTargets.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            "Mistbind Clique — tap all lands target player controls (CR 701.20)",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Player victim) return;

                // CR 608.2b — illegal-on-resolution check is implicit:
                // a removed player has no battlefield contents anyway, so
                // the foreach below no-ops naturally. We still guard for a
                // defensive null Battlefield reference.
                var battlefield = victim.Zones?.Battlefield;
                if (battlefield == null) return;

                // Snapshot to avoid iteration-mutation surprises if a
                // future replacement effect were to move lands mid-tap.
                var lands = battlefield.GetCards()
                    .OfType<Land>()
                    .ToList();
                foreach (var land in lands)
                {
                    if (!land.IsTapped) land.Tap();
                }
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
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    // No specialised intent — bot's player-targeting picker
                    // defaults to choosing an opponent for a tempo-negative
                    // effect like "tap all your lands".
                    Intent: BotIntent.None,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
