using System.Runtime.InteropServices;

namespace ClaudeUsageTray;

/// <summary>
/// One version of the credentials file, told apart by write time. Changes when Claude Code refreshes the
/// token or logs in again, which is the only thing that can fix a rejected token.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CredentialsVersion(DateTime LastWriteUtc);
