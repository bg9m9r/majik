// Polyfill for record init-only setters when targeting netstandard2.0.
// The Roslyn compiler emits a synthesized `IsExternalInit` reference for
// every `init` setter / positional record; netstandard2.0 has no such
// type, so we declare an internal shim under the conventional namespace.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit { }
