using Deventry.Core.ProjectFiles;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.ProjectFiles;

/// <summary>Open files' unsaved edits kept in the data folder (DESIGN.md §18, "Project files", "Saving").</summary>
public sealed class EditorBackupsTests : IDisposable
{
    private readonly TempFolder _temp = new("deventry-backups");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.Zero));
    private readonly EditorBackups _backups;

    public EditorBackupsTests() => _backups = new EditorBackups(_temp.Combine(EditorBackups.FolderName), _time);

    public void Dispose() => _temp.Dispose();

    private string Project(string name) => _temp.Combine("project", name);

    private string[] BackupFiles => Directory.Exists(_backups.Directory) ? Directory.GetFiles(_backups.Directory) : [];

    [Fact]
    public void The_hash_is_the_texts_sha256_or_says_it_was_missing()
    {
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", EditorBackups.Hash("abc"));
        Assert.Equal(EditorBackups.Hash("abc"), EditorBackups.Hash("abc"));
        Assert.NotEqual(EditorBackups.Hash("a\n"), EditorBackups.Hash("a\r\n"));
        Assert.Equal("missing", EditorBackups.Hash(null));
        Assert.NotEqual(EditorBackups.Hash(null), EditorBackups.Hash(""));
    }

    [Fact]
    public void Unsaved_edits_come_back_with_what_they_were_made_on_and_when()
    {
        var path = Project("Player.cpp");
        var baseHash = EditorBackups.Hash("as read\r\n");

        Assert.True(_backups.Save(path, "my edit\r\nü\t", baseHash));

        var backup = _backups.Load(path);
        Assert.Equal(new EditorBackup(path, "my edit\r\nü\t", baseHash, _time.GetUtcNow()), backup);
        Assert.Single(BackupFiles);
    }

    [Fact]
    public void Saving_again_replaces_the_backup()
    {
        var path = Project("Player.cpp");
        _backups.Save(path, "first", EditorBackups.Hash("a"));
        _time.Advance(TimeSpan.FromSeconds(5));

        _backups.Save(path, "second", EditorBackups.Hash("b"));

        var backup = _backups.Load(path);
        Assert.NotNull(backup);
        Assert.Equal(("second", EditorBackups.Hash("b"), _time.GetUtcNow()), (backup.Text, backup.Base, backup.SavedAt));
        Assert.Single(BackupFiles);
    }

    [Fact]
    public void Each_file_has_its_own_backup()
    {
        _backups.Save(Project("a.txt"), "a", EditorBackups.Hash(null));
        _backups.Save(Project("b.txt"), "b", EditorBackups.Hash(null));

        Assert.Equal("a", _backups.Load(Project("a.txt"))?.Text);
        Assert.Equal("b", _backups.Load(Project("b.txt"))?.Text);
        Assert.Null(_backups.Load(Project("c.txt")));
        Assert.Equal(2, BackupFiles.Length);
    }

    [Fact]
    public void A_path_is_matched_as_the_OS_matches_it()
    {
        _backups.Save(Project("Player.cpp"), "edit", EditorBackups.Hash(null));

        var other = _backups.Load(Project("PLAYER.CPP"));

        if (OperatingSystem.IsLinux())
        {
            Assert.Null(other);
        }
        else
        {
            Assert.Equal("edit", other?.Text);
        }
    }

    [Fact]
    public void Deleting_forgets_the_edits()
    {
        var path = Project("Player.cpp");
        _backups.Save(path, "edit", EditorBackups.Hash(null));

        _backups.Delete(path);
        _backups.Delete(path);

        Assert.Null(_backups.Load(path));
        Assert.Empty(BackupFiles);
    }

    [Fact]
    public void A_backup_that_cant_be_read_reads_as_none()
    {
        var path = Project("Player.cpp");
        _backups.Save(path, "edit", EditorBackups.Hash(null));
        File.WriteAllText(Assert.Single(BackupFiles), "{ not json");

        Assert.Null(_backups.Load(path));
    }

    [Fact]
    public void A_backup_that_cant_be_written_says_so()
    {
        // The folder's name is taken by a file.
        File.WriteAllText(_backups.Directory, "");

        Assert.False(_backups.Save(Project("Player.cpp"), "edit", EditorBackups.Hash(null)));
        Assert.Null(_backups.Load(Project("Player.cpp")));
    }

    [Fact]
    public void Backups_no_open_file_has_used_for_30_days_are_deleted()
    {
        var open = Project("open.txt");
        var unused = Project("unused.txt");
        var recent = Project("recent.txt");
        _backups.Save(open, "open", EditorBackups.Hash(null));
        _backups.Save(unused, "unused", EditorBackups.Hash(null));
        _time.Advance(EditorBackups.KeepUnused);
        _backups.Save(recent, "recent", EditorBackups.Hash(null));

        // Exactly 30 days is still kept.
        _backups.DeleteUnused([open]);
        Assert.NotNull(_backups.Load(unused));

        _time.Advance(TimeSpan.FromSeconds(1));
        _backups.DeleteUnused([open]);

        Assert.Null(_backups.Load(unused));
        Assert.NotNull(_backups.Load(open));
        Assert.NotNull(_backups.Load(recent));
        Assert.Equal(2, BackupFiles.Length);
    }

    [Fact]
    public void Leftovers_that_cant_be_read_are_deleted_and_other_files_left_alone()
    {
        _backups.Save(Project("a.txt"), "a", EditorBackups.Hash(null));
        var garbage = Path.Combine(_backups.Directory, "0123456789ABCDEF0123456789ABCDEF.json");
        File.WriteAllText(garbage, "{ not json");
        var other = Path.Combine(_backups.Directory, "readme.txt");
        File.WriteAllText(other, "not a backup");

        _backups.DeleteUnused([]);

        Assert.False(File.Exists(garbage));
        Assert.True(File.Exists(other));
        Assert.NotNull(_backups.Load(Project("a.txt")));
    }

    [Fact]
    public void Without_any_backups_nothing_happens()
    {
        _backups.DeleteUnused([Project("a.txt")]);

        Assert.False(Directory.Exists(_backups.Directory));
        Assert.Null(_backups.Load(Project("a.txt")));
    }

    /// <summary>
    /// A delete while something reads the backup, a scanner or a <see cref="EditorBackups.Load"/> on another thread, waits
    /// for it rather than leaving edits that were thrown away to come back at launch.
    /// </summary>
    [Fact]
    public async Task Deleting_waits_out_a_backup_held_open_for_a_moment()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to delete an open file.");
        var path = Project("Player.cpp");
        _backups.Save(path, "my edit", EditorBackups.Hash("a"));
        var held = new FileStream(Assert.Single(BackupFiles), FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150, TestContext.Current.CancellationToken);
            await held.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        _backups.Delete(path);
        await release;

        Assert.Empty(BackupFiles);
        Assert.Null(_backups.Load(path));
    }

    [Fact]
    public async Task Reading_waits_out_a_backup_held_open_for_a_moment()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to read a file another has open without sharing it.");
        var path = Project("Player.cpp");
        _backups.Save(path, "my edit", EditorBackups.Hash("a"));
        var held = new FileStream(Assert.Single(BackupFiles), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(150, TestContext.Current.CancellationToken);
            await held.DisposeAsync();
        }, TestContext.Current.CancellationToken);

        var backup = _backups.Load(path);
        await release;

        Assert.Equal("my edit", backup?.Text);
    }
}
