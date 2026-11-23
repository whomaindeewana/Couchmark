using System;
using System.Threading.Tasks;

namespace Couchmark.Extensions;

public static class TaskExtensions
{
    public static async void FireAndForgetSafeAsync(this Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error in fire-and-forget task.");
        }
    }
}
