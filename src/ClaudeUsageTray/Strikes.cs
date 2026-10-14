using System.Runtime.InteropServices;

namespace ClaudeUsageTray;

/// <summary>Consecutive failures per kind; each kind escalates its own backoff ladder.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Strikes(int RateLimit, int Failure);
