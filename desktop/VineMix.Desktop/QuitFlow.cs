using System;
using System.Threading.Tasks;

namespace VineMix.Desktop;

internal enum QuitChoice { Retry, RecoveryCopy, Cancel, Discard }
internal sealed record QuitRecoveryResult(bool Saved, bool Cancelled, string Error);

// The coordinator is independent of windows and engine processes so every
// failure choice, including a failed recovery write, can be checked unattended.
internal static class QuitFlow
{
    internal static async Task<bool> RunAsync(Func<Task<QuitSaveResult>> save,
        Func<QuitSaveResult, Task<QuitChoice>> choose, Func<Task<QuitRecoveryResult>> recovery,
        Action cancel, Func<Task> stop)
    {
        var result = await save();
        while (!result.Saved)
        {
            switch (await choose(result))
            {
                case QuitChoice.Retry:
                    result = await save();
                    break;
                case QuitChoice.RecoveryCopy:
                    var copy = await recovery();
                    if (copy.Saved) { await stop(); return true; }
                    if (!copy.Cancelled) result = result with { Error = "The recovery copy was not saved. " + copy.Error };
                    break;
                case QuitChoice.Discard:
                    await stop(); return true;
                default:
                    cancel(); return false;
            }
        }
        await stop(); return true;
    }
}
