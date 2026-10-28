using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UEManager.Core;
using UEManager.Core.Storage;

namespace UEManager.UI.Services;

/// <summary>
/// One running app per data folder. Two would each keep their own copy of the mod list and
/// overwrite each other's changes, so a second start hands its downloads to the first one and
/// ends. Builds with and without the catalog count as one app when they share a data folder.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    /// <summary>Allows any process to come to the front; see AllowSetForegroundWindow.</summary>
    private const int AnyProcess = -1;

    private readonly FileStream lockFile;
    private readonly string pipeName;
    private readonly CancellationTokenSource stopping = new();

    private SingleInstance(FileStream lockFile, string pipeName)
    {
        this.lockFile = lockFile;
        this.pipeName = pipeName;
    }

    /// <summary>Takes the data folder, or returns null while another app holds it.</summary>
    /// <param name="wait">How long to keep trying, as right after an update, while the old version is still ending.</param>
    public static SingleInstance? TryAcquire(AppPaths paths, TimeSpan wait)
    {
        var deadline = DateTime.UtcNow + wait;
        while (true)
        {
            try
            {
                // The lock lasts as long as the process: Windows closes the file, and deletes it, even after a crash.
                var file = new FileStream(Path.Combine(paths.Root, "instance.lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
                return new SingleInstance(file, PipeName(paths));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                    return null;
                Thread.Sleep(250);
            }
        }
    }

    /// <summary>Hands downloads to the app that holds the data folder, which then comes to the front.</summary>
    /// <returns>False when that app did not answer.</returns>
    public static bool TryHandOver(AppPaths paths, IReadOnlyList<string> downloads)
    {
        try
        {
            // This start was the user's last action, so it may let the other app take the focus.
            AllowSetForegroundWindow(AnyProcess);
            using var pipe = new NamedPipeClientStream(".", PipeName(paths), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(TimeSpan.FromSeconds(3));
            // Relative paths mean nothing to the other process, which has its own current folder.
            pipe.Write(JsonSerializer.SerializeToUtf8Bytes(downloads.Select(Path.GetFullPath).ToArray()));
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Log.Warn($"The app that is already running did not answer: {ex.Message}");
            return false;
        }
    }

    /// <summary>Receives what later starts hand over, until disposed. <paramref name="received"/> runs on a pool thread.</summary>
    public void Listen(Action<IReadOnlyList<string>> received) => _ = Task.Run(async () =>
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stopping.Token);
                using var message = new MemoryStream();
                await pipe.CopyToAsync(message, stopping.Token);
                received(JsonSerializer.Deserialize<string[]>(message.ToArray()) ?? []);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Warn($"A hand-over from another start failed: {ex.Message}");
            }
        }
    });

    public void Dispose()
    {
        stopping.Cancel();
        lockFile.Dispose();
    }

    /// <summary>One pipe per data folder, so test runs with --data-dir stay apart.</summary>
    private static string PipeName(AppPaths paths)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(paths.Root.ToUpperInvariant()));
        return "UEModManager-" + Convert.ToHexString(hash, 0, 8);
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
