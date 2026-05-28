using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Devourer of Destiny (The Brothers' War / Modern
/// Horizons reprint, {5}{C}{C}).
///
/// Creature — Eldrazi 6/6. Oracle text (Scryfall, verified):
///   "You may reveal this card from your opening hand. If you do, at the
///    beginning of your first upkeep, look at the top four cards of your
///    library. You may put one of those cards back on top of your library.
///    Exile the rest.
///    When you cast this spell, exile target permanent that's one or more
///    colors."
///
/// ## Implemented (v1)
///
/// - 6/6 Creature — Eldrazi, mana cost <c>{5}{C}{C}</c>.
///   Colourless via <see cref="CardColors"/> (no W/U/B/R/G pips; the two
///   <c>{C}</c> pips don't add colour — CR 105.2a says colourless mana
///   doesn't make a card a colour).
///
/// - <b>Opening-hand reveal rider</b> — implemented as the
///   <see cref="KeywordAbility"/>
///   <see cref="OpeningHandRevealLook4Trigger.RevealKeyword"/> marker.
///   The shared <see cref="OpeningHandRevealLook4Trigger"/> subscriber
///   (wired by <see cref="GameDriver"/> at game start) prompts via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/> on the
///   <see cref="OpeningHandCheckEvent"/>; on yes it registers a
///   <see cref="DelayedTriggeredAbility"/> with the supplied
///   <see cref="TriggerManager"/> that fires once on the revealer's first
///   <see cref="Majik.Core.StateMachine.PhaseStateType.Upkeep"/>. The
///   delayed trigger peeks top 4, prompts which (if any) to keep on top
///   via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>, and exiles
///   the rest (CR 701.21).
///
/// - <b>Cast trigger</b> (CR 603.6a / CR 603.10): "When you cast this
///   spell, exile target permanent that's one or more colors." Triggered
///   ability over <see cref="SpellCastEvent"/> filtered to
///   <c>e.Spell.Card == card</c> (same self-cast shape as
///   <see cref="UlamogTheCeaselessHungerFactory"/>); active in
///   <see cref="ZoneType.Stack"/> because the spell is on the stack at
///   cast time. One 1..1 "target permanent that's one or more colors"
///   <see cref="TargetRequest"/> whose
///   <see cref="TargetRequest.CandidateGatherer"/> scans every
///   battlefield permanent and includes only those whose
///   <see cref="CardColors.GetColors"/> set is non-empty (CR 700.2a —
///   "colored" means at least one of W/U/B/R/G). On resolution the chosen
///   permanent is moved Battlefield → Exile via the supplied
///   <see cref="ZoneService"/> (or raw zone manipulation when no
///   ZoneService is supplied, matching Ulamog's two-overload pattern).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Abilities attached;
///   triggers not registered; exile uses raw zone manipulation.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?)"/> — fully
///   wired. Cast trigger registers with <paramref name="triggers"/>;
///   exile routes through <paramref name="zones"/>.
///
/// The opening-hand reveal lives outside the factory because it shares
/// a subscriber surface across cards (today: Devourer only; tomorrow:
/// other "reveal from opening hand → first-upkeep ritual" carriers).
/// </summary>
[CardName("Devourer of Destiny")]
public static class DevourerOfDestinyFactory
{
    public const string CardName = "Devourer of Destiny";
    public const string PrintedManaCost = "{5}{C}{C}";
    public const int Power = 6;
    public const int Toughness = 6;

    /// <summary>Construct Devourer with no live wiring. All abilities are
    /// attached for shape observability; the cast trigger isn't registered
    /// with any <see cref="TriggerManager"/>; cast-trigger exile uses raw
    /// zone manipulation. Suitable for dispatcher / structural tests.</summary>
    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null);

    /// <summary>Construct Devourer with optional runtime services.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">When supplied, the cast-trigger exile routes
    /// through <see cref="ZoneService.MoveCard"/> so
    /// <see cref="CardMovedEvent"/> publishes for any zone-change
    /// subscribers (Containment Priest, Tormod's Crypt, etc.).</param>
    /// <param name="triggers">When supplied, the cast trigger registers
    /// with the manager so SpellCastEvent lands it on the stack
    /// automatically (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Opening-hand reveal rider — marker keyword only.
        // The shared OpeningHandRevealLook4Trigger subscriber (attached by
        // GameDriver at game start) walks each opening hand for cards
        // tagged with this keyword and schedules the first-upkeep ritual.
        // Adding more reveal-from-opening-hand carriers is therefore "tag
        // the factory with KeywordAbility(RevealKeyword)" with no further
        // per-card wiring required.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(
            OpeningHandRevealLook4Trigger.RevealKeyword, card, owner));

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.6a / CR 603.10.
        //   "When you cast this spell, exile target permanent that's one
        //    or more colors."
        // Fires while Devourer is on the stack (SpellCastEvent is
        // published as the spell moves to the stack), so activeZones =
        // Stack — same posture as UlamogTheCeaselessHungerFactory's cast
        // trigger.
        // ----------------------------------------------------------------
        TriggeredAbility? castTrigger = null;
        var castCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var castEffect = new Effect(
            $"{CardName}: exile target colored permanent (cast trigger)",
            () =>
            {
                if (castTrigger == null) return;
                var chosen = castTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Card permCard) return;
                // CR 608.2b — illegal-on-resolution check: target must
                // still be on the battlefield AND still colored. A
                // permanent that lost all its colors between cast and
                // resolution is no longer a legal target.
                if (permCard.Zone != ZoneType.Battlefield) return;
                if (CardColors.GetColors(permCard).Count == 0) return;

                // CR 701.21 — exile is NOT a destroy effect; indestructible
                // permanents are exiled normally. Route through ZoneService
                // when supplied so CardMovedEvent fires.
                if (zones != null)
                {
                    zones.MoveCard(permCard, ZoneType.Battlefield, ZoneType.Exile);
                }
                else
                {
                    var permController = permCard.Controller ?? permCard.Owner;
                    permController?.Zones.Battlefield.RemoveCard(permCard);
                    var exileOwner = permCard.Owner ?? owner;
                    exileOwner.Zones.Exile.AddCard(permCard);
                    permCard.SetZone(ZoneType.Exile);
                }
            });

        castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            interveningIf: null,
            // Cast trigger fires while Devourer is on the stack — same
            // active-zone posture as Cascade (CrashingFootfalls / Living
            // End) and Ulamog's cast trigger.
            activeZones: new[] { ZoneType.Stack },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent that's one or more colors",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 700.2a — "colored" means the permanent has at
                    // least one of W/U/B/R/G. Scan every battlefield
                    // permanent across all players and include only those
                    // with a non-empty color set; colorless permanents
                    // (Eldrazi, most artifacts) are deliberately excluded.
                    CandidateGatherer: ctx =>
                    {
                        var pool = new List<object>();
                        foreach (var p in ctx.AllPlayers)
                        {
                            foreach (var c in p.Zones.Battlefield.GetCards())
                            {
                                if (CardColors.GetColors(c).Count > 0)
                                {
                                    pool.Add(c);
                                }
                            }
                        }
                        return pool;
                    }),
            });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }
}
