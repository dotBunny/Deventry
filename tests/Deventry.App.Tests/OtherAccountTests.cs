using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Auth;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// Asking before a session carries on under another account than the one it was started under (DESIGN.md §4, "Continuing
/// under another account").
/// </summary>
public sealed class OtherAccountTests
{
    private static AuthStatus Signed(string email, string organization) => new(true, "claude.ai", "firstParty", email, organization, "max", null, null);

    private static SessionOrigin Origin(string email, string organization) => new() { Email = email, Organization = organization };

    /// <summary>A tab restored with the session s1, started under <paramref name="origin"/> (null: not recorded), with Me@Work signed in.</summary>
    private static TabViewModel Restore(TabTestHarness h, SessionOrigin? origin, string? accountId = null)
    {
        h.WriteTranscript("s1",
            Wire.Entry("user", "2026-09-28T10:00:00Z", new JsonObject { ["role"] = "user", ["content"] = "Hello" }),
            Wire.Entry("assistant", "2026-09-28T10:00:01Z", Wire.Message(new JsonObject { ["type"] = "text", ["text"] = "Hi" })));
        if (origin is not null)
        {
            h.Services.State.SessionOrigins["s1"] = origin;
        }
        h.Services.RemoteControl.UseAccount(Signed("me@work.com", "Work"));
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, IsPinned = true, SessionId = "s1", AccountId = accountId }];
        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single();
        // Started as its first message would start it (DESIGN.md §9, "Starting fast").
        h.Start(tab);
        return tab;
    }

    [Fact]
    public async Task A_new_session_records_the_account_it_started_under()
    {
        await using var h = new TabTestHarness();
        h.Services.RemoteControl.UseAccount(Signed("Me@Home.com", "Home"));
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "hello";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => h.Services.State.SessionOrigins.ContainsKey("s1"), "the record");

        var origin = h.Services.State.SessionOrigins["s1"];
        Assert.Equal(("Me@Home.com", "Home", null, h.Time.GetUtcNow()), (origin.Email, origin.Organization, origin.AccountId, origin.StartedAt));
    }

    [Fact]
    public async Task A_session_resumed_under_another_account_asks_first_and_sends_nothing()
    {
        await using var h = new TabTestHarness();
        var tab = Restore(h, Origin("me@home.com", "Home"));

        await TabTestHarness.Eventually(() => tab.IsAskingOtherAccount, "the question");

        Assert.Empty(h.Factory.Launches);
        Assert.Equal(TabStatus.NeedsInput, tab.Status);
        Assert.Equal("This conversation was started under the Default account, me@home.com (Home). Continuing it under the Default account, "
            + "me@work.com (Work) sends that account the whole earlier conversation, and its organization may be able to see it. Claude can't use "
            + "its earlier thinking from another organization, so its next replies may be slower and use more tokens.", tab.OtherAccountText);
        Assert.False(tab.CanUseOriginAccount);
    }

    [Fact]
    public async Task The_same_account_in_any_case_and_an_unknown_origin_start_without_asking()
    {
        await using var h = new TabTestHarness();
        var same = Restore(h, Origin("ME@WORK.COM", "work"));
        await TabTestHarness.Eventually(() => same.Status == TabStatus.Idle && same.IsSettled, "the start");
        Assert.False(same.IsAskingOtherAccount);

        await using var other = new TabTestHarness();
        var unknown = Restore(other, null);
        await TabTestHarness.Eventually(() => unknown.Status == TabStatus.Idle && unknown.IsSettled, "the start");
        Assert.False(unknown.IsAskingOtherAccount);
    }

    [Fact]
    public async Task Continue_here_starts_and_the_tab_isnt_asked_again_for_that_account()
    {
        await using var h = new TabTestHarness();
        var tab = Restore(h, Origin("me@home.com", "Home"));
        await TabTestHarness.Eventually(() => tab.IsAskingOtherAccount, "the question");

        await tab.ContinueUnderOtherAccountCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the start");

        Assert.False(tab.IsAskingOtherAccount);
        Assert.Single(h.Factory.Launches);
        Assert.Equal("s1|default:me@work.com|work", tab.State.ConfirmedOtherAccount);

        // Starting again, as a restart does, goes straight through.
        h.Transport.Exit(1);
        await TabTestHarness.Eventually(() => tab.CanRestart, "the exit");
        await tab.RestartCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 2 && tab.Status == TabStatus.Idle && tab.IsSettled, "the restart");
        Assert.False(tab.IsAskingOtherAccount);
    }

    [Fact]
    public async Task Cancel_leaves_the_tab_not_started()
    {
        await using var h = new TabTestHarness();
        var tab = Restore(h, Origin("me@home.com", "Home"));
        await TabTestHarness.Eventually(() => tab.IsAskingOtherAccount, "the question");

        tab.CancelOtherAccountCommand.Execute(null);

        Assert.False(tab.IsAskingOtherAccount);
        Assert.Equal(TabStatus.NotStarted, tab.Status);
        Assert.Empty(h.Factory.Launches);
        Assert.Null(tab.State.ConfirmedOtherAccount);
    }

    [Fact]
    public async Task New_chat_starts_a_session_without_the_earlier_conversation()
    {
        await using var h = new TabTestHarness();
        var tab = Restore(h, Origin("me@home.com", "Home"));
        await TabTestHarness.Eventually(() => tab.IsAskingOtherAccount, "the question");

        await InlineDispatcher.RunAsync(() => tab.NewChatUnderOtherAccountCommand.ExecuteAsync(null));
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1 && tab.Status == TabStatus.Idle && tab.IsSettled, "the new chat");

        Assert.Null(h.Factory.Launches[0].Resume);
        var items = InlineDispatcher.Read(() => tab.Items.ToList());
        Assert.DoesNotContain(items.OfType<UserMessageItem>(), m => m.Text == "Hello");
        Assert.Contains(items.OfType<NoteItem>(), n => n.Text == "A new chat under the Default account, me@work.com (Work). The earlier conversation is in History.");
    }

    [Fact]
    public async Task New_chat_with_a_digest_leaves_it_in_the_composer_of_a_new_session()
    {
        await using var h = new TabTestHarness();
        var tab = Restore(h, Origin("me@home.com", "Home"));
        await TabTestHarness.Eventually(() => tab.IsAskingOtherAccount, "the question");

        await InlineDispatcher.RunAsync(() => tab.NewChatWithDigestCommand.ExecuteAsync(null));
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 1 && tab.Status == TabStatus.Idle && tab.IsSettled, "the new chat");

        Assert.Null(h.Factory.Launches[0].Resume);
        Assert.StartsWith("Context from an earlier conversation (a digest):", tab.ComposerText, StringComparison.Ordinal);
        Assert.Contains("Me: Hello", tab.ComposerText, StringComparison.Ordinal);
        Assert.DoesNotContain(InlineDispatcher.Read(() => tab.Items.OfType<UserMessageItem>().ToList()), m => m.Text == "Hello");
    }

    [Fact]
    public async Task Cancel_puts_a_message_sent_while_it_asked_back_in_the_composer()
    {
        await using var h = new TabTestHarness();
        var tab = Restore(h, Origin("me@home.com", "Home"));
        await TabTestHarness.Eventually(() => tab.IsAskingOtherAccount, "the question");
        tab.ComposerText = "Carry on";
        await InlineDispatcher.RunAsync(() => tab.SendCommand.ExecuteAsync(null));

        tab.CancelOtherAccountCommand.Execute(null);

        Assert.Equal("Carry on", tab.ComposerText);
        Assert.DoesNotContain(InlineDispatcher.Read(() => tab.Items.OfType<UserMessageItem>().ToList()), m => m.Text == "Carry on");
        Assert.Empty(h.Factory.Launches);
    }

    [Fact]
    public async Task With_the_setting_off_the_session_just_starts()
    {
        await using var h = new TabTestHarness(s => s.Accounts.WarnBeforeOtherAccount = false);
        var tab = Restore(h, Origin("me@home.com", "Home"));

        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the start");

        Assert.False(tab.IsAskingOtherAccount);
        Assert.Single(h.Factory.Launches);
    }

    [Fact]
    public async Task A_session_started_under_Default_offers_to_go_back_when_the_tab_moved_to_an_API_key_account()
    {
        await using var h = new TabTestHarness();
        h.Services.Accounts.Credentials = new FakeCredentialStore();
        var work = await h.Services.Accounts.AddApiKeyAccountAsync("Work", "sk-ant-work");
        var tab = Restore(h, Origin("me@work.com", "Work"), work.Id);
        await TabTestHarness.Eventually(() => tab.IsAskingOtherAccount, "the question");

        Assert.Equal("Use the Default account instead", tab.UseOriginAccountText);
        Assert.Contains("Work (an API key)", tab.OtherAccountText, StringComparison.Ordinal);
        await tab.UseOriginAccountCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.IsSettled, "the start");
        Assert.Null(tab.State.AccountId);
        Assert.False(tab.IsAskingOtherAccount);
    }

    [Fact]
    public async Task Moving_the_tab_with_its_account_menu_doesnt_ask_again()
    {
        await using var h = new TabTestHarness();
        h.Services.Accounts.Credentials = new FakeCredentialStore();
        var work = await h.Services.Accounts.AddApiKeyAccountAsync("Work", "sk-ant-work");
        h.Services.RemoteControl.UseAccount(Signed("me@home.com", "Home"));
        var tab = await h.OpenTabAsync();
        h.Transport.EmitTurn();
        // Idle too: a move confirmed while the tab still says it's working waits for a turn end that's gone by.
        await TabTestHarness.Eventually(() => h.Services.State.SessionOrigins.ContainsKey("s1") && tab.Status == TabStatus.Idle && tab.IsSettled, "the turn");

        tab.SwitchAccountCommand.Execute(work.Id);
        await Assert.IsType<ConfirmationViewModel>(h.Shell.Confirmation).ConfirmCommand.ExecuteAsync(null);
        await TabTestHarness.Eventually(() => h.Factory.Launches.Count == 2 && tab.IsSettled || tab.IsAskingOtherAccount, "the restart");

        Assert.False(tab.IsAskingOtherAccount);
        Assert.Equal("s1", h.Factory.Launches[1].Resume);
    }

    [Fact]
    public void The_digest_keeps_prompts_and_answers_and_leaves_out_check_ins()
    {
        var answer = new AssistantTextItem();
        answer.Markdown.Append("Hi there");
        var digest = TabViewModel.ConversationDigest([
            new UserMessageItem("Hello"),
            new UserMessageItem("ping", isCheckIn: true),
            answer,
        ]);
        Assert.Contains("Me: Hello", digest);
        Assert.Contains("Claude: Hi there", digest);
        Assert.DoesNotContain("ping", digest);
        Assert.Equal("", TabViewModel.ConversationDigest([]));
    }
}
