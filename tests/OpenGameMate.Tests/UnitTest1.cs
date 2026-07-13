namespace OpenGameMate.Tests;

public class Phase0UnitTests
{
    [Theory]
    [InlineData("https://chatgpt.com/", true)]
    [InlineData("https://auth.openai.com/", true)]
    [InlineData("https://help.openai.com/", true)]
    [InlineData("http://chatgpt.com/", false)]
    [InlineData("https://chatgpt.com.evil.example/", false)]
    [InlineData("https://example.com/", false)]
    public void OfficialOpenAiUriFilter_IsFailClosed(string value, bool expected)
    {
        Assert.Equal(expected,
            OpenGameMate.Browser.ChatGptBrowserSession.IsOfficialOpenAiUri(new Uri(value)));
    }

    [Theory]
    [InlineData("https://chatgpt.com/")]
    [InlineData("https://login.microsoftonline.com/common/oauth2/v2.0/authorize")]
    [InlineData("https://login.live.com/oauth20_authorize.srf")]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth")]
    [InlineData("https://appleid.apple.com/")]
    [InlineData("https://example.com:444/")]
    public void Phase0DiagnosticNavigation_AllowsAnyHttpsOrigin(string value)
    {
        Assert.True(
            OpenGameMate.Browser.ChatGptBrowserSession.IsAllowedPhase0NavigationUri(new Uri(value)));
    }

    [Theory]
    [InlineData("http://login.microsoftonline.com/")]
    [InlineData("file:///C:/temp/test.html")]
    [InlineData("ftp://example.com/file")]
    public void Phase0DiagnosticNavigation_BlocksNonHttpsOrigins(string value)
    {
        Assert.False(
            OpenGameMate.Browser.ChatGptBrowserSession.IsAllowedPhase0NavigationUri(new Uri(value)));
    }

    [Fact]
    public void RuntimeEvaluateResponseParser_ReadsTopLevelRemoteObjectValue()
    {
        var value = OpenGameMate.Adapters.ChatGptWebAdapter.ParseRuntimeEvaluateResponse(
            """{"result":{"type":"object","value":{"code":"ok","fileSelected":true}}}""");

        Assert.Equal("ok", value.GetProperty("code").GetString());
        Assert.True(value.GetProperty("fileSelected").GetBoolean());
    }

    [Fact]
    public void RuntimeEvaluateResponseParser_FailsClosedOnPageException()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OpenGameMate.Adapters.ChatGptWebAdapter.ParseRuntimeEvaluateResponse(
                """{"result":{"type":"object"},"exceptionDetails":{"text":"failure"}}"""));
    }

    [Fact]
    public void RuntimeEvaluateResponseParser_FailsClosedOnMissingValue()
    {
        Assert.Throws<InvalidOperationException>(() =>
            OpenGameMate.Adapters.ChatGptWebAdapter.ParseRuntimeEvaluateResponse(
                """{"result":{"type":"undefined"}}"""));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void InputPreparationResult_AcceptsFileSelectionOrVerifiedPreview(
        bool fileSelected,
        bool previewDetected,
        bool expected)
    {
        var result = new OpenGameMate.Core.InputPreparationResult(
            fileSelected, previewDetected, true, "test");

        Assert.Equal(expected, result.ImageAdded);
    }

    [Theory]
    [InlineData(3840, 2160, 1920, 1080)]
    [InlineData(2560, 1440, 1920, 1080)]
    [InlineData(3440, 1440, 1920, 804)]
    [InlineData(1280, 720, 1280, 720)]
    [InlineData(1080, 1920, 608, 1080)]
    public void ScreenshotSize_IsBoundedAndPreservesExpectedRatio(
        int sourceWidth,
        int sourceHeight,
        int expectedWidth,
        int expectedHeight)
    {
        var actual = OpenGameMate.Capture.PrimaryDisplayCapture.CalculateOutputSize(
            sourceWidth, sourceHeight, 1920, 1080);

        Assert.Equal((expectedWidth, expectedHeight), actual);
        Assert.True(actual.Width <= 1920);
        Assert.True(actual.Height <= 1080);
    }

    [Fact]
    public void ScreenshotSize_DoesNotUpscaleSmallDisplays()
    {
        Assert.Equal((800, 600),
            OpenGameMate.Capture.PrimaryDisplayCapture.CalculateOutputSize(800, 600, 1920, 1080));
    }

    [Fact]
    public void FocusEvidence_RequiresBothWindowAndCursorToRemainStable()
    {
        var original = new OpenGameMate.Core.FocusSnapshot(101, 5, 400, 300);

        Assert.True(original.SameForegroundAndCursor(
            new OpenGameMate.Core.FocusSnapshot(101, 5, 400, 300)));
        Assert.False(original.SameForegroundAndCursor(
            new OpenGameMate.Core.FocusSnapshot(102, 5, 400, 300)));
        Assert.False(original.SameForegroundAndCursor(
            new OpenGameMate.Core.FocusSnapshot(101, 5, 401, 300)));
    }

    [Fact]
    public void ScreenshotSize_RejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OpenGameMate.Capture.PrimaryDisplayCapture.CalculateOutputSize(0, 1080, 1920, 1080));

    }

    [Fact]
    public void FixedRuntimeDiscovery_ReturnsNullForMissingRoot()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        Assert.Null(OpenGameMate.Browser.ChatGptBrowserSession.FindFixedRuntimeFolder(missingRoot));
    }
}
