using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Valakut Awakening // Valakut Stoneforge (Zendikar Rising, {2}{R}).
///
/// Instant. Oracle text (front, verified against Scryfall):
///   "Put any number of cards from your hand on the bottom of your library,
///    then draw that many cards plus one."
///
/// Back face — <see cref="ValakutStoneforgeFactory"/> (Land —
/// "This land enters tapped." / "{T}: Add {R}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Two-factory cast-either-face dispatch — same architecture as
/// <see cref="BalaGedRecoveryFactory"/> / <see cref="BalaGedSanctuaryFactory"/>.
/// Casting the front face resolves "Valakut Awakening" → this factory → an
/// <see cref="Instant"/> with the bottom-then-draw effect. Playing the back
/// face resolves "Valakut Stoneforge" → <see cref="ValakutStoneforgeFactory"/>
/// → a simple tapland.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>valakut-awakening.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time spell behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor the bottom-then-draw effect).
///
/// ## Implemented (v1)
///
/// - Instant identity at <c>{2}{R}</c>, mono-red ({R} pip), owner /
///   controller wired.
/// - <see cref="MdfcState"/> attached (front = "Valakut Awakening",
///   back = "Valakut Stoneforge"); starts on the front face.
/// - No modes, no X, no target requests — the effect resolves entirely on
///   the caster (same posture as <see cref="QuickStudyFactory"/>).
/// - <b>Resolve</b> (CR 608.2e — left-to-right clause ordering, CR 121.1
///   draw):
///     <list type="bullet">
///       <item>"Put any number of cards from your hand on the bottom of
///         your library" — the caster's registered <see cref="IPlayerAgent"/>
///         is consulted via <see cref="IPlayerAgent.ChooseCardsToBottomAsync"/>
///         (the "any number" choice; the agent may return zero up to the
///         whole hand). Each chosen card is removed from the hand and
///         appended to the library — appending is the BOTTOM, since the top
///         of the library is index 0 (see <see cref="Fx.DrawCards"/>, which
///         draws the FIRST card). Only cards still in the caster's hand are
///         honoured (defensive — an agent can't bottom a card it doesn't
///         hold).</item>
///       <item>"then draw that many cards plus one" — the caster draws
///         <c>(bottomed count) + 1</c> via <see cref="Fx.DrawCards"/>. Each
///         draw routes through the replacement bus (Dredge etc.); an empty
///         library stamps the SBA loss flag (CR 704.5b) without throwing.
///         Note the bottomed cards went to the BOTTOM, so they are not
///         re-drawn unless the library was already that short.</item>
///     </list>
///   The pre-agent default (no registered agent) bottoms nothing, so the
///   caster simply draws one card — the minimal legal resolution.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Agent count surface</b> — "any number" is modelled by passing the
///   full hand size as the cap to <see cref="IPlayerAgent.ChooseCardsToBottomAsync"/>
///   and honouring whatever subset the agent returns (it may return fewer,
///   including zero). The existing prompt was designed for the mulligan
///   "bottom exactly N" case; here it is reused as "bottom up to N", which
///   the ScriptedAgent / bot agents already satisfy.
/// </summary>
[CardName("Valakut Awakening")]
public static class ValakutAwakeningFactory
{
    public const string CardName = "Valakut Awakening";
    public const string BackName = "Valakut Stoneforge";
    public const string Slug = "valakut-awakening";

    /// <summary>
    /// Construct the front face of Valakut Awakening as an Instant with
    /// owner / controller wired and the <see cref="MdfcState"/> face tracker
    /// attached (starts on the front face). Identity comes from the embedded
    /// JSON. The resolve-time body is produced by
    /// <see cref="BuildResolveEffect"/> / <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Valakut Stoneforge) is observable from the
        // front-face card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Valakut Awakening. No
    /// modes, no X, no target requests — the body resolves entirely on the
    /// caster.
    /// </summary>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => BuildResolveEffect(caster));
    }

    /// <summary>
    /// Build the resolve effect: bottom any number of cards from the
    /// caster's hand, then draw that many plus one (CR 121.1).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: bottom any number of cards from hand, then draw that many plus one.",
                async ctx =>
                {
                    // 1) "Put any number of cards from your hand on the
                    //    bottom of your library." Consult the agent for the
                    //    chosen subset (CR 601.3h-style player choice).
                    var bottomed = await ChooseCardsToBottomAsync(caster, ctx).ConfigureAwait(false);
                    var bottomedCount = 0;
                    foreach (var card in bottomed)
                    {
                        // Defensive: only bottom cards still in the hand.
                        if (!caster.Zones.Hand.ContainsCard(card)) continue;
                        caster.Zones.Hand.RemoveCard(card);
                        // Append = BOTTOM of library (top is index 0 — see
                        // Fx.DrawCards, which draws the first card).
                        caster.Zones.Library.AddCard(card);
                        card.SetZone(ZoneType.Library);
                        bottomedCount++;
                    }

                    // 2) "then draw that many cards plus one." (CR 121.1)
                    // "that many" = the count actually bottomed in step 1.
                    Fx.DrawCards(caster, bottomedCount + 1);
                }),
        };
    }

    /// <summary>
    /// Consult the caster's registered <see cref="IPlayerAgent"/> for the
    /// "any number of cards from your hand" choice. The hand size is passed
    /// as the cap (so the agent may bottom up to its whole hand); the agent
    /// may return any subset, including zero. No agent registered → bottom
    /// nothing (minimal legal resolution: just draw one).
    /// </summary>
    private static async ValueTask<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(Player caster, ResolutionContext ctx)
    {
        var hand = caster.Zones.Hand.GetCards().ToList();
        if (hand.Count == 0) return Array.Empty<ICard>();

        var agent = ctx.Agent ?? AgentRegistry.Get(caster);
        if (agent == null) return Array.Empty<ICard>();

        try
        {
            var chosen = await agent.ChooseCardsToBottomAsync(
                    ctx.Game!,
                    hand: hand,
                    countToBottom: hand.Count)
                .ConfigureAwait(false);
            return chosen ?? (IReadOnlyList<ICard>)Array.Empty<ICard>();
        }
        catch
        {
            // Defensive: any agent failure → bottom nothing (still draw 1).
            return Array.Empty<ICard>();
        }
    }
}
