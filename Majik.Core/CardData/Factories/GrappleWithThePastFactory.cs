using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grapple with the Past (Eldritch Moon, {1}{G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Mill three cards, then you may return a creature or land card from your
///    graveyard to your hand. (To mill three cards, put the top three cards of
///    your library into your graveyard.)"
///
/// ## Relationship to the analogues
/// Grapple is the instant-speed, <i>mill-then-recur-from-graveyard</i> cousin
/// of <see cref="OverlordOfTheBalemurkFactory"/> ("mill four cards, then you
/// may return a non-Avatar creature or planeswalker card from your graveyard
/// to your hand"). It differs only in:
///   - <b>count = 3</b> (not 4),
///   - <b>eligible predicate = creature OR land</b> (CR 110.1 — not
///     "creature or planeswalker"), and
///   - it is an <b>Instant resolution</b> (CR 608.3 one-shot), not an
///     enters-or-attacks trigger.
///
/// Unlike <see cref="GrislySalvageFactory"/> / <see cref="SatyrWayfinderFactory"/>
/// (which <i>reveal</i> from the top of the library and pick before anything
/// hits the graveyard), Grapple first <b>mills</b> (the three milled cards land
/// in the graveyard and are themselves eligible to be returned), then picks
/// from the <b>whole</b> graveyard — so it reuses the
/// <see cref="Majik.Core.Keywords.MillAction.Apply"/> primitive plus the
/// graveyard-pick-to-hand idiom from Overlord, not the
/// <c>RevealAndChoose</c> reveal primitive.
///
/// ## Implementation / prod wiring
/// The card shape (name, Instant, {1}{G}) comes from the embedded JSON
/// (<c>grapple-with-the-past.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The instant's <b>resolution</b> reaches
/// the live engine through the oracle-text spell-template registry — see
/// <see cref="SpellTemplates.Templates.Bespoke.GrappleWithThePastPatternTemplate"/>,
/// registered in <c>OracleSpellBinder.BuildTemplateList()</c> (same prod path
/// as <see cref="SpellTemplates.Templates.Bespoke.MalevolentRumblePatternTemplate"/>).
/// The <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>
/// helpers here mirror that body for direct unit testing; the shared resolution
/// core is <see cref="MillThreeThenMayReturnAsync"/>.
/// </summary>
[CardName("Grapple with the Past")]
public static class GrappleWithThePastFactory
{
    public const string CardName = "Grapple with the Past";
    public const string Slug = "grapple-with-the-past";
    public const string PrintedManaCost = "{1}{G}";

    /// <summary>Cards milled by the spell (printed value).</summary>
    public const int MillCount = 3;

    /// <summary>
    /// Build the Grapple with the Past card shape from the embedded JSON
    /// definition (name, Instant, {1}{G}). The resolve effect is built on
    /// demand via <see cref="BuildSpellDefinition"/> / <see cref="BuildResolveEffect"/>.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Grapple with the Past. No
    /// modes, no X, no target requests — the spell resolves entirely on the
    /// caster's own library + graveyard (CR 601 — the returned card is chosen
    /// at resolution from the graveyard, not a target).
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster, returnSelector: null));
    }

    /// <summary>
    /// Build Grapple with the Past's resolve effect — mill three, then the
    /// caster <i>may</i> return one creature or land card from their graveyard
    /// to hand (CR 701.13 mill + CR 117.x "may").
    /// </summary>
    /// <param name="caster">The spell's controller (CR 608.2).</param>
    /// <param name="returnSelector">Deterministic test override for the "you
    /// may return …" decision. Invoked with the pre-filtered list of eligible
    /// graveyard cards (creatures + lands); its return value is the card to
    /// return, or <see langword="null"/> to decline. When null, the registered
    /// <see cref="IPlayerAgent"/> is consulted via
    /// <see cref="IPlayerAgent.ChooseFromPileAsync"/>.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        Func<IReadOnlyList<ICard>, ICard?>? returnSelector = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                $"{CardName}: mill {MillCount}, then may return a creature or " +
                "land card from your graveyard to your hand.",
                ctx => MillThreeThenMayReturnAsync(caster, returnSelector, ctx)),
        };
    }

    /// <summary>
    /// Mill three cards from <paramref name="controller"/>'s library, then
    /// optionally return one eligible graveyard card (a creature card or a
    /// land card) to hand. The candidate set is the controller's <b>whole</b>
    /// graveyard after the mill (the milled cards are now in the graveyard and
    /// are themselves eligible). When <paramref name="returnSelector"/> is
    /// supplied it drives the decision; otherwise the registered agent is
    /// consulted. Returning <see langword="null"/> is a legal decline
    /// (CR 117.x).
    /// </summary>
    public static async ValueTask MillThreeThenMayReturnAsync(
        Player controller,
        Func<IReadOnlyList<ICard>, ICard?>? returnSelector,
        ResolutionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(controller);

        // CR 701.13 — mill three (put the top three of your library into your
        // graveyard). Fewer than three → mill whatever is there; no loss is
        // triggered directly (the empty-library loss is the CR 704.5b
        // draw-step SBA, not a mill).
        Majik.Core.Keywords.MillAction.Apply(controller, MillCount);

        // CR 110.1 — "a creature or land card": only the Creature and Land card
        // types are eligible from the whole graveyard (a creature land
        // qualifies via either type). The just-milled cards are now in the
        // graveyard and are themselves eligible.
        var candidates = controller.Zones.Graveyard.GetCards()
            .Where(IsEligibleReturn)
            .ToList();
        if (candidates.Count == 0) return;

        // "you may" (CR 117.x) — selector override (tests) or the registered
        // agent. The upside Reanimate intent makes the default agent accept
        // and pick the first candidate; an agentless path mirrors that.
        ICard? pick;
        if (returnSelector != null)
        {
            pick = returnSelector(candidates);
        }
        else
        {
            var agent = ctx.Agent ?? AgentRegistry.Get(controller);
            pick = agent != null
                ? await agent.ChooseFromPileAsync(
                    chooser: controller,
                    candidates: candidates,
                    pileLabel: "a creature or land card in your graveyard",
                    intent: BotIntent.Reanimate)
                    .ConfigureAwait(false)
                : candidates[0];
        }

        // Decline (null) or an out-of-set pick → no-op (CR 117.x).
        if (pick == null) return;
        if (!candidates.Contains(pick)) return;

        // Graveyard → hand. Direct-zone mutation mirrors the Overlord of the
        // Balemurk graveyard→hand return idiom at this resolution path.
        controller.Zones.Graveyard.RemoveCard(pick);
        controller.Zones.Hand.AddCard(pick);
        pick.SetZone(ZoneType.Hand);
    }

    /// <summary>
    /// Eligibility filter for the "return … to your hand" clause: a creature
    /// card or a land card (CR 110.1). Every other card type (instant,
    /// sorcery, non-land artifact, enchantment, planeswalker) is ineligible.
    /// </summary>
    public static bool IsEligibleReturn(ICard card) =>
        card != null &&
        (card.HasType(CardType.Creature) || card.HasType(CardType.Land));
}
