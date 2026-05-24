using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aether Gust (Core Set 2020, {1}{U}).
///
/// Instant. Oracle text:
///   "Choose target spell or permanent that's red or green. Its owner puts
///    it on the top or bottom of their library."
///
/// ## Implemented (v1)
/// - Instant {1}{U} (Blue) card shape with owner / controller wired.
/// - <b>Bounce-to-library</b> — <see cref="BuildDefinition"/> builds a
///   <see cref="SpellDefinition"/> whose effect:
///   1. Resolves the chosen target to an <see cref="ISpell"/> on the stack
///      or a <see cref="Permanent"/> on the battlefield (CR 115 — "spell or
///      permanent" target).
///   2. Verifies the target is red or green at resolution time
///      (<see cref="CardColors"/> reads the printed mana cost per CR 105).
///      Illegal at resolution → effect does nothing (CR 608.2b).
///   3. If the target is a spell on the stack, removes it from the stack via
///      <see cref="OracleSpellBinder.RemoveFromStack"/> (CR 701.5 counter-by-
///      relocation — Aether Gust is NOT a counterspell, but the stack-removal
///      mechanic is identical). If the target is a permanent, removes it
///      from the battlefield.
///   4. Lets the target's <b>owner</b> (CR 109.4 / 701.20a — "its owner puts
///      it") choose top vs. bottom via the optional <c>topChooser</c>
///      callback supplied to <see cref="BuildDefinition"/>. Returning true
///      = top, false = bottom. When no chooser is wired, the v1 default
///      sends the card to the <b>bottom</b> of their library (mirrors the
///      conservative defaults used elsewhere — see Manamorphose / Subtlety).
///   5. Places the card via <see cref="IZone.InsertCardAt"/> (top = index 0)
///      or <see cref="IZone.AddCard"/> (bottom = append).
///
/// ## Notes on target legality (CR 115 / 608.2b)
/// "Target spell or permanent that's red or green" admits both ISpell on the
/// stack and Permanent on the battlefield. v1 ActionValidator does not yet
/// filter the agent's target list by colour or stack/battlefield zone — the
/// resolve-time guard catches illegal picks and no-ops the effect, matching
/// the pattern used by SpellSnare / ForceOfNegation / Karakas.
/// </summary>
[CardName("Aether Gust")]
public static class AetherGustFactory
{
    public const string CardName = "Aether Gust";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>
    /// Construct Aether Gust as an Instant card with owner / controller
    /// wired. The bounce SpellDefinition is built on demand via
    /// <see cref="BuildDefinition"/> at the SpellCastFlow resolver wire-up
    /// site (mirrors Force of Negation / Spell Snare).
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
    /// Build the "put target red/green spell or permanent on top or bottom
    /// of its owner's library" SpellDefinition.
    /// CR 608.2b: if the chosen target isn't red or green at resolution
    /// time, or isn't a spell/permanent in a valid zone, the effect does
    /// nothing.
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token chosen by
    /// the caster (typically the same <see cref="ISpell"/> or
    /// <see cref="Permanent"/> reference) — passed through verbatim in
    /// tests; production callers may translate via a TargetResolver service.</param>
    /// <param name="stack">Stack to remove a spell target from when the
    /// chosen target is an <see cref="ISpell"/>. May be null in shape tests.</param>
    /// <param name="topChooser">Optional callback invoked at resolution time
    /// with the target's owner; return true to put the card on top of that
    /// owner's library, false for bottom. Null = always bottom (v1 default).
    /// Production code wires this to the owner's <see cref="IPlayerAgent"/>;
    /// tests can pre-script the decision deterministically.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        Func<Player, bool>? topChooser = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target spell or permanent that's red or green",
                    1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        "Aether Gust — put target red/green spell or permanent on top or bottom of its owner's library",
                        () => Resolve(resolved, stack, topChooser)),
                };
            });

    private static void Resolve(
        object resolved,
        Majik.Core.Stack.Stack? stack,
        Func<Player, bool>? topChooser)
    {
        // Resolve the target into the card to move + the owner that decides
        // top/bottom + the zone-removal side effect.
        ICard? card = null;
        ISpell? spell = null;

        if (resolved is ISpell s)
        {
            spell = s;
            card = s.Card;
        }
        else if (resolved is Permanent perm)
        {
            card = perm;
        }

        if (card == null) return;

        // CR 105 — colour identity from printed mana cost. Aether Gust's
        // target predicate is "red or green".
        var colors = CardColors.GetColors(card);
        if (!colors.Contains(ManaColor.Red) && !colors.Contains(ManaColor.Green))
        {
            // CR 608.2b — illegal target at resolution → effect does nothing.
            return;
        }

        var owner = card.Owner;
        if (owner == null) return;

        // Pull the card out of its current zone before placing into library.
        if (spell != null)
        {
            if (stack == null) return; // shape-only path
            OracleSpellBinder.RemoveFromStack(stack, spell);
        }
        else if (card.Zone == ZoneType.Battlefield)
        {
            var src = (card is Permanent p && p.Controller != null) ? p.Controller : owner;
            src.Zones.Battlefield.RemoveCard(card);
        }
        else
        {
            // Defensive: target is neither on stack nor battlefield — bail
            // out rather than silently double-zone the card.
            return;
        }

        // CR 109.4 / 701.20a — "Its owner puts it on the top or bottom".
        // Defer to the supplied chooser; default = bottom.
        var putOnTop = topChooser != null && topChooser(owner);

        if (putOnTop)
        {
            owner.Zones.Library.InsertCardAt(0, card);
        }
        else
        {
            owner.Zones.Library.AddCard(card);
        }
        card.SetZone(ZoneType.Library);
    }
}
