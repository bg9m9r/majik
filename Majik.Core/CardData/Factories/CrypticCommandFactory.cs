using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cryptic Command (Lorwyn, {1}{U}{U}{U}).
///
/// Instant. Oracle text:
///   "Choose two —
///     • Counter target spell.
///     • Return target permanent to its owner's hand.
///     • Tap all creatures your opponents control.
///     • Draw a card."
///
/// CR 700.2d — modal spells choose N distinct modes; each chosen mode's
/// effects are applied in the printed order. <see cref="ModalChooseOneTemplate"/>'s
/// runtime already supports multi-pick via <see cref="ChosenSpellParams.ModeIndexes"/>;
/// this factory wires Cryptic Command's four mode bodies directly so the
/// spell binds to a hand-shaped SpellDefinition (the oracle-text path
/// would need per-mode template binding for "Counter target spell",
/// "Return target permanent to its owner's hand", "Tap all creatures
/// your opponents control", "Draw a card" — composing those at this
/// layer is heavier than a single inline definition, and the inline
/// form gives us deterministic resolution for tests).
///
/// Targets are addressed by index into <see cref="ChosenSpellParams.Targets"/>:
///   Targets[0] — chosen target for mode 0 (Counter), if mode 0 was picked.
///   Targets[1] — chosen target for mode 1 (Bounce), if mode 1 was picked.
/// Mode 2 (mass tap) and mode 3 (draw) are not targeted. Mode 2 reads
/// <see cref="ChosenSpellParams.AllPlayers"/> to find opponents; if that
/// list is unset, it falls back to a no-op (lossy v1).
///
/// The "choose two" prompt itself is owned by the caller — at v1
/// <see cref="SpellCastFlow"/> only collects a single <c>ModeIndex</c>;
/// callers that want full multi-pick wire <c>ModeIndexes</c> directly into
/// <see cref="ChosenSpellParams"/> (the modal runtime honours either
/// shape — see <see cref="ModalChooseOneTemplate.Rehydrate"/>).
/// </summary>
public static class CrypticCommandFactory
{
    public const string CardName = "Cryptic Command";

    public const int ModeCounter = 0;
    public const int ModeBounce = 1;
    public const int ModeTapOpponents = 2;
    public const int ModeDraw = 3;

    /// <summary>
    /// Number of modes to pick on cast (CR 700.2d — "Choose two —").
    /// </summary>
    public const int PickCount = 2;

    /// <summary>Total number of printed modes.</summary>
    public const int TotalModes = 4;

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, "{1}{U}{U}{U}");
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// The printed mode labels, in oracle order. Exposed so callers can
    /// reuse them when constructing the bound <see cref="SpellDefinition"/>
    /// or scoring per-mode intent in the bot agents.
    /// </summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Counter target spell.",
        "Return target permanent to its owner's hand.",
        "Tap all creatures your opponents control.",
        "Draw a card.",
    };

    /// <summary>
    /// Build the SpellDefinition for Cryptic Command. The caller resolves
    /// targets through <paramref name="targetResolver"/> (typically a
    /// <c>StackResolver</c>) and supplies the live <paramref name="stack"/>
    /// for the counter mode.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — target requests are emitted for every mode that
        // takes a target, regardless of whether that mode was chosen at
        // declare time. The caster only fills slots for chosen modes;
        // unchosen modes' slots arrive as empty lists. (The cast flow's
        // MinTargets gate is at 0 here so missing modes don't blow up.)
        // Modes 2 and 3 are non-targeted — they have no TargetRequest.
        var targetRequests = new[]
        {
            // Mode 0 — counter target spell.
            new TargetRequest("target spell", 0, 1, Array.Empty<object>(), BotIntent.Counter),
            // Mode 1 — return target permanent to its owner's hand.
            new TargetRequest("target permanent", 0, 1, Array.Empty<object>(), BotIntent.Bounce),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.Counter,
                BotIntent.Bounce,
                BotIntent.Removal, // mass-tap is removal-adjacent (opponent attackers locked)
                BotIntent.Draw,
            },
            EffectFactory: p =>
            {
                // Multi-pick — prefer ModeIndexes; fall back to legacy
                // scalar ModeIndex (single chosen mode) so the spell still
                // resolves if a caller hasn't yet upgraded to the list
                // shape.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var effects = new List<IEffect>();
                var seen = new HashSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    if (!seen.Add(raw)) continue;     // CR 700.2d — each mode at most once
                    if (seen.Count > PickCount) break; // honor printed pick count

                    switch (raw)
                    {
                        case ModeCounter:
                            effects.Add(BuildCounterEffect(p, targetResolver, stack));
                            break;
                        case ModeBounce:
                            effects.Add(BuildBounceEffect(p, targetResolver));
                            break;
                        case ModeTapOpponents:
                            effects.Add(BuildTapOpponentsEffect(caster, p));
                            break;
                        case ModeDraw:
                            effects.Add(BuildDrawEffect(caster));
                            break;
                    }
                }
                return effects;
            });
    }

    private static IEffect BuildCounterEffect(
        ChosenSpellParams p,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack) =>
        new Effect("Cryptic Command — counter target spell", () =>
        {
            if (stack == null) return;
            if (p.Targets.Count <= ModeCounter) return;
            var slot = p.Targets[ModeCounter];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ISpell spell) return;
            OracleSpellBinder.RemoveFromStack(stack, spell);
            spell.Card.SetZone(ZoneType.Graveyard);
        });

    private static IEffect BuildBounceEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Cryptic Command — return target permanent to owner's hand", () =>
        {
            if (p.Targets.Count <= ModeBounce) return;
            var slot = p.Targets[ModeBounce];
            if (slot.Count == 0) return;
            var resolved = resolver(slot[0]);
            if (resolved is not ICard card) return;
            ReturnToOwnersHand(card);
        });

    private static IEffect BuildTapOpponentsEffect(Player caster, ChosenSpellParams p) =>
        new Effect("Cryptic Command — tap all creatures your opponents control", () =>
        {
            // Iterate every player and tap creatures controlled by anyone
            // who isn't the caster. AllPlayers covers multiplayer; falls
            // back to a single-opponent search via caster's view if absent
            // (lossy — without AllPlayers we can't reach opponents in
            // n-player; v1 best-effort).
            var players = p.AllPlayers;
            if (players == null) return;
            foreach (var pl in players)
            {
                if (ReferenceEquals(pl, caster)) continue;
                foreach (var perm in pl.Zones.Battlefield.GetCards().OfType<Creature>())
                {
                    if (!perm.IsTapped) perm.Tap();
                }
            }
        });

    private static IEffect BuildDrawEffect(Player caster) =>
        new Effect("Cryptic Command — draw a card", () =>
        {
            var top = caster.Zones.Library.GetCards().FirstOrDefault();
            if (top == null) return;
            caster.Zones.Library.RemoveCard(top);
            caster.Zones.Hand.AddCard(top);
            top.SetZone(ZoneType.Hand);
        });

    private static void ReturnToOwnersHand(ICard card)
    {
        var owner = card.Owner;
        if (owner != null)
        {
            if (card.Zone == ZoneType.Battlefield)
                owner.Zones.Battlefield.RemoveCard(card);
            else if (card.Zone == ZoneType.Graveyard)
                owner.Zones.Graveyard.RemoveCard(card);
            else if (card.Zone == ZoneType.Exile)
                owner.Zones.Exile.RemoveCard(card);
            owner.Zones.Hand.AddCard(card);
        }
        card.SetZone(ZoneType.Hand);
    }
}
