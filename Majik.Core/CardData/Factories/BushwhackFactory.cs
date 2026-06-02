using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bushwhack (Modern Horizons 2, {G}).
///
/// Sorcery. Oracle text:
///   "Choose one —
///     • Search your library for a basic land card, reveal it, put it into
///       your hand, then shuffle.
///     • Target creature you control fights target creature you don't control.
///       (Each deals damage equal to its power to the other.)"
///
/// CR 700.2d — modal "Choose one —" spell. Only mode 1 (fight) takes targets;
/// mode 0 (the basic-land tutor) is targetless. The bound
/// <see cref="SpellDefinition"/> exposes a target slot per mode so the chosen
/// mode index lines up with its slot, with MinTargets=0 on the tutor slot so
/// the unchosen tutor mode never gates the cast (mirrors
/// <see cref="WitherbloomCharmFactory"/> / <see cref="IzzetCharmFactory"/>).
///
/// ## Mode 0 — basic-land tutor to hand (CR 701.19 / CR 701.20a)
/// "Search your library for a basic land card, reveal it, put it into your
/// hand, then shuffle." MANDATORY search (no "you may") for ONE basic land
/// (CR 305.6 — Basic supertype + Land card type). Consults the registered
/// <see cref="IPlayerAgent"/> via
/// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>; a library with no basic
/// lands legally finds nothing (CR 701.19c). Moves the pick Library → Hand and
/// shuffles ONCE via <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a —
/// a single search effect performs exactly one shuffle whether or not a card
/// was found). Mirrors <see cref="BorderlandRangerFactory"/>'s tutor body, but
/// the search is mandatory rather than optional. Deterministic first-basic
/// fallback when no agent is registered (same posture as the tutor family).
///
/// ## Mode 1 — fight (CR 701.13)
/// "Target creature you control fights target creature you don't control."
/// Two 1..1 creature target requests. On resolution each creature deals damage
/// equal to its power to the other simultaneously (CR 701.13a); both powers are
/// read BEFORE any damage applies so a power-reducing interaction on one does
/// not change the other's incoming damage. A target that is no longer a
/// battlefield creature at resolution causes the fight to do nothing
/// (CR 608.2b). Cribbed from <see cref="KhalniAmbushFactory"/>.
///
/// ## Shape source
/// Card identity (name, {G}, Sorcery) is loaded from
/// <c>Majik.Core/CardData/Cards/bushwhack.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The modal behaviour is attached in code
/// (the JSON ability schema does not express modal "Choose one" spells).
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal step</b>: the tutored basic moves Library → Hand without
///   publishing a reveal event — same gap as every tutor factory
///   (<see cref="BorderlandRangerFactory"/>, Cultivate). The card still reaches
///   the hand so the observable game state is correct; only the public
///   "reveal" UI signal is absent.
/// </summary>
[CardName("Bushwhack")]
public static class BushwhackFactory
{
    public const string CardName = "Bushwhack";
    public const string PrintedManaCost = "{G}";

    /// <summary>Mode 0 — basic-land tutor to hand.</summary>
    public const int ModeTutorBasic = 0;

    /// <summary>Mode 1 — fight.</summary>
    public const int ModeFight = 1;

    /// <summary>CR 700.2d — "Choose one —" pick count.</summary>
    public const int PickCount = 1;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 2;

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Choose one —\n" +
        "• Search your library for a basic land card, reveal it, put it into your hand, then shuffle.\n" +
        "• Target creature you control fights target creature you don't control. " +
        "(Each deals damage equal to its power to the other.)";

