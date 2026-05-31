using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Genesis Wave (Scars of Mirrodin, {X}{G}{G}{G}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "Reveal the top X cards of your library. You may put any number of
///    permanent cards with mana value X or less from among them onto the
///    battlefield. Then put all cards revealed this way that weren't put
///    onto the battlefield into your graveyard."
///
/// Pairs two analogue shapes already in the engine:
/// - <see cref="CollectedCompanyFactory"/> — reveal/peek the top of the
///   library, agent-driven "put any number onto the battlefield" pick loop,
///   permanents routed Library → Battlefield through <see cref="ZoneService"/>
///   when one is registered (CR 603.6a — ETB triggers fire).
/// - <see cref="GreenSunsZenithFactory"/> — variable-X idiom
///   (<see cref="SpellDefinition.HasVariableX"/> = true; resolve reads
///   <c>ChosenSpellParams.X</c> both as the reveal count AND as the mana-value
///   ceiling) plus library manipulation.
///
/// The base card shape (name / Sorcery type / {X}{G}{G}{G} cost) is
/// materialised from the embedded JSON definition (<c>genesis-wave.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the resolve-time effect is layered
/// on here because the JSON <c>AbilityDefinition</c> schema does not express the
/// X-driven reveal/put loop (same posture as <see cref="ArdentPleaFactory"/>).
///
/// ## Implemented (v1)
/// - Sorcery shape, printed cost {X}{G}{G}{G}.
/// - <see cref="BuildSpellDefinition"/> returns a <see cref="SpellDefinition"/>
///   with <c>HasVariableX = true</c>, no targets, and a single effect closure
///   that resolves against the caster's library.
/// - <b>Reveal top X</b> (CR 701.16a — "reveal"; the revealed cards are the top
///   X, fewer if the library is short, which never throws — CR 120.x). X is the
///   chosen value from cast time; X = 0 ⇒ no cards revealed ⇒ no-op.
/// - <b>Eligibility</b>: permanent cards (artifact / creature / enchantment /
///   land / planeswalker — CR 110.4a) whose mana value ≤ X (CR 202.3 — mana
///   value reads off the printed cost; CR 202.3b — X in a card's cost is 0 in
///   any zone other than the stack, so the reveal pile's own costs compute with
///   X = 0). Nonpermanent cards (instant / sorcery) are never eligible.
/// - <b>"You may put any number"</b> (CR 122.1c / the "you may" rider): an
///   unbounded agent-driven pick loop — each pass offers the remaining eligible
///   permanents and the agent either picks one (it moves to the battlefield) or
///   declines (<see langword="null"/>), which ends the loop. Same per-pick
///   prompt shape as <see cref="CollectedCompanyFactory"/>, with the upper
///   bound removed (Genesis Wave has no cap). No agent registered ⇒ greedy:
///   every eligible permanent is put onto the battlefield (deterministic
///   pre-agent posture, matching Collected Company).
/// - <b>Battlefield move</b>: picked permanents route Library → Battlefield via
///   <see cref="ZoneService.MoveCard"/> when a service is supplied / registered
///   (CR 603.6a — ETB triggers fire; <c>CardMovedEvent</c> publishes); raw zone
///   mutation fallback otherwise.
/// - <b>Rest to graveyard</b> (CR 608.2 — "Then put all cards revealed this way
///   that weren't put onto the battlefield into your graveyard"): every revealed
///   card not put onto the battlefield (over-cost permanents, nonpermanents, and
///   declined permanents alike) moves Library → Graveyard.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the reveal does not publish a per-card reveal event —
///   same gap as the rest of the reveal-top-N family
///   (<see cref="CollectedCompanyFactory"/>).
/// - <b>Simultaneous entry</b>: picked permanents enter one at a time in pick
///   order rather than as a single simultaneous batch (CR 603.6b corner cases
///   for "as ~ enters" replacements seeing each other are out of scope), the
///   same lossy simplification as Collected Company.
/// </summary>
[CardName("Genesis Wave")]
public static class GenesisWaveFactory
{
    public const string CardName = "Genesis Wave";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "genesis-wave";

    /// <summary>
    /// Construct Genesis Wave owned and controlled by <paramref name="owner"/>.
    /// Base shape (name / Sorcery / {X}{G}{G}{G}) is materialised from the
    /// embedded JSON; the resolve-time spell definition is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Genesis Wave uses on resolution.
    /// <see cref="SpellDefinition.HasVariableX"/> is true so the engine prompts
    /// for X at cast time; the resolve-time effect reads
    /// <c>ChosenSpellParams.X</c> as BOTH the reveal count and the mana-value
    /// ceiling.
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library is
    /// revealed and onto whose battlefield / into whose graveyard the revealed
    /// cards are routed.</param>
    /// <param name="card">The Genesis Wave card instance (kept for parity with
    /// the analogue factories; the effect itself does not relocate the spell —
    /// a sorcery goes to the graveyard via the normal stack resolver).</param>
    /// <param name="zoneService">Optional. When supplied, the picked permanents'
    /// Library → Battlefield moves route through this service so ETB triggers
    /// (CR 603.6a) and <c>CardMovedEvent</c> listeners fire. When null,
    /// <see cref="ZoneServiceRegistry.Get"/> is consulted, falling back to raw
    /// zone manipulation.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ICard card,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(card);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                var x = p.X ?? 0;
                return new IEffect[]
                {
                    new Effect(
                        $"Genesis Wave: reveal top {x}, put any number of permanents " +
                        $"with mv ≤ {x} onto the battlefield, rest to graveyard.",
                        ctx => ResolveAsync(caster, x, ctx, zoneService)),
                };
            });
    }

    /// <summary>
    /// Execute Genesis Wave's resolution against <paramref name="caster"/>'s
    /// library at the chosen <paramref name="x"/>. Public so tests and bots can
    /// drive the resolution without going through SpellCastFlow.
    /// </summary>
    /// <param name="caster">Spell controller — library / battlefield / graveyard
    /// owner.</param>
    /// <param name="x">The chosen X: reveal count AND mana-value ceiling.</param>
    /// <param name="zoneService">Optional zone service for routing the
    /// Library → Battlefield move so ETB triggers fire.</param>
    /// <param name="agent">Optional explicit agent that owns the "any number"
    /// pick decisions. When null, falls back to <see cref="AgentRegistry.Get"/>;
    /// when no agent is registered either, greedily puts every eligible
    /// permanent onto the battlefield (deterministic pre-agent posture).</param>
    public static async ValueTask ResolveAsync(
        Player caster,
        int x,
        ResolutionContext ctx,
        ZoneService? zoneService = null,
        IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        if (x <= 0) return;

        var library = caster.Zones.Library;

        // CR 701.16a — reveal the top X cards (fewer if the library is short).
        var revealed = library.GetCards().Take(x).ToList();
        if (revealed.Count == 0) return;

        // CR 110.4a — permanent card types. CR 202.3 — mana value off the
        // printed cost; CR 202.3b — X in cost reads as 0 outside the stack.
        bool IsEligible(ICard c) =>
            IsPermanentType(c) &&
            ManaCost.Parse(c.ManaCost ?? string.Empty).TotalValue <= x;

        agent = ctx.Agent ?? agent ?? AgentRegistry.Get(caster);

        // CR 122.1c — "you may put any number". Unbounded pick loop: each pass
        // offers the remaining eligible permanents; the agent picks one (→
        // battlefield) or declines (→ loop ends). No agent registered ⇒ greedy
        // (put every eligible permanent), mirroring Collected Company.
        var picks = new List<ICard>();
        var taken = new HashSet<ICard>();

        while (true)
        {
            var candidates = revealed
                .Where(c => !taken.Contains(c) && IsEligible(c))
                .ToList();
            if (candidates.Count == 0) break;

            ICard? pick;
            if (agent != null)
            {
                pick = await agent.ChooseLibraryPickAsync(
                        ctx.Game,
                        candidates: candidates,
                        kindLabel: $"permanent card with mana value {x} or less")
                    .ConfigureAwait(false);

                // CR 122.1c — declining ends the "any number" loop.
                if (pick == null) break;
                // Defensive: agent must pick from the offered candidates.
                if (!candidates.Contains(pick)) break;
            }
            else
            {
                // Greedy deterministic fallback — take the first candidate each
                // pass until none remain.
                pick = candidates[0];
            }

            picks.Add(pick);
            taken.Add(pick);
        }

        // Move picked permanents Library → Battlefield (CR 603.6a via service).
        var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(caster);
        foreach (var pick in picks)
        {
            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(
                    pick, ZoneType.Library, ZoneType.Battlefield, caster);
            }
            else
            {
                library.RemoveCard(pick);
                caster.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                pick.SetController(caster);
            }
        }

        // CR 608.2 — every revealed card not put onto the battlefield goes to
        // the caster's graveyard (over-cost permanents, nonpermanents, and
        // declined permanents alike).
        var toGraveyard = revealed.Where(c => !taken.Contains(c)).ToList();
        foreach (var c in toGraveyard)
        {
            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(
                    c, ZoneType.Library, ZoneType.Graveyard, caster);
            }
            else
            {
                library.RemoveCard(c);
                caster.Zones.Graveyard.AddCard(c);
                c.SetZone(ZoneType.Graveyard);
            }
        }
    }

    /// <summary>
    /// CR 110.4a — permanent card types. Battle / Saga are out of scope (no
    /// <c>Battle</c> enum member ships yet — same caveat as
    /// <see cref="AetherworksMarvelFactory"/>).
    /// </summary>
    private static bool IsPermanentType(ICard card) =>
        card.HasType(CardType.Artifact)
        || card.HasType(CardType.Creature)
        || card.HasType(CardType.Enchantment)
        || card.HasType(CardType.Land)
        || card.HasType(CardType.Planeswalker);
}
