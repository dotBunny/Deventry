using System.Runtime.InteropServices;
using Deventry.Core.Installation;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// Settings → Advanced (DESIGN.md §14): extra arguments for <c>claude</c>, protocol logging (DESIGN.md §13, "Logging"),
/// the data folder, and Diagnostics (DESIGN.md §16, "Staying tolerant at runtime").
/// </summary>
public sealed partial class AdvancedPage : SettingsPage
{
    private readonly ClaudeCodePage _claudeCode;

    /// <param name="claudeCode">Claude Code, whose Claude app settings the copied diagnostics report.</param>
    public AdvancedPage(SettingsContext context, ClaudeCodePage claudeCode) : base(context, SettingsCategory.Advanced)
    {
        _claudeCode = claudeCode;
        Section("claude", "Running claude",
        [
            Setting("advanced.extraArguments", "Extra arguments for every claude process", s => s.Advanced.ExtraArguments, (s, v) => s.Advanced.ExtraArguments = v,
                "Split on spaces and added after Deventry's own. Applies to tabs started after the change.", keywords: ["command line", "flags", "cli"]),
            Setting("advanced.logProtocol", "Log protocol traffic", s => s.Advanced.LogProtocol, (s, v) => s.Advanced.LogProtocol = v,
                "Everything each session sends and receives, to help with a bug report. Kept 7 days.",
                "Logs hold your prompts and file contents. Messages Deventry skips also show in the conversation. Applies to tabs started after the change.",
                ["debug", "logging", "open log folder"]),
            Info("advanced.dataFolder", "Deventry's data folder", "Saved tabs, usage history and logs.", keywords: ["open data folder", "files"]),
        ]);
        Section("diagnostics", "Diagnostics",
        [
            Info("advanced.diagnostics", "What Claude Code sent that Deventry doesn't know",
                "Skipped safely; a count here after a Claude Code update is worth a bug report.",
                keywords: ["copy diagnostics", "unknown messages", "minimum supported claude code version", "bug report", .. ClaudeCodePage.ShowLoginShellSetting ? ["login shell"] : (string[])[]]),
        ]);
    }

    public override string IconKey => "IconToolBash";

    public override string Description => "How Deventry runs claude, its logs and its files, and what it has noticed this run.";

    public string ExtraArguments
    {
        get => Settings.Advanced.ExtraArguments;
        set => Set(value, v => Settings.Advanced.ExtraArguments = v ?? "");
    }

    [RelayCommand]
    private Task OpenDataFolderAsync() => Services.Platform.RevealFolderAsync(Services.Paths.DataDirectory);

    /// <summary>Each session's raw protocol traffic, to the log folder (DESIGN.md §13, "Logging").</summary>
    public bool LogProtocol
    {
        get => Settings.Advanced.LogProtocol;
        set => Set(value, v => Settings.Advanced.LogProtocol = v);
    }

    [RelayCommand]
    private Task OpenLogFolderAsync()
    {
        Directory.CreateDirectory(Services.Paths.ProtocolLogDirectory);
        return Services.Platform.RevealFolderAsync(Services.Paths.ProtocolLogDirectory);
    }

    // ---- Diagnostics (DESIGN.md §16, "Staying tolerant at runtime") -------------------------------------------------

    public string MinimumVersionText => ClaudeLocator.MinimumVersion.ToString();

    public string InstalledVersionText => Services.InstalledClaudeVersion?.ToString() ?? "Not found";

    /// <summary>Whether the login shell's environment is used, which shell and how long it took, or why not. Never values.</summary>
    public string LoginShellText => Services.UserEnvironment.Describe();

    /// <summary>Whether the computer is kept awake for tabs connected to the Claude app, or why not (DESIGN.md §18).</summary>
    public string KeepAwakeText => Services.KeepAwake.Describe();

    /// <summary>Each remote client's state, how many accounts are paired, and why it isn't connected (DESIGN.md §18). Never a token or a message.</summary>
    public string RemoteClientsText => Services.RemoteClients.Describe();

    /// <summary>What Claude Code has sent this run that Deventry doesn't know, in words.</summary>
    public string DiagnosticsText => DiagnosticsReport(includeHeader: false);

    /// <summary><b>Refresh</b>: what the page shows, as it is now.</summary>
    [RelayCommand]
    private void RefreshDiagnostics()
    {
        OnPropertyChanged(nameof(DiagnosticsText));
        OnPropertyChanged(nameof(InstalledVersionText));
        OnPropertyChanged(nameof(LoginShellText));
        OnPropertyChanged(nameof(KeepAwakeText));
        OnPropertyChanged(nameof(RemoteClientsText));
    }

    [RelayCommand]
    private Task CopyDiagnosticsAsync() => Services.Platform.SetClipboardTextAsync(DiagnosticsReport(includeHeader: true));

    /// <summary>The Diagnostics page's contents, and with the header, the report copied for a bug report.</summary>
    internal string DiagnosticsReport(bool includeHeader)
    {
        var snapshot = Services.Diagnostics.Snapshot();
        var lines = new List<string>();
        if (includeHeader)
        {
            lines.Add($"Deventry {Services.Build.Description}");
            lines.Add($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.RuntimeIdentifier})");
            lines.Add($"Claude Code: {InstalledVersionText}{(Services.Install is { } install ? $" at {install.Path}" : "")}");
            lines.Add($"Minimum supported Claude Code: {MinimumVersionText}");
            lines.Add($"Login shell environment: {LoginShellText}");
            lines.Add($"Claude app (Remote Control): new tabs {(_claudeCode.ConnectNewTabsToClaudeApp ? "connect" : "don't connect")}; "
                + (_claudeCode.RemoteControlUnavailableText ?? "available for this account") + ".");
            lines.Add($"Keeping the computer awake: {KeepAwakeText}");
            lines.Add(RemoteClientsText);
            lines.Add($"Protocol logging: {(LogProtocol ? "on" : "off")}");
            lines.Add("");
        }
        lines.Add(snapshot.UnknownMessageCount == 0
            ? "Unknown message types: none"
            : $"Unknown message types ({snapshot.UnknownMessageCount} skipped): " + string.Join(", ", snapshot.UnknownMessageTypes.Select(t => $"{t.Key} ×{t.Value}")));
        lines.Add(snapshot.UnknownFields.Count == 0
            ? "New fields: none"
            : "New fields: " + string.Join(", ", snapshot.UnknownFields.Select(f => $"{f.Key} ×{f.Value}")));
        lines.Add($"Lines that couldn't be read: {snapshot.ParseErrors}");
        return string.Join(Environment.NewLine, lines);
    }
}
