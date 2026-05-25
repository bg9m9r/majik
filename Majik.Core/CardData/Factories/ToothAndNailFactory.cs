using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tooth and Nail (Mirrodin, {5}{G}{G}{G}).
///
/// Sorcery. Oracle text:
///   "Choose one —
///     • Search your library for up to two creature cards, reveal them,
///       put them into your hand, then shuffle.
///     • Search your library for up to two creature cards, put them
///       onto the battlefield, then shuffle.
///    Entwine {2}{R} (Choose both if you pay the entwine cost.)"
///
/// CR 700.2d — modal "Choose one —" sorcery with two modes. Each mode
/// permits "up to two" picks, allowing fewer-than-two finds (CR 701.19a).
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {5}{G}{G}{G}.
/// - Two modes wired through the standard
///   <see cref="ChosenSpellParams.ModeIndexes"/> / <see cref="ChosenSpellParams.ModeIndex"/>
///   surface (mirrors <see cref="IzzetCharmFactory"/> /
///   <see cref="ClingToDustFactory"/>).
/// - <b>Mode 0 — tutor up to two creatures into hand</b>. Repeats the
///   library-pick prompt up to two times. The agent may decline the
///   second pick (return null) — CR 701.19a permits "up to two" finds
///   of zero, one, or two. A single CR 701.20a shuffle fires at the
///   end (one search effect → one post-search shuffle).
/// - <b>Mode 1 — tutor up to two creatures onto the battlefield</b>.
///   Same up-to-two prompt loop, but each pick is routed through
///   <see cref="ZoneServiceRegistry"/> so ETB triggers fire
///   (CR 603.6a). Falls back to raw library→battlefield zone moves
///   when no live <see cref="ZoneService"/> is wired.
/// - <b>Entwine marker</b>. The printed Entwine {2}{R} clause is
///   surfaced on the card via <see cref="EntwineCost"/> /
///   <see cref="HasEntwine"/> so dispatcher-aware shape tests can
///   inspect it. The cost is NOT layered onto the cast flow — Majik
///   has no Entwine primitive yet (no Entwine cost class, no cast-time
///   "pick both" branch in <see cref="SpellCastFlow"/>). Multi-mode
///   resolution itself IS supported (see <c>ModeIndexes</c>); a future
///   Entwine primitive can populate <c>ModeIndexes = {0, 1}</c> from
///   the cast flow when the {2}{R} rider is paid.
///
/// ## Deferred (v1 gaps)
/// - <b>Entwine cost</b>. No <c>EntwineAdditionalCost</c> /
///   <c>EntwineAlternativeCost</c> primitive in <c>Majik.Core/Costs/</c>
///   today. Adding one would be a one-line change to layer onto
///   <see cref="SpellCastFlow"/>; for now the rider is documented +
///   surfaced as a marker constant.
/// - <b>Mode prompt</b>. The agent doesn't yet pick between the two
///   modes at announcement; callers must populate
///   <c>ChosenSpellParams.ModeIndex(es)</c> directly. Same posture as
///   every other Choose-one modal factory.
/// - <b>Reveal event</b>. Library-pick moves don't publish reveal
///   events; same gap as the other search factories.
/// </summary>
[CardName("Tooth and Nail")]
public static class ToothAndNailFactory
{
    public const string CardName = "Tooth and Nail";
    public const string PrintedManaCost = "{5}{G}{G}{G}";

    /// <summary>Printed Entwine cost rider. Marker only — see class summary.</summary>
    public const string EntwineCost = "{2}{R}";

    /// <summary>Whether the card prints an Entwine rider. Always true for Tooth and Nail.</summary>
    public const bool HasEntwine = true;

    public const int ModeTutorToHand        = 0;
    public const int ModeTutorToBattlefield = 1;

