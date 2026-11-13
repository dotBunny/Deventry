using System.Runtime.CompilerServices;
using System.Text.Json;
using Deventry.App.Services;
using Deventry.Core.Settings;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// One page of the Settings window (DESIGN.md §14): a category, or one of the selected tab's project pages. It declares
/// its settings (<see cref="SettingItem"/>) and sections (<see cref="SettingsGroup"/>), which its view's rows find by
/// key, and which give the search box its entries and each section its <b>Reset to defaults</b>. Pages share a
/// <see cref="SettingsContext"/>.
/// </summary>
public abstract class SettingsPage : ViewModelBase, ISettingsSource
{
    private readonly Dictionary<string, SettingItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SettingsGroup> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<SettingItem, SettingsGroup> _groupOf = [];

    protected SettingsPage(SettingsContext context, string title)
    {
        Context = context;
        Title = title;
        context.AllPagesChanged += () =>
        {
            OnPropertyChanged(string.Empty);
            RefreshItems();
        };
        context.SettingsSaved += RefreshItems;
    }

    /// <summary>The page's name in the sidebar: one of <see cref="SettingsCategory"/>, or a project page's.</summary>
    public string Title { get; }

    /// <summary>A line under the page's title, saying what's on it.</summary>
    public virtual string? Description => null;

    /// <summary>Its icon in the sidebar: the key of one of App.axaml's small vector icons.</summary>
    public abstract string IconKey { get; }

    /// <summary>A word after its name in the sidebar, such as <i>Off</i> for a project type switched off; null for none.</summary>
    public virtual string? NavTag => null;

    /// <summary>The tag says the page needs the user, such as a remote client stopped: it's in the caution colors.</summary>
    public virtual bool IsNavTagCaution => false;

    protected SettingsContext Context { get; }

    protected AppServices Services => Context.Services;

    protected AppSettings Settings => Context.Settings;

    /// <summary>
    /// Everything on the page is kept on this computer (General, Claude Code, Perforce…): the page says so once, by its
    /// title, rather than each section.
    /// </summary>
    public bool IsMachineOnly => _items.Values.Any(i => i.IsSetting) && _groups.Values.Where(g => g.Items.Any(i => i.IsSetting)).All(g => g.IsMachineOnly);

    public IReadOnlyCollection<SettingItem> Items => _items.Values;

    public IReadOnlyCollection<SettingsGroup> Groups => _groups.Values;

    public SettingItem? FindSetting(string key) => _items.GetValueOrDefault(key);

    public SettingsGroup? FindGroup(string key) => _groups.GetValueOrDefault(key);

    /// <summary>The section a setting is in, for where search says it is.</summary>
    public SettingsGroup? GroupOf(SettingItem item) => _groupOf.GetValueOrDefault(item);

    // ---- Search (DESIGN.md §14, "Search") ---------------------------------------------------------------------------

    /// <summary>
    /// The page's entries for the search box: each section and each setting, with the words they're found by. Each
    /// names a row or section of the page's view by its key, which the window scrolls to (SettingsWindowTests checks
    /// that the view has one for each). A section with a row of its own name, such as Quick suffixes, is found as the
    /// row, which has more words to be found by, rather than twice.
    /// </summary>
    public virtual IEnumerable<SettingsSearchResult> SearchEntries =>
    [
        .. _groups.Values.Where(g => g.Items.All(i => i.Title != g.Title))
            .Select(g => Entry(g.Key, g.Title, g.Description, null, [g.Title, g.Description ?? ""])),
        .. _items.Values.Select(i => Entry(i.Key, i.Title, i.Description, GroupOf(i)?.Title, i.SearchText)),
    ];

    protected virtual SettingsSearchResult Entry(string key, string label, string? description, string? section, IEnumerable<string> words) =>
        new(Title, label) { Key = key, Description = description, Section = section, Words = [.. words] };

    // ---- Declaring settings and sections -----------------------------------------------------------------------------

    /// <summary>
    /// A setting kept in <c>settings.json</c> at <paramref name="key"/>: it's at its default while it reads as a new
    /// <see cref="AppSettings"/> does, and goes back by writing that.
    /// </summary>
    protected SettingItem Setting<T>(string key, string title, Func<AppSettings, T> read, Action<AppSettings, T> write,
        string? description = null, string? more = null, IReadOnlyList<string>? keywords = null, string? overrideKey = null, bool resetsWithSection = true) =>
        Add(new SettingItem(key, title, () => Same(read(Settings), read(new AppSettings())), () => write(Settings, read(new AppSettings())))
        {
            Description = description,
            More = more,
            Keywords = keywords ?? [],
            OverrideKey = overrideKey,
            ResetsWithSection = resetsWithSection,
        });

