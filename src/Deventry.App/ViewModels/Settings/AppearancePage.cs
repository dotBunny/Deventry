using Deventry.Core.Settings;

namespace Deventry.App.ViewModels.Settings;

/// <summary>A Style choice in Settings → Appearance (DESIGN.md §3, "Visual style").</summary>
public sealed record StyleOption(AppStyle Style, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Settings → Appearance → Motion's choices (DESIGN.md §3, "Accessibility").</summary>
public sealed record MotionChoice(MotionSetting Setting)
{
    public override string ToString() => Setting switch
    {
        MotionSetting.Reduce => "Reduce motion",
        MotionSetting.Full => "Don't reduce",
        _ => "Follow the system",
    };
}

/// <summary>Settings → Appearance → Tab activity's choices (DESIGN.md §4, "Sidebar").</summary>
public sealed record TabActivityChoice(TabActivity Activity)
{
    public override string ToString() => Activity == TabActivity.Bars ? "Colored rows" : "Status dots";
}

/// <summary>Joulene's sizes in Settings → Appearance (DESIGN.md §5, "Joulene on the composer").</summary>
public sealed record JouleneSizeChoice(JouleneSize Size)
{
    public override string ToString() => Size switch
    {
        JouleneSize.Small => "Small",
        JouleneSize.Large => "Large",
        _ => "Medium",
    };
}

/// <summary>How often Joulene does something, in Settings → Appearance (DESIGN.md §5, "Joulene on the composer").</summary>
public sealed record JouleneLivelinessChoice(JouleneLiveliness Liveliness)
{
    public override string ToString() => Liveliness == JouleneLiveliness.Calm ? "Calm: every 30 to 90 seconds" : "Lively: every 10 to 30 seconds";
}

/// <summary>Settings → Appearance → Zoom's steps (DESIGN.md §3, "Accessibility").</summary>
public sealed record ZoomChoice(int Percent)
{
    public override string ToString() => $"{Percent}%";
}

/// <summary>
/// Settings → Appearance (DESIGN.md §14): the preview, theme, style, density and width, the fonts, zoom and motion, and
/// Joulene on the composer with her games.
/// </summary>
public sealed class AppearancePage : SettingsPage
{
    public AppearancePage(SettingsContext context) : base(context, SettingsCategory.Appearance)
    {
        Section("look", "Look",
        [
            Setting("appearance.theme", "Theme", s => s.Appearance.Theme, (s, v) => s.Appearance.Theme = v,
                "Light, dark, or as your computer is.", keywords: ["dark mode", "light mode", "colors"]),
            Setting("appearance.style", "Style", s => s.Appearance.Style, (s, v) => s.Appearance.Style = v,
                "Deventry's own look, or the Claude apps'.",
                "Claude looks like the Claude apps: ivory and warm greys, Claude's orange, rounder cards and serif replies. Standard uses neutral greys and your computer's accent color.",
                ["claude style", "standard", "look", "theme"]),
            Setting("appearance.density", "Density", s => s.Appearance.Density, (s, v) => s.Appearance.Density = v,
                "Compact tightens the conversation, the sidebar's rows and the message box.", keywords: ["compact", "comfortable", "spacing"]),
            Setting("appearance.fullWidthConversation", "Full-width conversation", s => s.Appearance.FullWidthConversation, (s, v) => s.Appearance.FullWidthConversation = v,
                "Fill the tab's width, rather than stopping at a width that's easy to read.", keywords: ["wide", "narrow", "max width"]),
            Setting("appearance.systemTitleBar", "Use the system's title bar", s => s.Appearance.SystemTitleBar, (s, v) => s.Appearance.SystemTitleBar = v,
                "Your system's title bar on each window, rather than Deventry's own.",
                "Deventry's own: the main window's header is its title bar, and its other windows have one in the same style. Drag a title bar's gaps to move the window, and double-click them to maximize it. Linux always has the system's.",
                ["title bar", "window frame", "caption"]),
        ]);
        Section("text", "Text",
        [
            Setting("appearance.conversationFont", "Conversation font", s => s.Appearance.ConversationFont, (s, v) => s.Appearance.ConversationFont = v,
                "Empty uses Deventry's own; one that isn't installed falls back to it.", keywords: ["typeface", "font family"]),
            Setting("appearance.conversationFontSize", "Conversation font size", s => s.Appearance.ConversationFontSize, (s, v) => s.Appearance.ConversationFontSize = v,
                keywords: ["text size", "bigger", "smaller"]),
            Setting("appearance.codeFont", "Code font", s => s.Appearance.CodeFont, (s, v) => s.Appearance.CodeFont = v,
                "Empty uses Cascadia Mono, Consolas or Menlo.", keywords: ["monospace", "typeface"]),
            Setting("appearance.codeFontSize", "Code font size", s => s.Appearance.CodeFontSize, (s, v) => s.Appearance.CodeFontSize = v,
                keywords: ["monospace", "text size"]),
        ]);
        Section("accessibility", "Accessibility",
        [
            Setting("appearance.zoom", "Zoom", s => s.Appearance.Zoom, (s, v) => s.Appearance.Zoom = v,
                "Everything in the main window, bigger or smaller.",
                "Ctrl and + or −, or Ctrl+0, change it too (Cmd on a Mac). Each computer keeps its own, to suit its screen.",
                ["scale", "bigger", "magnify"]),
            Setting("appearance.tabActivity", "Tab activity", s => s.Appearance.TabActivity, (s, v) => s.Appearance.TabActivity = v,
                "How the sidebar shows that Claude is working in a tab, or waiting for you.",
                "Colored rows: no status icons; a soft highlight runs across the row while Claude works, the row flashes orange while it waits for your answer, a pale green wash marks a tab it finished in while you weren't looking, until you open it, and a red one a tab that stopped with an error. A tab's name shimmers while its session starts. Status dots: the dot before the tab's name pulses while Claude works.",
                ["working", "busy", "waiting", "progress", "bar", "dot", "sidebar", "activity"]),
            Setting("appearance.motion", "Motion", s => s.Appearance.Motion, (s, v) => s.Appearance.Motion = v,
                "Whether the busy dots pulse, the working line twinkles and the icon moves.",
                "Following the system, motion is reduced when your computer's settings, or Claude Code's prefersReducedMotion, ask for it. Joulene on the composer stands still then too.",
                ["reduce motion", "animation", "accessibility"]),
        ]);
        Section("joulene", "Joulene",
        [
            Setting("appearance.showJoulene", "Show Joulene on the composer", s => s.Appearance.ShowJoulene, (s, v) => s.Appearance.ShowJoulene = v,
                "She stands on the message box and does little things every so often.",
                "She walks about, dances, juggles, sits on the edge, and sometimes falls off behind it. While Claude works she works too, with whatever suits what it's doing, and she points out other tabs that need you. Click her to startle her, drag her about, or right-click to hide her. She stands still when motion is reduced.",
                ["mascot", "character"]),
            Setting("appearance.jouleneSize", "Size", s => s.Appearance.JouleneSize, (s, v) => s.Appearance.JouleneSize = v, keywords: ["joulene", "mascot"]),
            Setting("appearance.jouleneLiveliness", "How often", s => s.Appearance.JouleneLiveliness, (s, v) => s.Appearance.JouleneLiveliness = v,
                "How often she does something.", keywords: ["joulene", "mascot", "calm", "lively"]),
            Setting("appearance.jouleneTips", "Shares tips", s => s.Appearance.JouleneTips, (s, v) => s.Appearance.JouleneTips = v,
                "Now and then she holds up a tip, such as a shortcut you might not know.",
                "At most every ten minutes, and never while you're typing.", ["joulene", "hints"]),
            Setting("appearance.gamesWithJoulene", "Games with Joulene", s => s.Appearance.GamesWithJoulene, (s, v) => s.Appearance.GamesWithJoulene = v,
                "Tic-tac-toe, Four in a Row, hangman, Minesweeper or Pipeline against her while Claude works.",
                "Or while a usage limit holds a task. Start one by right-clicking her, from the command palette, or from the usage limit bar; it opens in the Games tool window. She plays whether or not she's on the composer.",
                ["play", "tic-tac-toe", "four in a row", "hangman", "minesweeper", "pipeline", "pipes"]),
        ]);
        context.SettingsSaved += OnPreviewSettingsSaved;
        context.AllPagesChanged += OnPreviewSettingsSaved;
    }

    public override string IconKey => "IconSettingsAppearance";

    // ---- The preview (DESIGN.md §14, "Appearance"): a little of a tab, as these settings draw it ------------------------

    /// <summary>The preview's cards and message box are drawn compact.</summary>
    public bool IsCompact => Settings.Appearance.Density == Density.Compact;

    /// <summary>The preview's in-between steps are quieter, as Settings → Conversation says.</summary>
    public bool QuietSteps => Settings.Appearance.QuietSteps;

    /// <summary>The preview's message starts further in, as Settings → Conversation says.</summary>
    public bool IndentYou => Settings.Appearance.IndentYourMessages;

    /// <summary>The preview's thinking starts a little in, as Settings → Conversation says.</summary>
    public bool IndentSteps => Settings.Appearance.IndentClaudesSteps;

    /// <summary>The preview drawn at the main window's zoom.</summary>
    public double ZoomScale => Settings.Appearance.Zoom / 100.0;

    /// <summary>Screen pixels in a cell of Joulene's sprite, for her size in the preview.</summary>
    public double JouleneCell => Settings.Appearance.JouleneSize switch
    {
        Core.Settings.JouleneSize.Small => 2,
        Core.Settings.JouleneSize.Large => 4,
        _ => 3,
    };

    /// <summary>A setting saved anywhere (Conversation's quieter steps, the zoom keys) shows in the preview.</summary>
    private void OnPreviewSettingsSaved()
    {
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(QuietSteps));
        OnPropertyChanged(nameof(IndentYou));
        OnPropertyChanged(nameof(IndentSteps));
        OnPropertyChanged(nameof(ZoomScale));
        OnPropertyChanged(nameof(JouleneCell));
    }

    public override string Description => "How Deventry looks: in every tab, and on this computer's screen.";

    public IReadOnlyList<ThemeChoice> Themes { get; } = [ThemeChoice.System, ThemeChoice.Light, ThemeChoice.Dark];

    public ThemeChoice Theme
    {
        get => Settings.Appearance.Theme;
        set => Set(value, v => Settings.Appearance.Theme = v);
    }

    /// <summary>Settings → Appearance → Style (DESIGN.md §3, "Visual style"), named as the list shows them.</summary>
    public IReadOnlyList<StyleOption> StyleOptions { get; } =
        [new(AppStyle.Standard, "Standard"), new(AppStyle.Claude, "Claude")];

    public StyleOption Style
    {
        get => StyleOptions.FirstOrDefault(o => o.Style == Settings.Appearance.Style) ?? StyleOptions[0];
        set => Set(value, v => Settings.Appearance.Style = v?.Style ?? AppStyle.Standard);
    }

    public IReadOnlyList<Density> Densities { get; } = [Density.Comfortable, Density.Compact];

    /// <summary>Compact tightens the conversation, the sidebar's rows and the composer (DESIGN.md §14).</summary>
    public Density Density
    {
        get => Settings.Appearance.Density;
        set => Set(value, v => Settings.Appearance.Density = v);
    }

    /// <summary>The conversation and the composer fill the tab's width rather than stopping at a readable one (DESIGN.md §14).</summary>
    public bool FullWidthConversation
    {
        get => Settings.Appearance.FullWidthConversation;
        set => Set(value, v => Settings.Appearance.FullWidthConversation = v);
    }

    public bool SystemTitleBar
    {
        get => Settings.Appearance.SystemTitleBar;
        set => Set(value, v => Settings.Appearance.SystemTitleBar = v);
    }

    /// <summary>Linux always has the system's title bar, so the switch is only for Windows and macOS.</summary>
    public bool CanMergeTitleBar => Context.Services.Platform.CanMergeTitleBar;

    // ---- Fonts (DESIGN.md §14, "Appearance") ----------------------------------------------------------------------

    /// <summary>The fonts installed here, to pick from; any other name can be typed.</summary>
    public IReadOnlyList<string> InstalledFonts => field ??= Services.Platform.InstalledFonts();

    /// <summary>Empty for Deventry's own font.</summary>
    public string ConversationFont
    {
        get => Settings.Appearance.ConversationFont ?? "";
        set => Set(value, v => Settings.Appearance.ConversationFont = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    public decimal? ConversationFontSize
    {
        get => (decimal)Settings.Appearance.ConversationFontSize;
        set => Set(value, v => Settings.Appearance.ConversationFontSize = Math.Clamp((double)(v ?? 14), 9, 28));
    }

    /// <summary>Empty for the default monospace fonts.</summary>
    public string CodeFont
    {
        get => Settings.Appearance.CodeFont ?? "";
        set => Set(value, v => Settings.Appearance.CodeFont = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    public decimal? CodeFontSize
    {
        get => (decimal)Settings.Appearance.CodeFontSize;
        set => Set(value, v => Settings.Appearance.CodeFontSize = Math.Clamp((double)(v ?? 13), 8, 28));
    }

    // ---- Accessibility (DESIGN.md §3): the main window's scale, and whether anything moves -----------------------

    public IReadOnlyList<ZoomChoice> ZoomChoices { get; } = [.. Core.Accessibility.Zoom.Steps.Select(percent => new ZoomChoice(percent))];

    /// <summary>The main window's content, scaled (DESIGN.md §3, "Accessibility"); Ctrl/Cmd +, − and 0 change it too.</summary>
    public ZoomChoice Zoom
    {
        get => ZoomChoices.FirstOrDefault(c => c.Percent == Settings.Appearance.Zoom) ?? new ZoomChoice(Settings.Appearance.Zoom);
        set => Set(value?.Percent ?? Core.Accessibility.Zoom.Default, v => Settings.Appearance.Zoom = v);
    }

    public IReadOnlyList<TabActivityChoice> TabActivityChoices { get; } = [new(Core.Settings.TabActivity.Bars), new(Core.Settings.TabActivity.Dots)];

    /// <summary>Whether the sidebar shows a working or waiting tab with its row's colors or a status dot (DESIGN.md §4, "Sidebar").</summary>
    public TabActivityChoice TabActivity
    {
        get => TabActivityChoices.FirstOrDefault(c => c.Activity == Settings.Appearance.TabActivity) ?? TabActivityChoices[0];
        set => Set(value?.Activity ?? Core.Settings.TabActivity.Bars, v => Settings.Appearance.TabActivity = v);
    }

    public IReadOnlyList<MotionChoice> MotionChoices { get; } =
        [new(MotionSetting.System), new(MotionSetting.Reduce), new(MotionSetting.Full)];

    /// <summary>Whether the busy dots pulse, the working glyph twinkles and the taskbar or Dock icon moves.</summary>
    public MotionChoice Motion
    {
        get => MotionChoices.FirstOrDefault(c => c.Setting == Settings.Appearance.Motion) ?? MotionChoices[0];
        set => Set(value?.Setting ?? MotionSetting.System, v => Settings.Appearance.Motion = v);
    }

    // ---- Joulene on the composer (DESIGN.md §5) -----------------------------------------------------------------

    /// <summary>Joulene standing on the composer, doing little things now and then (DESIGN.md §5, "Joulene on the composer").</summary>
    public bool ShowJoulene
    {
        get => Settings.Appearance.ShowJoulene;
        set => Set(value, v => Settings.Appearance.ShowJoulene = v);
    }

    public IReadOnlyList<JouleneSizeChoice> JouleneSizes { get; } =
        [new(Core.Settings.JouleneSize.Small), new(Core.Settings.JouleneSize.Medium), new(Core.Settings.JouleneSize.Large)];

    /// <summary>How big she is: 24, 36 or 48 pixels tall at 100%.</summary>
    public JouleneSizeChoice JouleneSize
    {
        get => JouleneSizes.FirstOrDefault(c => c.Size == Settings.Appearance.JouleneSize) ?? JouleneSizes[1];
        set => Set(value?.Size ?? Core.Settings.JouleneSize.Medium, v => Settings.Appearance.JouleneSize = v);
    }

    public IReadOnlyList<JouleneLivelinessChoice> JouleneLivelinesses { get; } =
        [new(Core.Settings.JouleneLiveliness.Lively), new(Core.Settings.JouleneLiveliness.Calm)];

    /// <summary>How often she does something.</summary>
    public JouleneLivelinessChoice JouleneLiveliness
    {
        get => JouleneLivelinesses.FirstOrDefault(c => c.Liveliness == Settings.Appearance.JouleneLiveliness) ?? JouleneLivelinesses[0];
        set => Set(value?.Liveliness ?? Core.Settings.JouleneLiveliness.Lively, v => Settings.Appearance.JouleneLiveliness = v);
    }

    /// <summary>She shares a tip now and then, such as a shortcut.</summary>
    public bool JouleneTips
    {
        get => Settings.Appearance.JouleneTips;
        set => Set(value, v => Settings.Appearance.JouleneTips = v);
    }

    /// <summary>Tic-tac-toe, Four in a Row, hangman, Minesweeper and Pipeline against her (DESIGN.md §18, "Games with Joulene").</summary>
    public bool GamesWithJoulene
    {
        get => Settings.Appearance.GamesWithJoulene;
        set => Set(value, v => Settings.Appearance.GamesWithJoulene = v);
    }
}
