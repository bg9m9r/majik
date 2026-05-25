using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mastermind's Acquisition (Rivals of Ixalan,
/// {3}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Choose one —
///     • Search your library for a card, put that card into your hand,
///       then shuffle.
///     • Choose a card you own from outside the game, reveal it, and put
///       it into your hand."
///
/// CR 700.2d — modal "Choose one —" spell with 2 modes (PickCount = 1).
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {3}{B}{B}.
/// - <b>Mode 0 — library tutor (CR 701.19a)</b>: walks
///   <c>caster.Zones.Library.GetCards()</c>, prompts the controller's
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> with the full set
///   (kindLabel "card"), moves the pick Library → Hand, then shuffles
///   via <see cref="Majik.Core.Zones.LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a). Deterministic first-pick fallback when no agent is
///   registered (same posture as
///   <see cref="MysticalTutorFactory"/> / <see cref="SearchSpellFactory"/>).
/// - <b>Mode 2 — wishboard tutor (CR 408)</b>: delegates to the new
///   <see cref="WishTutorEffect"/> primitive with
///   <see cref="WishTutorEffect.Predicates.AnyCard"/>. The wishboard
///   pile is <see cref="Player.Wishboard"/> (= <see cref="Player.Sideboard"/>);
///   deck-builders mark the wish-pool by adding cards to the
///   controller's sideboard zone. No eligible card → no-op (CR 117.x).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>. Both modes move the picked card directly to
///   hand without publishing a <c>CardRevealedEvent</c>; same gap as
///   every other tutor factory (Stoneforge / Mystical Tutor / Goblin
///   Matron).
/// - <b>"Any owner" sideboard ownership check</b>. The mode 2 predicate
///   doesn't re-validate ownership because <see cref="Player.Wishboard"/>
///   only enumerates the caster's own sideboard zone. Multi-headed /
///   shared-sideboard formats would need an explicit "you own" clause.
/// </summary>
[CardName("Mastermind's Acquisition")]
public static class MastermindsAcquisitionFactory
{
    public const string CardName = "Mastermind's Acquisition";
    public const string PrintedManaCost = "{3}{B}{B}";

    /// <summary>Mode 0 — library tutor.</summary>
    public const int ModeLibrary = 0;
    /// <summary>Mode 1 — wishboard tutor.</summary>
    public const int ModeWishboard = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Search your library for a card, put that card into your hand, then shuffle.",
        "Choose a card you own from outside the game, reveal it, and put it into your hand.",
    };

    /// <summary>Construct Mastermind's Acquisition as a Sorcery owned by
    /// <paramref name="owner"/>. Card shape only — the resolve body is
    /// produced by <see cref="BuildDefinition"/>.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Mastermind's Acquisition.
    /// Both modes are wired. No target requests — both modes resolve via
    /// internal pickers (library agent prompt + wishboard agent prompt).
    /// </summary>
    public static SpellDefinition BuildDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            // CR 601.2c — no per-mode target requests; both modes resolve
            // their picker against zones the caster owns (library /
            // wishboard) at resolve-time without a cast-time target list.
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Tutor, // library tutor — strict upside
                BotIntent.Tutor, // wishboard tutor — also a tutor for scoring
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (first entry wins for
                // a Choose-one card) or the legacy scalar ModeIndex.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;       // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break;  // CR 700.2d — pick count cap

                    switch (raw)
                    {
                        case ModeLibrary:
                            effectsOut.Add(BuildLibraryTutorEffect(caster));
                            break;
                        case ModeWishboard:
                            effectsOut.Add(BuildWishboardEffect(caster));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    private static IEffect BuildLibraryTutorEffect(Player caster) =>
        new Effect("Mastermind's Acquisition — tutor any card from library", () =>
        {
            // CR 701.19a — library search consults the agent. "A card"
            // accepts the entire library; predicate is the trivial
            // accept-all so the agent sees every option.
            var candidates = caster.Zones.Library.GetCards().ToList();
            if (candidates.Count == 0) return;

            var agent = AgentRegistry.Get(caster);
            ICard? pick = agent != null
                ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "card")
                    .GetAwaiter().GetResult()
                : candidates[0];
            if (pick == null) return; // CR 701.19a — decline is legal.

            caster.Zones.Library.RemoveCard(pick);
            caster.Zones.Hand.AddCard(pick);
            pick.SetZone(ZoneType.Hand);
            // CR 701.20a — shuffle after the search resolves.
            Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "masterminds-acquisition");
        });

    private static IEffect BuildWishboardEffect(Player caster) =>
        new WishTutorEffect(
            predicate: WishTutorEffect.Predicates.AnyCard,
            pileLabel: "a card you own from outside the game",
            intent: BotIntent.Tutor)
            .AsEffect(caster);
}
