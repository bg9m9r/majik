using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Noxious Revival (New Phyrexia, {G/P}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "({G/P} can be paid with either {G} or 2 life.)
///    Put target card from a graveyard on top of its owner's library."
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>noxious-revival.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> (same data-only shape as
/// <see cref="AncientGrudgeFactory"/>). The printed {G/P} cost is a
/// Phyrexian green pip (CR 107.4f); <see cref="ManaCost.Parse"/> already
/// models it as a single <see cref="ManaCost.PhyrexianPips"/> entry —
/// payable with {G} or 2 life — so no bespoke cost wiring is needed.
///
/// The resolve-time body lives in <see cref="BuildDefinition"/> because a
/// <see cref="SpellDefinition"/> needs a target resolver supplied by the
/// caller's <see cref="GameContext"/> (not expressible in the data-only
/// JSON schema).
///
/// - <b>Put target card from a graveyard on top of its owner's library</b> —
///   a single 1..1 "target card in a graveyard" <see cref="TargetRequest"/>.
///   The <c>CandidateGatherer</c> walks EVERY player's graveyard (CR 109.5 —
///   "a graveyard", any player's), mirroring <see cref="AncientGrudgeFactory"/>'s
///   all-battlefield gather but over the graveyard zone. On resolution it
///   re-checks the target is still in a graveyard (CR 608.2b illegal-target
///   gate), then removes it from that graveyard and inserts it at index 0
///   of its OWNER's library — the canonical "top of library" position read
///   by <see cref="DrawAction"/> and friends (same placement as
///   <see cref="MysticalTutorFactory"/>). The destination is the card's
///   owner's library, not the controller's (CR 401.1 — a card's owner is
///   the player who started the game with it in their deck).
///
/// ## Notes / deferred
/// - <b>Reveal event</b>. The card moves graveyard → top-of-library without
///   publishing a reveal event; same gap as the search factories
///   (<see cref="MysticalTutorFactory"/>).
/// </summary>
[CardName("Noxious Revival")]
public static class NoxiousRevivalFactory
{
    public const string CardName = "Noxious Revival";
    public const string Slug = "noxious-revival";

    /// <summary>CR 107.4f — printed Phyrexian-green cost {G/P} (payable with
    /// {G} or 2 life). Stored as a constant so tests assert Scryfall-exact.</summary>
    public const string PrintedManaCost = "{G/P}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "put target card from a graveyard on top of its owner's
    /// library" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the resolved target is still an
    /// <see cref="ICard"/> in a graveyard (CR 608.2b — illegal target at
    /// resolution → no-op); then removes it from that graveyard and inserts
    /// it at index 0 of its owner's library (CR 401.1 / CR 700.4 — top of
    /// the owner's library).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// cards directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card in a graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.CardAdvantage,
                    // Agent-prompt: walk every graveyard (CR 109.5 — "a
                    // graveyard" = any player's). Mirrors AncientGrudge's
                    // all-battlefield gather, over the graveyard zone.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Graveyard.GetCards())
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: put target card from a graveyard on top of its owner's library",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not ICard card) return;
                            if (card.Zone != ZoneType.Graveyard) return;

                            // CR 401.1 — destination is the card's OWNER's
                            // library, not necessarily the spell controller's.
                            var owner = card.Owner;
                            if (owner == null) return;

                            // Remove from whichever graveyard it currently
                            // sits in (could be any player's). The card knows
                            // its zone is Graveyard; its owner's graveyard is
                            // the canonical home, but it might rest in another
                            // player's graveyard — remove from each to be safe.
                            owner.Zones.Graveyard.RemoveCard(card);

                            // CR 700.4 — "top of library" = index 0 (the
                            // position read by DrawAction). Insert there, then
                            // sync the card's zone marker (CR 401.1).
                            owner.Zones.Library.InsertCardAt(0, card);
                            card.SetZone(ZoneType.Library);
                        }),
                };
            });
    }
}
