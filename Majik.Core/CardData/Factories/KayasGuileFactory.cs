using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Kaya's Guile (Modern Horizons, {1}{W}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Choose two —
///     • Each opponent sacrifices a creature of their choice.
///     • Exile all opponents' graveyards.
///     • Create a 1/1 white and black Spirit creature token with flying.
///     • You gain 4 life.
///    Entwine {3} (Choose all if you pay the entwine cost.)"
///
/// CR 700.2e — modal "Choose two —" with four modes; CR 702.41 — entwine is
/// an additional cost that lets the caster choose ALL modes when paid
/// (CR 700.2e). Shape-wise this is the same four-mode "Choose two" instant as
/// <see cref="KolaghansCommandFactory"/>; the per-mode bodies reuse existing
/// primitives only — no new engine mechanic is introduced.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{W}{B}, white + black. Card shape comes from
///   the embedded JSON (<c>kayas-guile.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - Modal "Choose two —" (CR 700.2e). Multi-pick is read from
///   <see cref="ChosenSpellParams.ModeIndexes"/> (legacy scalar
///   <see cref="ChosenSpellParams.ModeIndex"/> is also honoured). Each mode
///   resolves at most once (CR 700.2e — distinct modes), and the number of
///   modes resolved is capped at <see cref="PickCount"/> (=2) unless the
///   entwine path is taken (see below). No printed mode takes a cast-time
///   target, so <see cref="SpellDefinition.TargetRequests"/> is empty.
/// - <b>Mode 0 — "Each opponent sacrifices a creature of their choice"</b>:
///   mirrors <see cref="SheoldredsEdictFactory"/>. Iterates every opponent of
///   the caster (via <see cref="ChosenSpellParams.AllPlayers"/>) and makes that
///   opponent sacrifice one creature (CR 701.16). "Of their choice" — the
///   affected player's agent drives the pick (intent
///   <see cref="BotIntent.Removal"/>) with a deterministic first-creature
///   fallback. An opponent with no creature sacrifices nothing (no-op).
/// - <b>Mode 1 — "Exile all opponents' graveyards"</b>: each opponent's
///   graveyard is moved card-by-card to that opponent's exile zone
///   (CR 701.21 / CR 406). The caster's own graveyard is untouched.
/// - <b>Mode 2 — "Create a 1/1 white and black Spirit creature token with
///   flying"</b>: <see cref="TokenFactory.CreateOnBattlefield"/> with a
///   <see cref="TokenFactory.TokenSpec"/> carrying the explicit white+black
///   colour identity (CR 111.4) and the Flying keyword.
/// - <b>Mode 3 — "You gain 4 life"</b>: CR 119.3 — <see cref="Player.GainLife"/>.
///
/// ## Rules citations
/// - CR 700.2e — "Choose two —" (and "Choose all" under entwine).
/// - CR 702.41 — entwine (additional cost → choose all modes).
/// - CR 701.16 — sacrifice. CR 119.3 — gain life. CR 111.4 — token colour.
///
/// ## Deferred (v1 gaps)
/// - <b>Entwine additional cost</b>: the {3} entwine cost is NOT charged by
///   the cast-cost flow yet — same engine gap as every other entwine card
///   (<see cref="ToothAndNailFactory"/>; zero entwine costs are enforced at
///   v1). The definition still supports the "choose all" resolve: when the
///   caller bumps the pick count past two (the entwine path,
///   <c>entwined: true</c>), all four modes resolve. The caller is responsible
///   for stapling the additional cost. See <see cref="BuildDefinition"/>.
/// - <b>Forced sacrifice / exile prompts</b>: mode 0's "of their choice" pick
///   and the exile have no portal decision UI yet (same queue as Sheoldred's
///   Edict / Nihil Spellbomb).
/// </summary>
[CardName("Kaya's Guile")]
public static class KayasGuileFactory
{
    public const string CardName = "Kaya's Guile";
    public const string Slug = "kayas-guile";
    public const string PrintedManaCost = "{1}{W}{B}";

