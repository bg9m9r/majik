using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vampiric Tutor (Visions / reprinted, {B}).
///
/// Instant. Oracle text (modern wording):
///   "Search your library for a card, then shuffle. Put that card on top.
///    You lose 2 life."
///
/// ## Why it gets its own factory
/// Vampiric Tutor is the unrestricted-predicate sibling of
/// <see cref="MysticalTutorFactory"/>: any card in the library is a legal
/// pick (no type filter), the destination is the top of the library
/// (index 0), and a 2-life payment fires regardless of whether a card
/// was found (CR 701.19a permits declining; the life loss is a separate
/// instruction on the resolve effect). The shared
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Search.SearchSpellFactory"/>
/// hard-codes pick→hand and doesn't compose a post-search life-loss
/// rider, so this card hosts its own resolve closure, mirroring the
/// pattern in <see cref="MysticalTutorFactory"/>.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}.
/// - On-resolve effect: ask the controller's agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for ANY card from
///   the library; place pick on top of library (index 0) via
///   <see cref="IZone.InsertCardAt"/>. No agent registered = the
///   deterministic first-match fallback used elsewhere in
///   <see cref="MysticalTutorFactory"/>. Empty library / null pick = no
///   tutor (CR 701.19a permits declining).
/// - Life-loss instruction: controller loses 2 life via
///   <see cref="Player.LoseLife"/> AFTER the (optional) tutor step,
///   regardless of whether a card was found. The printed order is
///   "search → shuffle → top → lose 2 life"; we keep the life-loss as a
///   post-tutor side effect in the same effect closure.
///
/// ## Deferred (v1 gaps)
/// - <b>Library shuffle</b> (CR 701.19c). Same rationale as the rest of
///   the tutor surface — no IZone.Shuffle entry point yet. The picked
///   card still ends up on top, which is the end state Vampiric Tutor
///   controllers actually consume next turn.
/// - <b>Reveal event</b>. The picked card moves Library → top-of-Library
///   without publishing a reveal event; same gap as the other search
///   factories.
/// </summary>
[CardName("Vampiric Tutor")]
public static class VampiricTutorFactory
{
    public const string CardName = "Vampiric Tutor";
    public const string PrintedManaCost = "{B}";

    /// <summary>
    /// Build a Vampiric Tutor instant owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time spell definition is built on
    /// demand via <see cref="BuildSpellDefinition"/> so the caster
    /// reference matches the player resolving the spell.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Vampiric Tutor uses on
    /// resolution. No predicate (any library card is eligible). The pick
    /// is inserted at index 0 of the controller's library — the
    /// canonical "top of library" position read by
    /// <see cref="Majik.Core.Game.Actions.DrawAction"/> and friends. The
    /// controller then loses 2 life regardless of whether a card was
    /// found.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect("tutor any card -> top of library; lose 2 life", () =>
                {
                    var candidates = caster.Zones.Library.GetCards().ToList();
                    if (candidates.Count > 0)
                    {
                        // Mirror MysticalTutorFactory: agent-driven pick
                        // with a deterministic first-match fallback. The
                        // kindLabel ("any card") is the prompt string
                        // surfaced to the agent so policies can score /
                        // filter on oracle wording.
                        var agent = AgentRegistry.Get(caster);
                        ICard? pick = agent != null
                            ? agent.ChooseLibraryPickAsync(
                                ctx: null,
                                candidates,
                                "any card")
                                .GetAwaiter().GetResult()
                            : candidates[0];

                        if (pick != null)
                        {
                            caster.Zones.Library.RemoveCard(pick);
                            caster.Zones.Library.InsertCardAt(0, pick);
                            pick.SetZone(ZoneType.Library);
                            // CR 701.19c — shuffle after a search effect.
                            // Deferred (see class xmldoc): no IZone.Shuffle
                            // entry point yet. The picked card still ends
                            // up on top, which is the end state Vampiric
                            // Tutor controllers actually consume next
                            // turn.
                        }
                    }

                    // CR 119.3 — the 2-life payment is unconditional. It
                    // fires whether or not the tutor found a card (CR
                    // 701.19a allows declining, but the cost-side life
                    // loss is a separate resolve instruction).
                    caster.LoseLife(2);
                }),
            });
    }
}
