using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Laboratory Maniac (Innistrad, {2}{U}).
///
/// Creature — Human Wizard 2/2. Oracle text:
///   "If you would draw a card while your library has no cards in it,
///    you win the game instead."
///
/// ## Implemented (v1)
/// - Creature — Human Wizard, {2}{U}, 2/2, owner/controller wired.
/// - MV 3 (2 generic + 1 blue).
/// - <b>CR 614.6 replacement effect</b>: while Laboratory Maniac is on the
///   battlefield under its controller's control, any time the controller
///   would draw a card with an empty library, the draw is replaced by
///   "you win the game instead" — implemented as marking every supplied
///   opponent as <see cref="Player.MarkLost"/> so the GameDriver's
///   alive-count gate declares the controller the winner (same pattern as
///   <see cref="DarksteelReactorFactory"/>). The
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> loss flag is NOT
///   stamped, so the SBA-driven loss from CR 704.5b does not fire.
/// - Replacement registered against the controller's
///   <see cref="Player.Replacements"/> bus via
///   <see cref="LaboratoryManiacDrawReplacement"/>. The replacement is
///   permanently registered (not one-shot) and self-gates on
///   (a) the source being on the battlefield,
///   (b) the drawing player matching the controller at intent time, and
///   (c) the controller's library being empty — so the effect is
///   transparent for normal (non-empty-library) draws and while
///   Laboratory Maniac is not on the battlefield. LTB lifts the rider
///   naturally (zone check fails).
///
/// ## Win-the-game modelling
/// Majik models "win the game" as "all opponents lose" (same as
/// <see cref="DarksteelReactorFactory"/>). The replacement marks every
/// opponent supplied at construction time as <see cref="Player.HasLost"/>,
/// which trips the <see cref="Majik.Core.Game.GameDriver"/>'s
/// alive-count gate (CR 104.2a). Opponents may be null or an empty list
/// (shape-only path) — in that posture the win is unobservable but the
/// replacement still cancels the would-be-draw correctly, so the loss
/// flag from CR 704.5b is suppressed.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape-only. No replacement bus wired.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, ReplacementBus?, IReadOnlyList{Player}?)"/>
///   — full wiring: replacement registered + opponents list supplied for
///   win-the-game resolution.
///
/// ## CR alignment
/// - <b>CR 614.6</b>: "If you would draw a card while your library has no
///   cards in it, you win the game instead." This is a replacement effect
///   whose source must be on the battlefield. The replacement intercepts
///   <see cref="DrawCardIntent"/> when the controller's library is empty
///   and substitutes a win (opponents-lose) effect for the draw.
/// - <b>CR 704.5b</b>: A player who tried to draw from an empty library
///   loses the game as an SBA. Laboratory Maniac's replacement fires first
///   (CR 614), cancels the draw, and suppresses the loss flag, so 704.5b
///   never fires.
/// - <b>CR 104.2a</b>: "If a player wins the game, that player wins and
///   all other players lose." Modelled via MarkLost on every opponent.
///
/// ## Deferred (v1 gaps)
/// - <b>Multiple controllers</b>: if control of Laboratory Maniac changes
///   mid-game the replacement still gates on the original owner matching
///   the drawing player. Full controller-change tracking is deferred.
/// - <b>Replacement ordering (CR 616)</b>: when Dredge and Laboratory
///   Maniac both apply to the same draw (empty library after mill), the
///   bus processes in registration order. The correct interaction (Dredge
///   wins when library has ≥ N cards, Maniac fires only on truly empty
///   library) is enforced by Dredge's own CR 702.52b library-count gate,
///   so ordering is benign here.
/// </summary>
[CardName("Laboratory Maniac")]
public static class LaboratoryManiacFactory
{
    public const string CardName = "Laboratory Maniac";
    public const string PrintedManaCost = "{2}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Laboratory Maniac with no live replacement wiring.
    /// Shape-only — suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, opponents: null);

    /// <summary>
    /// Construct Laboratory Maniac with optional runtime services.
    /// When <paramref name="replacements"/> is supplied, a
    /// <see cref="LaboratoryManiacDrawReplacement"/> is registered so the
    /// empty-library draw replacement fires while the card is on the
    /// battlefield. When <paramref name="opponents"/> is supplied,
    /// triggering the win marks every opponent as lost so the game's
    /// single-survivor gate declares the controller the winner.
    /// </summary>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        IReadOnlyList<Player>? opponents)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null)
        {
            var replacement = new LaboratoryManiacDrawReplacement(card, opponents);
            replacements.Register(replacement);
        }

        return card;
    }
}

/// <summary>
/// CR 614.6 replacement for Laboratory Maniac's empty-library win clause.
///
/// Applies when:
///   - Laboratory Maniac is on the battlefield (zone check, CR 614.6).
///   - The drawing player is Laboratory Maniac's controller (the "your
///     library" printed clause).
///   - The controller's library is empty (the "has no cards in it" gate).
///
/// On match: marks every supplied opponent as
/// <see cref="Player.MarkLost"/> (CR 104.2a win = all opponents lose),
/// then returns null to cancel the underlying draw so
/// <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> is never called
/// (CR 704.5b loss suppressed).
/// </summary>
public sealed class LaboratoryManiacDrawReplacement : IReplacementEffect<DrawCardIntent>
{
    private readonly Creature _source;
    private readonly IReadOnlyList<Player>? _opponents;

    public LaboratoryManiacDrawReplacement(
        Creature source,
        IReadOnlyList<Player>? opponents)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _opponents = opponents;
    }

    public bool OneShot => false;
    public object? Tag => this;

    public bool Applies(DrawCardIntent intent, IReadOnlyList<object> history)
    {
        // CR 614.6 — only active while source is on the battlefield.
        if (_source.Zone != ZoneType.Battlefield) return false;

        // "your library" — controller at intent time.
        var controller = _source.Controller;
        if (controller is null) return false;
        if (!ReferenceEquals(intent.Player, controller)) return false;

        // "while your library has no cards in it" — empty at intent time.
        if (controller.Zones.Library.Count > 0) return false;

        return true;
    }

    public DrawCardIntent? Replace(DrawCardIntent intent, IReadOnlyList<object> history)
    {
        // CR 104.2a — win the game: mark every opponent as lost. The
        // GameDriver's alive-count gate then declares the controller the
        // winner. With no opponents supplied (shape path) the win is
        // unobservable but the draw is still cancelled.
        var controller = _source.Controller;
        if (_opponents != null && controller != null)
        {
            foreach (var opp in _opponents)
            {
                if (opp is null) continue;
                if (ReferenceEquals(opp, controller)) continue;
                if (opp.HasLost) continue;
                opp.MarkLost();
            }
        }

        // Return null to cancel the underlying draw (CR 614 — replacement
        // can cancel the event). MarkTriedToDrawFromEmptyLibrary is never
        // called, so CR 704.5b loss flag is suppressed.
        return null;
    }
}
