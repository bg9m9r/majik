using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Definitions;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kozilek's Return (Oath of the Gatewatch, {2}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Devoid (This card has no color.)
///    Kozilek's Return deals 2 damage to each creature.
///    Whenever you cast an Eldrazi creature spell with mana value 7 or
///    greater, you may exile this card from your graveyard. If you do, this
///    card deals 5 damage to each creature."
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>kozileks-return.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. Two behavioural halves layered on
/// top:
///
/// 1. <b>Devoid (CR 702.114)</b> — stamped on the card via
///    <see cref="Card.SetDevoid"/> so <see cref="CardColors.GetColors"/>
///    returns the empty set despite the {R} pip, plus a
///    <see cref="KeywordAbility"/> marker for ability-scan observability
///    (mirrors <see cref="WrithingChrysalisFactory"/> /
///    <see cref="SowingMycospawnFactory"/>).
///
/// 2. <b>Printed sweep — "deals 2 damage to each creature" (CR 109.5)</b> —
///    the spell's resolve effect, built on demand via
///    <see cref="BuildResolveEffect"/> (same shape as
///    <see cref="AngerOfTheGodsFactory.BuildResolveEffect"/>: scan every
///    supplied player's battlefield, 2 damage to each creature). No exile
///    rider — Kozilek's Return's first ability is a plain sweep.
///
/// 3. <b>Graveyard recursion trigger — "Whenever you cast an Eldrazi
///    creature spell with mana value 7 or greater, you may exile this card
///    from your graveyard. If you do, this card deals 5 damage to each
///    creature." (CR 603.6d / CR 603.10)</b> — a triggered ability over
///    <see cref="SpellCastEvent"/>, active while Kozilek's Return is in its
///    owner's graveyard (<c>activeZones = {Graveyard}</c>, mirroring
///    <see cref="BridgeFromBelowFactory"/>). The condition gates on:
///      - the cast spell being controlled by Kozilek's Return's controller
///        ("you cast"),
///      - the spell's card being a <see cref="CardType.Creature"/> with the
///        <see cref="CardSubtype.Eldrazi"/> subtype, and
///      - the card's mana value (CR 202.3) being &gt;= 7.
///    On resolution the effect offers the controller the optional "may
///    exile this card from your graveyard" choice (CR 117.x —
///    <see cref="IPlayerAgent.ChooseYesNoAsync"/>, tagged
///    <see cref="BotIntent.Wrath"/>; the pre-agent default auto-accepts the
///    board-wipe upside). On a yes the card is moved Graveyard → Exile (raw
///    zone mutation, same posture as Bridge's self-exile) and then deals 5
///    damage to each creature (CR 608.2 — "If you do" is gated on the exile
///    succeeding).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Devoid + the recursion
///   triggered ability are attached to <see cref="Card.Abilities"/> for
///   structural tests; the trigger is not registered with any
///   <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the
///   recursion trigger so the bus drives it on <see cref="SpellCastEvent"/>.
///
/// ## v1 simplifications
/// - The recursion exile + sweep operate over the players supplied to the
///   trigger via the controller's known opponents at resolution time. The
///   effect scans every player the controller can reach through
///   <see cref="Player"/> battlefield zones it is handed; mirrors the
///   AngerOfTheGods sweep posture. Exposed seam
///   <see cref="BuildGraveyardRecursionEffect"/> takes the player list and
///   the source card explicitly so it is unit-testable without a live game.
/// </summary>
[CardName("Kozilek's Return")]
public static class KozileksReturnFactory
{
    public const string CardName = "Kozilek's Return";
    public const string Slug = "kozileks-return";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>CR 109.5 — the printed first-ability sweep amount.</summary>
    public const int SweepDamage = 2;

    /// <summary>CR 608.2 — the graveyard-recursion sweep amount.</summary>
    public const int RecursionDamage = 5;

    /// <summary>The mana-value threshold the cast trigger gates on
    /// (CR 202.3 — "mana value 7 or greater").</summary>
    public const int EldraziManaValueThreshold = 7;

    /// <summary>CR 702.114 — Devoid keyword marker string for the
    /// <see cref="KeywordAbility"/> the factory attaches.</summary>
    public const string DevoidKeyword = "Devoid";

    /// <summary>Build Kozilek's Return shape-only (no trigger registration).</summary>
    public static Instant Create(Player owner) => Create(owner, triggers: null, playerResolver: null);

    /// <summary>
    /// Build Kozilek's Return from the embedded JSON, stamp Devoid, and
    /// attach the graveyard-recursion triggered ability.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the recursion trigger registers
    /// so <see cref="SpellCastEvent"/> drives it automatically (CR 603.2).</param>
    /// <param name="playerResolver">Supplies the full player list the
    /// recursion sweep should reach (every player whose battlefield is
    /// scanned). The engine has no global player registry, so the caller
    /// injects this the same way <see cref="FalkenrathNobleFactory"/>
    /// injects its opponent resolver. When null the sweep falls back to the
    /// controller alone (single-seat shape posture).</param>
    public static Instant Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Player>>? playerResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 702.114 — Devoid. Stamp IsDevoid so CardColors.GetColors
        // returns empty regardless of the {R} pip; also attach the
        // KeywordAbility marker for ability-scan discoverability.
        card.SetDevoid(true);
        card.AddAbility(new KeywordAbility(DevoidKeyword, card, owner));

