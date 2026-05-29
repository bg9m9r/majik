using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Turntimber Symbiosis // Turntimber, Serpentine Wood
/// (Zendikar Rising, {4}{G}{G}{G}).
///
/// Sorcery. Oracle text (front, verified against Scryfall):
///   "Look at the top seven cards of your library. You may put a creature
///    card from among them onto the battlefield. If that card has mana
///    value 3 or less, it enters with three additional +1/+1 counters on
///    it. Put the rest on the bottom of your library in a random order."
///
/// Back face — <see cref="TurntimberSerpentineWoodFactory"/> (Land —
/// "As this land enters, you may pay 3 life. If you don't, it enters
/// tapped." / "{T}: Add {G}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="AgadeemsAwakeningFactory"/> /
/// <see cref="AgadeemTheUndercryptFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>turntimber-symbiosis.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the resolve-time dig behaviour are attached in code (the
/// JSON schema models neither MDFC faces nor look-at-top-N digs).
///
/// ## Implemented (v1)
///
/// - Sorcery identity at <c>{4}{G}{G}{G}</c>, mono-green (three {G} pips),
///   owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Turntimber Symbiosis",
///   back = "Turntimber, Serpentine Wood"); starts on the front face.
/// - <see cref="BuildSpellDefinition"/> returns a no-target, no-X
///   <see cref="SpellDefinition"/> whose single effect closure
///   <see cref="Resolve"/>:
///     <list type="bullet">
///       <item>Peeks the top seven cards of the caster's library
///         (CR 701.21 — short library is fine).</item>
///       <item>Prompts the caster's agent once for "you may put a creature
///         card from among them onto the battlefield" — any creature card,
///         no mana-value cap (unlike <see cref="CollectedCompanyFactory"/>).
///         The agent may decline (null) per the printed "you may".</item>
///       <item>Moves the chosen creature Library → Battlefield, routed
///         through <see cref="ZoneService.MoveCard"/> when available so ETB
///         triggers fire (CR 603.6a).</item>
///       <item>If that creature's mana value is 3 or less (CR 202.3 — the
///         printed mana value; X = 0 off the stack per CR 202.3b), it
///         enters with three additional +1/+1 counters (CR 122 —
///         <see cref="Fx.PlaceCounter"/>). "Enters with" counters are
///         applied as the permanent enters here at resolution.</item>
///       <item>Bottoms the rest in a random order
///         (<see cref="GameRandom.Shuffle"/> from
///         <see cref="GameRandomRegistry.Get"/> — deterministic when tests
///         seed it), mirroring <see cref="CollectedCompanyFactory"/>.</item>
///     </list>
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Enters with" replacement timing</b>: the three +1/+1 counters are
///   placed immediately after the Library → Battlefield move rather than via
///   a CR 614.1g enters-with replacement effect. The observable end state
///   (creature on the battlefield with the counters) is identical for the
///   one-shot resolution; same posture as the rest of the put-onto-battlefield
///   family that lacks a true enters-with replacement hook.
/// - <b>Reveal event</b>: the peek does not publish a per-card reveal event.
///   Same gap as <see cref="CollectedCompanyFactory"/> /
///   <see cref="AncientStirringsFactory"/>.
///
/// ## References
///
/// - <see cref="CollectedCompanyFactory"/> — the peek-top-N /
///   put-creatures-onto-battlefield / bottom-rest-randomly body this
///   directly cribs (single pick, no mv cap, +counters tweak).
/// - <see cref="AgadeemsAwakeningFactory"/> — companion ZNR MDFC spell
///   front face with the same MdfcState shape.
/// </summary>
[CardName("Turntimber Symbiosis")]
public static class TurntimberSymbiosisFactory
{
    public const string CardName = "Turntimber Symbiosis";
    public const string BackName = "Turntimber, Serpentine Wood";
    public const int PeekCount = 7;
    public const int CounterThresholdManaValue = 3;
    public const int AdditionalCounters = 3;

