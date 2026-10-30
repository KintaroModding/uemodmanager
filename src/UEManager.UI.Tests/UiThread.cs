using System.Runtime.ExceptionServices;
using Xunit;

namespace UEManager.UI.Tests;

/// <summary>Runs a check on a thread of its own that WPF accepts, and passes its failure on.</summary>
internal static class UiThread
{
    public static void Run(Action check)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                check();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The check on the WPF thread did not finish.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
