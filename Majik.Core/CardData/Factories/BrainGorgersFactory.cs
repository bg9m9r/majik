using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Brain Gorgers (Future Sight, {3}{B}).
/// Creature — Zombie 4/2. Oracle text (verified against Scryfall):
///   "When you cast this spell, any player may sacrifice a creature of their
///    choice. If a player does, counter Brain Gorgers.
///    Madness {1}{B} (If you discard this card, discard it into exile. When you
///    do, cast it for its madness cost or put it into your graveyard.)"
///
/// ## Madness is intrinsic — NOT wired here
/// Madness (CR 702.35) is handled centrally for every catalogued card by
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> (name → cost) consulted by
/// the discard funnel <see cref="Fx.DiscardCard"/>. "Brain Gorgers" is in the
/// catalog at {1}{B}, so the printed "Madness {1}{B}" line needs no factory
/// code. This factory implements ONLY the card's other body: the cast-trigger
/// self-counter.
///
/// ## Implemented (v1)
/// - Base shape (name, Creature, Zombie, {3}{B}, 4/2) from the embedded JSON
///   definition (<c>brain-gorgers.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>Cast trigger (CR 603.2 / 603.3a — "When you cast this spell")</b>: a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> gated to
///   this card, functioning on the Stack (CR 603.6e — a trigger that refers to
///   the spell being cast resolves before the spell does, while the spell is
///   still on the stack). On resolution:
///   1. <b>"any player may sacrifice a creature of their choice"</b>: each
///      player (the controller first, then opponents — a simple APNAP-ish
///      order, CR 603.3b) with at least one creature is given an explicit "may"
///      gate via <see cref="IPlayerAgent.ChooseYesNoAsync(string,BotIntent,System.Threading.CancellationToken)"/>
///      classified <see cref="BotIntent.CostToDecline"/> (the default heuristic
///      agent declines — sacrificing your own creature is a cost). On accept the
///      player picks the creature THEY control to sacrifice via
///      <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>; it is sacrificed
///      via <see cref="Fx.Sacrifice(ICard)"/> (CR 701.16). The may gate is kept
///      SEPARATE from the pick because <c>ChooseFromBattlefieldAsync</c> is not
///      optional on the production agent (it always returns a candidate), so
///      folding "whether" into the pick would force every player with a creature
///      to always sacrifice.
///   2. <b>"If a player does, counter Brain Gorgers"</b>: if AT LEAST ONE
///      player sacrificed, the Brain Gorgers spell is countered (CR 701.5a —
///      removed from the stack and put into its owner's graveyard) via
///      <see cref="Fx.Counter"/>. If nobody sacrificed, the spell is untouched
///      and resolves normally into a 4/2 Zombie.
///
/// ## Why a named factory (not a spell template / JSON ability)
/// The trigger is a creature-spell cast trigger that both (a) prompts every
/// player for an optional sacrifice and (b) conditionally counters the very
/// spell that is being cast. Neither the JSON <c>AbilityDefinition</c> schema
/// nor the regex spell-template binders express a multi-player optional
/// sacrifice gating a self-counter, so the behaviour lives here (same posture
/// as <see cref="StormscaleScionFactory"/>, whose Storm cast-trigger also
/// outgrows the schema).
///
/// ## Deferred (v1 gaps)
/// - <b>Live-game wiring of the counter</b>: the resolve effect reads the live
///   <see cref="GameContext.Stack"/> and <see cref="GameContext.AllPlayers"/>
///   off the resolution context, and resolves each player's agent through
///   <see cref="AgentRegistry"/>. When the trigger resolves without a live
///   <see cref="ResolutionContext.Game"/> (some isolated test harnesses) it is
///   a structural no-op (no players to prompt, no stack to counter on).
/// </summary>
[CardName("Brain Gorgers")]
public static class BrainGorgersFactory
{
    public const string CardName = "Brain Gorgers";
    public const string Slug = "brain-gorgers";
    public const string PrintedManaCost = "{3}{B}";
    public const int Power = 4;
    public const int Toughness = 2;