    /// <summary>
    /// Construct Turntimber Symbiosis as a Sorcery (identity from JSON) with
    /// the <see cref="MdfcState"/> face tracker attached. The resolve-time
    /// <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + printed cost come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("turntimber-symbiosis");
        var card = (Sorcery)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name is observable from the front-face card object.
        card.MdfcState = new MdfcState(CardName, BackName);

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Turntimber Symbiosis uses on
    /// resolution. No targets, no variable X — a single effect closure that
    /// performs the look-at-top-seven dig (see <see cref="Resolve"/>).
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library is
    /// peeked and onto whose battlefield the chosen creature lands.</param>
    /// <param name="zoneService">Optional. When supplied the chosen
    /// creature's Library → Battlefield move routes through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers fire
    /// (CR 603.6a). When null, <see cref="ZoneServiceRegistry.Get"/> is
    /// consulted, falling back to raw zone manipulation.</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect(
                    $"Turntimber Symbiosis: peek top {PeekCount}, put a creature card onto the " +
                    $"battlefield (+{AdditionalCounters} +1/+1 counters if mv ≤ {CounterThresholdManaValue}), " +
                    "rest to bottom in random order.",
                    () => Resolve(caster, zoneService)),
            });
    }

    /// <summary>
    /// Execute Turntimber Symbiosis's resolution against
    /// <paramref name="caster"/>'s library. Public so tests and bots can
    /// drive the resolution without going through SpellCastFlow.
    /// </summary>
    /// <param name="caster">Spell controller — library / battlefield owner.</param>
    /// <param name="zoneService">Optional zone service for routing the
    /// Library → Battlefield move so ETB triggers fire.</param>
    /// <param name="agent">Optional explicit agent that owns the "you may
    /// put a creature card" decision. When null, falls back to
    /// <see cref="AgentRegistry.Get"/>; when no agent is registered either,
    /// picks the first eligible creature (deterministic pre-agent posture,
    /// matching <see cref="CollectedCompanyFactory"/>).</param>
    public static void Resolve(
        Player caster,
        ZoneService? zoneService = null,
        IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        var library = caster.Zones.Library;

        // 1. Peek up to PeekCount cards (CR 701.21 — short library is fine).
        var peeked = library.GetCards().Take(PeekCount).ToList();
        if (peeked.Count == 0) return;

        // 2. Eligible pool: any creature card — no mana-value cap here
        //    (the mv ≤ 3 clause only gates the bonus counters, not eligibility).
        bool IsEligible(ICard c) => c.HasType(CardType.Creature);

        var candidates = peeked.Where(IsEligible).ToList();

        // 3. "You may put a creature card" — one pick; agent may decline.
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            agent ??= AgentRegistry.Get(caster);
            pick = agent != null
                ? agent.ChooseLibraryPickAsync(
                    ctx: null,
                    candidates: candidates,
                    kindLabel: "creature card")
                    .GetAwaiter().GetResult()
                : candidates[0];

            // CR 117.x — "you may" lets the agent decline (null).
            // Defensive: the agent must pick from the offered candidates.
            if (pick != null && !candidates.Contains(pick)) pick = null;
        }

        // 4. Move the picked creature Library → Battlefield, then apply the
        //    enters-with counters if its mana value is 3 or less.
        if (pick != null)
        {
            var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(caster);
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

            // "If that card has mana value 3 or less, it enters with three
            //  additional +1/+1 counters on it." (CR 202.3 / CR 122 / CR 614.1g)
            var mv = ManaCost.Parse(pick.ManaCost ?? string.Empty).TotalValue;
            if (mv <= CounterThresholdManaValue && pick is Permanent permanent)
            {
                Fx.PlaceCounter(permanent, CounterType.PlusOnePlusOne, AdditionalCounters);
            }
        }

        // 5. Bottom the rest in a random order. Per-game RNG; tests seed it.
        var remainder = peeked.Where(c => !ReferenceEquals(c, pick)).ToList();
        if (remainder.Count > 0)
        {
            var rng = GameRandomRegistry.Get(caster);
            rng.Shuffle(remainder);

            // Library.AddCard appends to the bottom; remove-then-add each
            // remainder card so the new bottom order is the shuffled order.
            foreach (var c in remainder)
            {
                library.RemoveCard(c);
            }
            foreach (var c in remainder)
            {
                library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }
    }
}
