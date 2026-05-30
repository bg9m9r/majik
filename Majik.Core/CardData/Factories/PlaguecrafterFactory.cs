using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Plaguecrafter (Guilds of Ravnica, {2}{B}).
/// Creature — Human Shaman 3/2.
///
/// ## Card text (Scryfall verified)
/// "When this creature enters, each player sacrifices a creature or
///  planeswalker of their choice. Each player who can't discards a card."
///
/// ## Base shape
/// Name / Creature / Human Shaman / {2}{B} / 3/2 are materialised from the
/// embedded JSON definition (<c>plaguecrafter.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="KroxaTitanFactory"/>. The ETB behaviour is layered on here
/// because the JSON ability schema doesn't yet express the edict-with-discard
/// rider.
///
/// ## Implemented (v1)
/// - <b>ETB triggered ability (CR 603.1)</b>: wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/> — same trigger shape as
///   <see cref="RavenousChupacabraFactory"/>.
/// - <b>"Each player sacrifices a creature or planeswalker of their choice"</b>
///   (CR 701.16). Unlike <see cref="DiabolicEdictFactory"/> /
///   <see cref="SheoldredsEdictFactory"/> (which iterate "each opponent"),
///   Plaguecrafter affects EACH player — the controller included
///   (CR 109.5 / 800.4). The affected player picks the permanent "of their
///   choice": their agent drives the pick (intent
///   <see cref="BotIntent.Removal"/>), with a deterministic first-eligible
///   fallback (mirrors <see cref="SheoldredsEdictFactory"/>). Eligible
///   permanents = creatures OR planeswalkers that player controls. Sacrifice
///   bypasses Indestructible / regeneration (<see cref="Fx.Sacrifice"/>).
/// - <b>"Each player who can't discards a card"</b> (CR 701.8). A player who
///   controls neither a creature nor a planeswalker "can't" sacrifice — they
///   discard a card instead. The discarding player chooses (agent-driven,
///   deterministic first-card fallback). An empty-handed player who also
///   can't sacrifice does nothing (CR 701.8c — discard nothing if no cards).
///
/// ## Sequencing (CR 608.2 / APNAP 101.4)
/// Each player resolves their own sacrifice-or-discard as the iteration
/// reaches them. The "can't" check is evaluated per player against the board
/// state at the moment that player acts, which matches a single-instruction
/// resolution where each player makes the choice in APNAP order; no observable
/// difference arises because the sacrifices are independent across players
/// (no shared targets). The <paramref name="playerResolver"/> is expected to
/// yield players in APNAP order (CR 101.4) when supplied by the runtime.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice / discard prompt UI</b>: the affected player's agent
///   receives the full eligible list / hand. Surfacing the choice to the
///   portal decision panel is deferred — same queue as
///   <see cref="DiabolicEdictFactory"/> / <see cref="KroxaTitanFactory"/>.
/// </summary>
[CardName("Plaguecrafter")]
public static class PlaguecrafterFactory
{
    public const string CardName = "Plaguecrafter";
    public const string Slug = "plaguecrafter";

    /// <summary>
    /// Construct Plaguecrafter with no live wiring. The ETB trigger is
    /// attached for shape inspection (not registered with a
    /// <see cref="TriggerManager"/>); its body no-ops cleanly without a
    /// player resolver. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, playerResolver: null, triggers: null, agent: null);

    /// <summary>
    /// Construct Plaguecrafter with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="playerResolver">Returns the live player list at
    /// resolution time, ideally in APNAP order (CR 101.4). "Each player"
    /// iterates whatever this yields — the controller included. Null → the
    /// ETB body no-ops (shape path).</param>
    /// <param name="triggers">TriggerManager — when supplied the ETB trigger
    /// is registered so the enter-battlefield event lands it on the stack
    /// automatically.</param>
    /// <param name="agent">Optional agent driving each affected player's
    /// "of their choice" sacrifice pick and discard pick. Null falls back to
    /// a deterministic first-eligible / first-card pick.</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? playerResolver,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Human Shaman, {2}{B}, 3/2). No abilities in the JSON — the ETB
        // behaviour is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When this creature enters, each player sacrifices a creature or
        //    planeswalker of their choice. Each player who can't discards a
        //    card."
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: each player sacrifices a creature or planeswalker of their choice; each who can't discards a card",
            () => Resolve(playerResolver, agent));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }

    // -----------------------------------------------------------------------
    // Resolution body — CR 701.16 (sacrifice) + CR 701.8 (discard rider).
    // For each player: sacrifice a creature/planeswalker of their choice; if
    // they control none ("can't"), discard a card instead.
    // -----------------------------------------------------------------------
    private static void Resolve(
        Func<IReadOnlyList<Player>>? playerResolver,
        IPlayerAgent? agent)
    {
        var players = playerResolver?.Invoke();
        if (players == null) return; // shape path — no players wired.

        foreach (var pl in players)
        {
            if (pl == null) continue;

            // Eligible permanents = creatures OR planeswalkers this player
            // controls on the battlefield (CR 701.16 — "of their choice").
            var eligible = pl.Zones.Battlefield.GetCards()
                .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
                .ToList();

            if (eligible.Count > 0)
            {
                SacrificeOfTheirChoice(pl, eligible, agent);
            }
            else
            {
                // CR 701.8 — "Each player who can't discards a card."
                DiscardOfTheirChoice(pl, agent);
            }
        }
    }

    /// <summary>
    /// CR 701.16 — <paramref name="player"/> sacrifices a creature or
    /// planeswalker of their choice from <paramref name="eligible"/>. The
    /// affected player's agent drives the pick; an invalid / absent pick
    /// falls back deterministically to the first eligible permanent in
    /// battlefield order.
    /// </summary>
    private static void SacrificeOfTheirChoice(
        Player player,
        IReadOnlyList<ICard> eligible,
        IPlayerAgent? agent)
    {
        ICard pick;
        if (agent != null)
        {
            var chosen = agent
                .ChooseFromBattlefieldAsync(player, eligible, BotIntent.Removal)
                .GetAwaiter().GetResult();

            // Validate: must still be on this player's battlefield and be a
            // creature/planeswalker. Invalid → deterministic fallback.
            pick = (chosen != null
                    && chosen.Zone == ZoneType.Battlefield
                    && ReferenceEquals(chosen.Controller, player)
                    && (chosen.HasType(CardType.Creature)
                        || chosen.HasType(CardType.Planeswalker)))
                ? chosen
                : eligible[0];
        }
        else
        {
            pick = eligible[0];
        }

        // CR 701.16 — sacrifice bypasses Indestructible / regeneration.
        Fx.Sacrifice(pick);
    }

    /// <summary>
    /// CR 701.8 — <paramref name="player"/> discards a card of their choice.
    /// An empty hand → no discard (CR 701.8c). The discarding player chooses
    /// (agent-driven, deterministic first-card fallback).
    /// </summary>
    private static void DiscardOfTheirChoice(Player player, IPlayerAgent? agent)
    {
        var hand = player.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return; // can't sacrifice AND can't discard.

        ICard pick;
        if (agent != null)
        {
            var chosen = agent
                .ChooseFromHandAsync(player, hand.Cast<ICard>().ToList(), BotIntent.Discard)
                .GetAwaiter().GetResult();
            pick = (chosen != null && chosen.Zone == ZoneType.Hand) ? chosen : hand[0];
        }
        else
        {
            pick = hand[0];
        }

        player.Zones.Hand.RemoveCard(pick);
        player.Zones.Graveyard.AddCard(pick);
        pick.SetZone(ZoneType.Graveyard);
    }
}