    /// <summary>CR 700.2d — base "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Maximum creatures tutored by either mode (CR 701.19a "up to two").</summary>
    public const int MaxTutorsPerMode = 2;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Search your library for up to two creature cards, reveal them, put them into your hand, then shuffle.",
        "Search your library for up to two creature cards, put them onto the battlefield, then shuffle.",
    };

    /// <summary>
    /// Build a Tooth and Nail sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time spell definition is built on
    /// demand via <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Tooth and Nail uses on
    /// resolution. Two modes, both honouring the
    /// <see cref="ChosenSpellParams.ModeIndexes"/> / scalar
    /// <see cref="ChosenSpellParams.ModeIndex"/> selection (CR 700.2d).
    /// Entwine is deferred; populate <c>ModeIndexes = {0, 1}</c>
    /// directly to simulate the "Choose both" branch.
    /// </summary>
    /// <param name="caster">Player resolving the spell — owns the
    /// library searched, the hand receiving Mode 0 picks, and the
    /// battlefield receiving Mode 1 picks.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                // Mode 0 — tutor two creatures into hand: card advantage
                // + tutor classification (CR 701.19a search).
                BotIntent.Tutor | BotIntent.CardAdvantage,
                // Mode 1 — tutor two creatures directly onto the
                // battlefield: cheat-into-play (CR 603.6a triggers fire).
                BotIntent.Tutor | BotIntent.CheatIntoPlay,
            },
            EffectFactory: p =>
            {
                // Honor either the multi-pick list (Entwine = {0, 1}) or
                // the legacy scalar ModeIndex (Choose one).
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue
                        ? new[] { p.ModeIndex.Value }
                        : Array.Empty<int>());

                var effectsOut = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue; // CR 700.2d — each mode at most once
                    // No PickCount cap — Entwine permits all modes when
                    // the cost is paid. The cast flow is responsible for
                    // populating only the chosen indices (1 or 2 here).

                    switch (raw)
                    {
                        case ModeTutorToHand:
                            effectsOut.Add(BuildTutorToHandEffect(caster));
                            break;
                        case ModeTutorToBattlefield:
                            effectsOut.Add(BuildTutorToBattlefieldEffect(caster));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    /// <summary>
    /// Mode 0 — "Search your library for up to two creature cards,
    /// reveal them, put them into your hand, then shuffle."
    /// CR 701.19a / CR 701.20a.
    /// </summary>
    private static IEffect BuildTutorToHandEffect(Player caster) =>
        new Effect(
            $"{CardName} — tutor up to 2 creatures into hand",
            () =>
            {
                static bool Pred(ICard c) => c.HasType(CardType.Creature);

                var agent = AgentRegistry.Get(caster);
                var found = 0;
                for (var i = 0; i < MaxTutorsPerMode; i++)
                {
                    var candidates = caster.Zones.Library.GetCards()
                        .Where(Pred).ToList();
                    if (candidates.Count == 0) break;

                    var pick = agent != null
                        ? agent.ChooseLibraryPickAsync(
                            ctx: null,
                            candidates,
                            "creature card")
                            .GetAwaiter().GetResult()
                        : candidates[0];
                    if (pick == null) break; // CR 701.19a — decline ends "up to" loop.

                    caster.Zones.Library.RemoveCard(pick);
                    caster.Zones.Hand.AddCard(pick);
                    pick.SetZone(ZoneType.Hand);
                    found++;
                }

                // CR 701.20a — one search effect → one post-search shuffle.
                LibraryShuffle.ShuffleLibrary(caster, "tooth-and-nail/hand");
            });

    /// <summary>
    /// Mode 1 — "Search your library for up to two creature cards, put
    /// them onto the battlefield, then shuffle." CR 701.19a /
    /// CR 603.6a (ETB triggers via <see cref="ZoneService"/>) /
    /// CR 701.20a.
    /// </summary>
    private static IEffect BuildTutorToBattlefieldEffect(Player caster) =>
        new Effect(
            $"{CardName} — tutor up to 2 creatures onto the battlefield",
            () =>
            {
                static bool Pred(ICard c) => c.HasType(CardType.Creature);

                var agent = AgentRegistry.Get(caster);
                var zones = ZoneServiceRegistry.Get(caster);

                for (var i = 0; i < MaxTutorsPerMode; i++)
                {
                    var candidates = caster.Zones.Library.GetCards()
                        .Where(Pred).ToList();
                    if (candidates.Count == 0) break;

                    var pick = agent != null
                        ? agent.ChooseLibraryPickAsync(
                            ctx: null,
                            candidates,
                            "creature card")
                            .GetAwaiter().GetResult()
                        : candidates[0];
                    if (pick == null) break; // CR 701.19a — decline ends "up to" loop.

                    if (zones != null)
                    {
                        // CR 603.6a — ETB triggers fire on tutored
                        // creatures when routed through ZoneService.
                        zones.MoveCard(
                            pick, ZoneType.Library, ZoneType.Battlefield, caster);
                    }
                    else
                    {
                        // Shape / dispatcher-test fallback.
                        caster.Zones.Library.RemoveCard(pick);
                        caster.Zones.Battlefield.AddCard(pick);
                        pick.SetZone(ZoneType.Battlefield);
                        pick.SetController(caster);
                    }
                }

                // CR 701.20a — one search effect → one post-search shuffle.
                LibraryShuffle.ShuffleLibrary(caster, "tooth-and-nail/battlefield");
            });
}