        // ----------------------------------------------------------------
        // Graveyard recursion trigger — "Whenever you cast an Eldrazi
        // creature spell with mana value 7 or greater, you may exile this
        // card from your graveyard. If you do, this card deals 5 damage to
        // each creature." CR 603.6d (active in graveyard) + CR 603.10.
        // ----------------------------------------------------------------
        var recursionCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => QualifiesForRecursion(e.Spell, owner));

        var recursionEffect = new Effect(
            $"{CardName}: may exile from graveyard to deal {RecursionDamage} to each creature",
            () =>
            {
                var players = playerResolver?.Invoke() ?? new[] { owner };
                foreach (var ef in BuildGraveyardRecursionEffect(card, owner, players))
                {
                    ef.Execute();
                }
            });

        var recursionTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: recursionCondition,
            effects: new IEffect[] { recursionEffect },
            // CR 603.6d — active while Kozilek's Return sits in the graveyard.
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(recursionTrigger);
        triggers?.RegisterTriggeredAbility(recursionTrigger);

        return card;
    }

    /// <summary>
    /// CR 603.6d condition — the cast spell is an Eldrazi creature spell
    /// with mana value &gt;= 7 controlled by <paramref name="controller"/>
    /// ("you cast"). Reads the cast card's printed types/subtypes + mana
    /// value (CR 202.3).
    /// </summary>
    public static bool QualifiesForRecursion(Majik.Core.Spells.ISpell spell, Player controller)
    {
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(controller);

        var castCard = spell.Card;
        if (castCard == null) return false;

        // "you cast" — the spell's controller must be Kozilek's Return's
        // controller.
        if (!ReferenceEquals(castCard.Controller, controller)) return false;

        // "an Eldrazi creature spell" — creature card with the Eldrazi
        // subtype (CR 205.3m).
        if (!castCard.HasType(CardType.Creature)) return false;
        if (!castCard.HasSubtype(CardSubtype.Eldrazi)) return false;

        // "with mana value 7 or greater" — CR 202.3.
        if (castCard is not Card c) return false;
        return c.ManaCostValue.TotalValue >= EldraziManaValueThreshold;
    }

    /// <summary>
    /// Build the graveyard-recursion resolution effect: offer the optional
    /// "may exile this card from your graveyard" choice (CR 117.x); on yes,
    /// move the card Graveyard → Exile and deal <see cref="RecursionDamage"/>
    /// (5) to every creature on every supplied player's battlefield
    /// (CR 109.5 / CR 608.2 — the sweep is gated on the exile succeeding).
    /// </summary>
    /// <param name="card">Kozilek's Return itself (the card to exile + the
    /// damage source).</param>
    /// <param name="controller">The trigger's controller — receives the
    /// "may" prompt and owns the graveyard the card is exiled from.</param>
    /// <param name="allPlayers">Every player whose battlefield the sweep
    /// scans.</param>
    public static IReadOnlyList<IEffect> BuildGraveyardRecursionEffect(
        Instant card,
        Player controller,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: optional exile-from-graveyard -> {RecursionDamage} to each creature",
                () =>
                {
                    // CR 608.2b — the card must still be in the graveyard for
                    // the optional exile to be possible.
                    if (card.Zone != ZoneType.Graveyard) return;
                    if (!controller.Zones.Graveyard.GetCards().Contains(card)) return;

                    // CR 117.x — "you may exile this card from your
                    // graveyard." Consult the controller's agent when wired;
                    // pre-agent default auto-accepts the board-wipe upside
                    // (BotIntent.Wrath falls through to the neutral
                    // auto-accept posture).
                    bool yes = true;
                    var agent = AgentRegistry.Get(controller);
                    if (agent != null)
                    {
                        yes = agent.ChooseYesNoAsync(
                            $"Exile {CardName} from your graveyard to deal {RecursionDamage} damage to each creature?",
                            BotIntent.Wrath).GetAwaiter().GetResult();
                    }
                    if (!yes) return;

                    // CR 701.21 — exile is a zone change (Graveyard → Exile).
                    // Raw zone mutation, same posture as Bridge from Below's
                    // self-exile.
                    controller.Zones.Graveyard.RemoveCard(card);
                    var exileOwner = card.Owner ?? controller;
                    exileOwner.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);

                    // CR 608.2 — "If you do, this card deals 5 damage to each
                    // creature." Gated on the exile succeeding above
                    // (CR 109.5 sweep over every supplied battlefield).
                    foreach (var pl in allPlayers)
                    {
                        foreach (var creature in pl.Zones.Battlefield.GetCards()
                                     .OfType<Creature>().ToList())
                        {
                            creature.TakeDamage(RecursionDamage);
                        }
                    }
                }),
        };
    }

    /// <summary>
    /// Build the printed first-ability resolve effect: deal
    /// <see cref="SweepDamage"/> (2) damage to every creature on every
    /// supplied player's battlefield (CR 109.5). Mirrors
    /// <see cref="AngerOfTheGodsFactory.BuildResolveEffect"/> minus the
    /// exile rider — Kozilek's Return's first ability is a plain sweep.
    /// </summary>
    /// <param name="allPlayers">Every player whose battlefield the sweep
    /// scans.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: deal {SweepDamage} damage to each creature",
                () =>
                {
                    foreach (var pl in allPlayers)
                    {
                        foreach (var creature in pl.Zones.Battlefield.GetCards()
                                     .OfType<Creature>().ToList())
                        {
                            creature.TakeDamage(SweepDamage);
                        }
                    }
                }),
        };
    }
}
