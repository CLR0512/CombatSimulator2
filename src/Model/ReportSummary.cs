namespace MelvorMods.CombatSimulator2.Model;

/// <summary>A bug report in five lines, in English: what a player pastes or a developer reads first.</summary>
/// <param name="LastError">The newest failure: its kind, reason and phase; null when there was none.</param>
/// <param name="ReportFile">The zip's name, without its folder.</param>
internal sealed record ReportSummary(
    string Cs2Version,
    string GameBuild,
    string? LastRunId,
    string? LastRunState,
    string? LastError,
    string ReportFile
);

/// <summary>A saved report: its file and folder, size, what is in it, and its summary as written (redacted).</summary>
internal sealed record ReportResult(
    string File,
    long Bytes,
    IReadOnlyList<string> Entries,
    ReportSummary Summary,
    string Folder
);
