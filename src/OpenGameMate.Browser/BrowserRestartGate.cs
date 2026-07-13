namespace OpenGameMate.Browser;

/// <summary>
/// Limits an unexpected browser-window closure to one automatic recovery per
/// user-started session. UI composition decides when and how to reopen.
/// </summary>
public sealed class BrowserRestartGate
{
    private bool _automaticRestartConsumed;

    public bool TryConsumeAutomaticRestart()
    {
        if (_automaticRestartConsumed)
        {
            return false;
        }

        _automaticRestartConsumed = true;
        return true;
    }

    public void ResetForUserStartedSession() => _automaticRestartConsumed = false;
}