    public const int ModeEachOpponentSacrifices = 0;
    public const int ModeExileGraveyards        = 1;
    public const int ModeCreateSpirit           = 2;
    public const int ModeGainLife               = 3;

    /// <summary>CR 700.2e — printed "Choose two —" pick count (without entwine).</summary>
    public const int PickCount = 2;

    /// <summary>Total number of printed modes (the entwine "choose all" count).</summary>
    public const int TotalModes = 4;

    /// <summary>Mode 3 — life gained.</summary>
    public const int LifeGain = 4;

    /// <summary>Entwine additional cost (CR 702.41). Not enforced in v1 — see
    /// class header.</summary>
    public const string EntwineAdditionalCost = "{3}";

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Each opponent sacrifices a creature of their choice.",
        "Exile all opponents' graveyards.",
        "Create a 1/1 white and black Spirit creature token with flying.",
        "You gain 4 life.",
    };

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> for Kaya's Guile. No target
    /// requests — every mode resolves against the caster / its opponents
    /// directly. The chosen modes are read from
    /// <see cref="ChosenSpellParams.ModeIndexes"/> (or the legacy scalar
    /// <see cref="ChosenSpellParams.ModeIndex"/>).
    /// </summary>
    /// <param name="caster">The spell's controller — excluded from the "each
    /// opponent" / "opponents'" iterations; receives the Spirit token and the
    /// life gain.</param>
    /// <param name="allPlayers">All players in turn order (used to find the
    /// caster's opponents at resolution). The runtime's resolution-time list
    /// from <see cref="ChosenSpellParams.AllPlayers"/> takes precedence when
    /// supplied.</param>
    /// <param name="agent">Optional agent driving each affected player's mode-0
    /// "of their choice" sacrifice pick; null → deterministic first-creature
    /// fallback (mirrors <see cref="SheoldredsEdictFactory"/>).</param>
    /// <param name="zoneService">Optional zone service so the Spirit-token ETB
    /// routes through <see cref="ZoneService"/> (CardMovedEvent fires); null →
    /// direct zone move (shape-only / unit-test path).</param>
    /// <param name="entwined">Entwine path (CR 702.41): when <c>true</c> the
    /// pick-count cap is lifted so all four chosen modes resolve ("Choose
    /// all"). The caller is responsible for charging the {3} entwine cost —
    /// the cost flow does not yet (see class header). Default <c>false</c>.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        IPlayerAgent? agent,
        ZoneService? zoneService = null,
        bool entwined = false)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            ModeIntents: new[]
            {
                BotIntent.Removal, // each opponent sacrifices a creature
                BotIntent.Removal, // graveyard hate
                BotIntent.Token,   // make a flyer
                BotIntent.Heal,    // gain 4 life
            },
            EffectFactory: p =>
            {
                // Honor the multi-pick list (Choose-two / entwine path) or the
                // legacy scalar ModeIndex.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                // CR 702.41 / 700.2e — entwine lets ALL modes resolve; without
                // it, cap at the printed "Choose two —" pick count.
                var cap = entwined ? TotalModes : PickCount;

                var effects = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;  // CR 700.2e — each mode at most once
                    if (seen.Count > cap) break;   // honour the (entwine-adjusted) pick count

                    switch (raw)
                    {
                        case ModeEachOpponentSacrifices:
                            effects.Add(BuildEachOpponentSacrificesEffect(caster, allPlayers, agent, p));
                            break;
                        case ModeExileGraveyards:
                            effects.Add(BuildExileGraveyardsEffect(caster, allPlayers, p));
                            break;
                        case ModeCreateSpirit:
                            effects.Add(BuildCreateSpiritEffect(caster, zoneService));
                            break;
                        case ModeGainLife:
                            effects.Add(BuildGainLifeEffect(caster));
                            break;
                    }
                }
                return effects;
            });
    }

    // -----------------------------------------------------------------------
    // Mode bodies
    // -----------------------------------------------------------------------

    /// <summary>
    /// Mode 0 — "Each opponent sacrifices a creature of their choice."
    /// CR 701.16. Mirrors <see cref="SheoldredsEdictFactory"/>.
    /// </summary>
    private static IEffect BuildEachOpponentSacrificesEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        IPlayerAgent? agent,
        ChosenSpellParams p) =>
        new Effect($"{CardName}: each opponent sacrifices a creature of their choice", () =>
        {
            var players = p.AllPlayers is { Count: > 0 } fresh ? fresh : allPlayers;
            if (players == null) return;

            foreach (var pl in players)
            {
                // "Each opponent" — exclude the caster (CR 102.1).
                if (ReferenceEquals(pl, caster)) continue;

                var candidates = pl.Zones.Battlefield.GetCards()
                    .Where(c => c.HasType(CardType.Creature))
                    .ToList();

                // No creature → this opponent sacrifices nothing (CR 701.16).
                if (candidates.Count == 0) continue;

                // "Of their choice" — the affected player's agent drives the
                // pick (BotIntent.Removal); deterministic fallback to the
                // first creature in battlefield order (mirrors Sheoldred's
                // Edict / Diabolic Edict).
                ICard pick;
                if (agent != null)
                {
                    var chosen = agent
                        .ChooseFromBattlefieldAsync(pl, candidates, BotIntent.Removal)
                        .GetAwaiter().GetResult();

                    pick = (chosen != null
                            && chosen.Zone == ZoneType.Battlefield
                            && ReferenceEquals(chosen.Controller, pl)
                            && chosen.HasType(CardType.Creature))
                        ? chosen
                        : candidates[0];
                }
                else
                {
                    pick = candidates[0];
                }

                // CR 701.16 — sacrifice: battlefield → owner's graveyard,
                // bypassing Indestructible / regeneration.
                OracleSpellBinder.MoveToGraveyard(pick, ZoneMoveReason.Sacrifice);
            }
        });

    /// <summary>
    /// Mode 1 — "Exile all opponents' graveyards." CR 701.21. Each opponent's
    /// graveyard cards move to that opponent's exile zone; the caster's own
    /// graveyard is untouched. Mirrors the graveyard-exile idiom of
    /// <see cref="NihilSpellbombFactory"/>.
    /// </summary>
    private static IEffect BuildExileGraveyardsEffect(
        Player caster,
        IReadOnlyList<Player> allPlayers,
        ChosenSpellParams p) =>
        new Effect($"{CardName}: exile all opponents' graveyards", () =>
        {
            var players = p.AllPlayers is { Count: > 0 } fresh ? fresh : allPlayers;
            if (players == null) return;

            foreach (var pl in players)
            {
                // "Opponents'" — skip the caster (CR 102.1).
                if (ReferenceEquals(pl, caster)) continue;

                foreach (var card in pl.Zones.Graveyard.GetCards().ToList())
                {
                    pl.Zones.Graveyard.RemoveCard(card);
                    pl.Zones.Exile.AddCard(card);
                    card.SetZone(ZoneType.Exile);
                }
            }
        });

    /// <summary>
    /// Mode 2 — "Create a 1/1 white and black Spirit creature token with
    /// flying." CR 111.4 (colour identity) + CR 111.6 (enters the
    /// battlefield). Mirrors the token-mint idiom of
    /// <see cref="StormchasersTalentFactory"/> /
    /// <see cref="KrenkosCommandFactory"/>.
    /// </summary>
    private static IEffect BuildCreateSpiritEffect(Player caster, ZoneService? zoneService) =>
        new Effect($"{CardName}: create a 1/1 white and black Spirit with flying", () =>
        {
            var spec = new TokenFactory.TokenSpec(
                Name: "Spirit",
                Power: 1,
                Toughness: 1,
                Subtypes: new[] { CardSubtype.Spirit },
                Keywords: new[] { "Flying" },
                Colors: new[] { ManaColor.White, ManaColor.Black });
            TokenFactory.CreateOnBattlefield(spec, caster, zoneService);
        });

    /// <summary>Mode 3 — "You gain 4 life." CR 119.3.</summary>
    private static IEffect BuildGainLifeEffect(Player caster) =>
        new Effect($"{CardName}: you gain {LifeGain} life", () =>
        {
            caster.GainLife(LifeGain);
        });
}
