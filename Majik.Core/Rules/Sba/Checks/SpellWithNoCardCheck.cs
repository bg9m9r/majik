namespace Majik.Core.Rules.Sba.Checks;

/// <summary>CR 704.5e — a spell on the stack with no card backing it
/// ceases to exist. Engine-built spells always carry a card, so this is
/// a no-op placeholder until the engine produces detached stack
/// objects.</summary>
public sealed class SpellWithNoCardCheck : IStateBasedActionCheck
{
    public string Name => "SpellWithNoCard";

    public bool Execute(SbaContext ctx) => false;
}