    /// <summary>Printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Search your library for a basic land card, reveal it, put it into your hand, then shuffle.",
        "Target creature you control fights target creature you don't control.",
    };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("bushwhack");

    /// <summary>
    /// Construct Bushwhack as a <see cref="Sorcery"/> owned by
    /// <paramref name="owner"/>. Suitable for identity / shape / dispatcher
    /// tests; the resolve-time <see cref="SpellDefinition"/> is built on demand
    /// via <see cref="BuildDefinition"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Sorcery)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the modal SpellDefinition for Bushwhack (CR 700.2d).
    /// </summary>
    /// <param name="caster">The spell's controller (performs the tutor).</param>
    /// <param name="targetResolver">Resolves the raw mode-1 target tokens to
    /// live engine objects. Pass <c>o =&gt; o</c> for tests.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target slot per mode so the chosen mode index lines
        // up with its slot. The tutor mode is targetless; the fight mode needs
        // both a creature you control and a creature you don't (CR 701.13).
        var targetRequests = new[]
        {
            // Mode 0 — tutor a basic land to hand (no target).
            new TargetRequest("no target", 0, 0, Array.Empty<object>(), BotIntent.Ramp),
            // Mode 1 — your creature.
            new TargetRequest(
                "target creature you control",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Burn),
            // Mode 1 — their creature.
            new TargetRequest(
                "target creature you don't control",
                0, 1,
                Array.Empty<object>(),
                BotIntent.Burn),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Ramp,
                BotIntent.Burn,
            },
            EffectFactory: p =>
            {
                // Honour either the multi-pick list (first entry wins for a
                // Choose-one card) or the legacy scalar ModeIndex.
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
                        case ModeTutorBasic:
                            effectsOut.Add(BuildTutorEffect(caster));
                            break;
                        case ModeFight:
                            effectsOut.Add(BuildFightEffect(p, targetResolver));
                            break;
                    }
                }
                return effectsOut;
            });
    }

    /// <summary>
    /// Mode 0 — CR 701.19 / CR 701.20a. Mandatory search for ONE basic land,
    /// move it Library → Hand, then shuffle once. The "reveal it" step is a
    /// no-op signal in v1 (same gap as every tutor factory); the card still
    /// reaches the hand so the observable state is correct.
    /// </summary>
    private static IEffect BuildTutorEffect(Player caster) =>
        new Effect(
            $"{CardName} — search a basic land -> hand, then shuffle",
            ctx => TutorOneBasicToHandAsync(caster, ctx));

    private static async ValueTask TutorOneBasicToHandAsync(Player player, ResolutionContext ctx)
    {
        bool IsBasicLand(ICard c) =>
            c.HasType(CardType.Land) && c.HasSupertype(CardSupertype.Basic);

        var agent = ctx.Agent ?? AgentRegistry.Get(player);

        var candidates = player.Zones.Library.GetCards().Where(IsBasicLand).ToList();
        ICard? pick = null;
        if (candidates.Count > 0)
        {
            // CR 701.19c — a search may legally find nothing if no basic land is
            // present; here at least one candidate exists, so consult the agent.
            pick = agent != null
                ? await agent.ChooseLibraryPickAsync(ctx.Game, candidates,
                        "basic land card to put into your hand")
                    .ConfigureAwait(false)
                : candidates[0];
        }

        if (pick != null)
        {
            var zones = ZoneServiceRegistry.Get(player);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Hand, player);
            }
            else
            {
                player.Zones.Library.RemoveCard(pick);
                player.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
            }
        }

        // CR 701.20a — shuffle once after the search, even when zero cards were
        // found (the search still happened).
        LibraryShuffle.ShuffleLibrary(player, "bushwhack");
    }

    /// <summary>
    /// Mode 1 — CR 701.13a. Each creature deals damage equal to its power to the
    /// other simultaneously. Both powers are read BEFORE any damage applies so a
    /// power-reducing effect on one creature does not change the other's
    /// incoming damage. A target that is no longer a battlefield creature at
    /// resolution causes the whole fight to do nothing (CR 608.2b).
    /// </summary>
    private static IEffect BuildFightEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect($"{CardName} — fight", () =>
        {
            object? rawA = p.Targets.Count > ModeFight && p.Targets[ModeFight].Count > 0
                ? p.Targets[ModeFight][0]
                : null;
            object? rawB = p.Targets.Count > ModeFight + 1 && p.Targets[ModeFight + 1].Count > 0
                ? p.Targets[ModeFight + 1][0]
                : null;

            if (rawA == null || rawB == null) return;

            var a = resolver(rawA);
            var b = resolver(rawB);

            // CR 608.2b — both targets must still be creatures on the
            // battlefield; otherwise the fight does nothing.
            if (a is not Creature ca || ca.Zone != ZoneType.Battlefield) return;
            if (b is not Creature cb || cb.Zone != ZoneType.Battlefield) return;

            // CR 701.13a — read both powers up front, then apply simultaneously.
            var aPower = ca.Power;
            var bPower = cb.Power;
            if (aPower > 0) cb.TakeDamage(aPower);
            if (bPower > 0) ca.TakeDamage(bPower);
        });
}