    /// <summary>
    /// A setting with its own default and way back, such as one kept with this machine's state; without them, one that's
    /// never reset, such as the library folder.
    /// </summary>
    protected SettingItem Setting(string key, string title, Func<bool>? isDefault, Action? reset,
        string? description = null, string? more = null, IReadOnlyList<string>? keywords = null, string? overrideKey = null, bool resetsWithSection = true,
        bool? machineOnly = null)
    {
        return Add(new SettingItem(key, title, isDefault, reset)
        {
            Description = description,
            More = more,
            Keywords = keywords ?? [],
            OverrideKey = overrideKey,
            ResetsWithSection = resetsWithSection,
            IsMachineOnly = machineOnly ?? !SyncedSettings.Syncs(key),
        });
    }

    /// <summary>A row with no setting behind it, such as an action or what something is now, that search still finds.</summary>
    protected SettingItem Info(string key, string title, string? description = null, string? more = null, IReadOnlyList<string>? keywords = null) =>
        Add(new SettingItem(key, title) { Description = description, More = more, Keywords = keywords ?? [], IsSetting = false });

    /// <summary>A section of the page, with its settings in the order the view shows them.</summary>
    /// <param name="also">What else its <b>Reset to defaults</b> does, such as reading a list's rows again.</param>
    /// <param name="confirm">What its <b>Reset to defaults</b> asks first, when it loses the user's own entries.</param>
    protected SettingsGroup Section(string key, string title, IReadOnlyList<SettingItem> items, string? description = null, Action? also = null, string? confirm = null)
    {
        var group = new SettingsGroup(this, key, title, items, also) { Description = description, ConfirmText = confirm };
        _groups.Add(key, group);
        foreach (var item in items)
        {
            _groupOf[item] = group;
            if (confirm is not null)
            {
                item.AsksVia = group;
            }
        }
        group.Refresh();
        return group;
    }

    private SettingItem Add(SettingItem item)
    {
        _items.Add(item.Key, item);
        item.AttachTo(this);
        item.Refresh(Context.TabsWithOwn);
        return item;
    }

    /// <summary>Equal values, or lists and records that serialize the same.</summary>
    private static bool Same<T>(T a, T b) =>
        EqualityComparer<T>.Default.Equals(a, b)
        || JsonSerializer.Serialize(a, JsonFileStore<AppSettings>.Options) == JsonSerializer.Serialize(b, JsonFileStore<AppSettings>.Options);

    // ---- Resetting (DESIGN.md §14: each section has its own Reset to defaults) ---------------------------------------

    public void Reset(IReadOnlyList<SettingItem> items, Action? also = null)
    {
        foreach (var item in items)
        {
            item.ResetValue?.Invoke();
        }
        also?.Invoke();
        Save();
        OnReset();
        // Everything the page shows reads its value again, so none can be left showing what it was.
        OnPropertyChanged(string.Empty);
        RefreshItems();
    }

    /// <summary>Every section's <b>Reset to defaults</b> at once, without the questions some ask: for tests.</summary>
    internal void ResetAllSections() => Reset([.. _items.Values.Where(i => i.CanReset && i.ResetsWithSection && i.IsChanged)]);

    /// <summary>After a reset: anything the page holds besides its properties, such as a list's rows, reads again.</summary>
    protected virtual void OnReset()
    {
    }

    /// <summary>Each setting reads whether it's at its default again, and each section whether it has changes.</summary>
    public void RefreshItems()
    {
        foreach (var item in _items.Values)
        {
            item.Refresh(Context.TabsWithOwn);
        }
        var pageSays = IsMachineOnly;
        foreach (var group in _groups.Values)
        {
            group.Refresh();
            group.ShowsMachineMark = group.IsMachineOnly && !pageSays;
        }
    }

    /// <summary>The window shows the page: one that shows something slow to read, such as the library's size, reads it now.</summary>
    public virtual void OnShown()
    {
    }

    protected void Save() => Context.Save();

    /// <summary>Changes a setting from its property: applies it, says the property changed, and saves.</summary>
    protected void Set<T>(T value, Action<T> apply, [CallerMemberName] string? property = null)
    {
        apply(value);
        OnPropertyChanged(property);
        Save();
    }

    /// <summary>
    /// Saves this machine's state after a change to something kept there, such as the detailed usage header, and reads
    /// the settings' marks again (state isn't settings, so nothing else says it changed).
    /// </summary>
    protected void SaveState()
    {
        Services.SaveState();
        RefreshItems();
    }
}

/// <summary>A retention option in a dropdown.</summary>
public sealed record RetentionChoice(RetentionPeriod Period)
{
    public override string ToString() => Period.Label();
}
