using BdoComboSetup.Core.Confirmation;

namespace BdoComboSetup.Tests;

/// <summary>The reminder before a write: cancel writes nothing, continue lets it go on, and the flag skips the reminder.</summary>
public sealed class WriteConfirmationTests : IDisposable
{
    private readonly TempLogger _log = new();

    public void Dispose() => _log.Dispose();

    [Fact]
    public async Task Cancel_does_not_let_the_write_go_on()
    {
        var setting = new FakeWriteHintSetting();
        var prompt = new FakeConfirmationPrompt(new WriteHintAnswer(Continue: false, DontShowAgain: false));

        var allowed = await new WriteConfirmation(setting, prompt, _log.Logger).ConfirmAsync();

        Assert.False(allowed);
        Assert.Equal(1, prompt.Shown);
        Assert.False(setting.SkipWriteHint);
    }

    [Fact]
    public async Task Continue_lets_the_write_go_on()
    {
        var prompt = new FakeConfirmationPrompt(new WriteHintAnswer(Continue: true, DontShowAgain: false));

        var allowed = await new WriteConfirmation(new FakeWriteHintSetting(), prompt, _log.Logger).ConfirmAsync();

        Assert.True(allowed);
        Assert.Equal(1, prompt.Shown);
    }

    [Fact]
    public async Task Continue_with_dont_show_again_saves_the_flag()
    {
        var setting = new FakeWriteHintSetting();
        var prompt = new FakeConfirmationPrompt(new WriteHintAnswer(Continue: true, DontShowAgain: true));

        Assert.True(await new WriteConfirmation(setting, prompt, _log.Logger).ConfirmAsync());

        Assert.True(setting.SkipWriteHint);
    }

    [Fact]
    public async Task Cancel_with_dont_show_again_does_not_save_the_flag()
    {
        var setting = new FakeWriteHintSetting();
        var prompt = new FakeConfirmationPrompt(new WriteHintAnswer(Continue: false, DontShowAgain: true));

        Assert.False(await new WriteConfirmation(setting, prompt, _log.Logger).ConfirmAsync());

        Assert.False(setting.SkipWriteHint);
    }

    [Fact]
    public async Task With_the_flag_set_the_reminder_is_not_shown_and_the_write_goes_on()
    {
        var setting = new FakeWriteHintSetting { SkipWriteHint = true };
        var prompt = new FakeConfirmationPrompt(new WriteHintAnswer(Continue: false, DontShowAgain: false));

        var allowed = await new WriteConfirmation(setting, prompt, _log.Logger).ConfirmAsync();

        Assert.True(allowed);
        Assert.Equal(0, prompt.Shown);
    }
}