    /// <summary>
    /// Build Brain Gorgers from the embedded JSON definition and attach the
    /// cast-trigger self-counter. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Zombie subtype, {3}{B}, 4/2). The JSON carries no abilities — the
        // cast trigger is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        card.AddAbility(BuildCastTrigger(card, owner));
        return card;
    }

    /// <summary>
    /// Build the "When you cast this spell" triggered ability. The condition
    /// gates to this card's <see cref="SpellCastEvent"/> (CR 603.3a). The
    /// resolve effect prompts every player for an optional creature sacrifice
    /// and, if any player sacrifices, counters this spell (CR 701.5a).
    /// </summary>
    private static TriggeredAbility BuildCastTrigger(Creature card, Player controller)
    {
        // Capture the spell as it is cast so the resolve effect can counter
        // the exact stack object. Re-set on every match (a card can be cast
        // more than once across a game — e.g. recast from the graveyard).
        Majik.Core.Spells.ISpell? castSpell = null;

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            // CR 603.3a — "When you cast this spell" only fires for THIS card's
            // own cast.
            if (!ReferenceEquals(e.Spell.Card, card)) return false;
            castSpell = e.Spell;
            return true;
        });

        var effect = new Effect(
            "Brain Gorgers — any player may sacrifice a creature; if a player does, counter Brain Gorgers (CR 701.5a).",
            async rc =>
            {
                var game = rc.Game;
                if (game == null) return; // no live game → structural no-op.

                // CR 603.3b — order the optional sacrifice prompts controller-
                // first, then the other players (a simple APNAP-ish order;
                // only whether ANY player sacrifices matters for the counter).
                var players = new List<Player> { controller };
                foreach (var p in game.AllPlayers)
                {
                    if (!ReferenceEquals(p, controller)) players.Add(p);
                }

                var anyoneSacrificed = false;

                foreach (var player in players)
                {
                    if (player.HasLost) continue;

                    // "a creature of their choice" — only creatures the
                    // prompted player controls on the battlefield are legal.
                    var creatures = player.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .Where(c => c.Zone == ZoneType.Battlefield)
                        .Cast<ICard>()
                        .ToList();

                    if (creatures.Count == 0) continue;

                    var agent = AgentRegistry.Get(player);
                    if (agent == null) continue; // no agent → declines the "may".

                    // "any player MAY sacrifice …" — an explicit may gate
                    // (CR 117.x). Classified BotIntent.CostToDecline: sacrificing
                    // one of your own creatures to counter the spell is a cost,
                    // so the default heuristic agent declines (Brain Gorgers
                    // resolves; the controller keeps the 4/2 — the printed-
                    // favourable, opponent-neutral default). The may gate is
                    // SEPARATE from the creature pick because
                    // ChooseFromBattlefieldAsync is NOT optional (its production
                    // shim always returns the first candidate when one exists),
                    // so folding "whether" into the pick would force every player
                    // with a creature to always sacrifice.
                    var accepts = await agent
                        .ChooseYesNoAsync(
                            $"Sacrifice a creature to counter {CardName}?",
                            BotIntent.CostToDecline,
                            rc.Ct)
                        .ConfigureAwait(false);
                    if (!accepts) continue;

                    // "… a creature of their choice."
                    var pick = await agent.ChooseFromBattlefieldAsync(
                        player, creatures, BotIntent.None, rc.Ct).ConfigureAwait(false);

                    // Validate the pick is a creature the chooser still controls;
                    // otherwise fall back deterministically (CR 701.16).
                    if (pick is not Creature
                        || pick.Zone != ZoneType.Battlefield
                        || !ReferenceEquals(pick.Controller, player))
                    {
                        pick = creatures[0];
                    }

                    // CR 701.16 — sacrifice. The sacrificing player is the
                    // permanent's controller (the prompted player).
                    Fx.Sacrifice(pick);
                    anyoneSacrificed = true;
                }

                // CR 701.5a — "If a player does, counter Brain Gorgers." A
                // single sacrifice suffices to counter.
                if (anyoneSacrificed && castSpell != null)
                {
                    Fx.Counter(game.Stack, castSpell);
                }
            });

        return new TriggeredAbility(
            source: card,
            controller: controller,
            condition: condition,
            effects: new IEffect[] { effect },
            // CR 603.6e — a "When you cast this spell" trigger functions on the
            // stack while the spell itself is on the stack.
            activeZones: new[] { ZoneType.Stack });
    }
}
