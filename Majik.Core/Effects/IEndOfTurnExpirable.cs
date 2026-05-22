namespace Majik.Core.Effects;

/// <summary>
/// Opt-in marker for replacement effects that should be dropped during
/// the cleanup step (CR 514.2). The bus consults this in
/// <see cref="ReplacementBus.ExpireEndOfTurn"/>; replacements that aren't
/// per-turn (printed static replacements like Shock Land enters-tapped)
/// don't implement this and stay registered.
/// </summary>
public interface IEndOfTurnExpirable
{
    bool ExpiresAtEndOfTurn { get; }
}
