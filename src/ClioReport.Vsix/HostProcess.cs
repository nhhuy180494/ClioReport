using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClioReport.Vsix
{
    /// <summary>
    /// Runs the designer host (ClioReport.Designer, .NET 8, shipped in the "Host" folder of the extension) as a child process on a
    /// free loopback port. Visual Studio itself is .NET Framework, so the ClioReport libraries cannot be loaded in-process.
    /// </summary>
    internal static class HostProcess
    {
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static readonly StringBuilder Output = new StringBuilder();
        private static Process _process;
        private static string _url;

        public static string ExtensionDirectory =>
            Path.GetDirectoryName(typeof(HostProcess).Assembly.Location);

        public static async Task<string> EnsureStartedAsync()
        {
            await Gate.WaitAsync().ConfigureAwait(true);
            try
            {
                if (_process != null && !_process.HasExited) return _url;

                var dir = Path.Combine(ExtensionDirectory, "Host");
                var dll = Path.Combine(dir, "ClioReport.Designer.dll");
                if (!File.Exists(dll))
                    throw new FileNotFoundException("The designer host is missing from the extension (" + dll + "). Rebuild it with build-vsix.ps1.");

                _url = "http://127.0.0.1:" + FreePort() + "/";
                Output.Clear();
                var info = new ProcessStartInfo
                {
                    FileName = FindDotnet(),
                    Arguments = "\"" + dll + "\" --urls " + _url,
                    WorkingDirectory = dir,          // the host serves ./wwwroot relative to its content root
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                try { _process = Process.Start(info); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Cannot start 'dotnet'. Install the .NET 8 runtime (https://dotnet.microsoft.com/download/dotnet/8.0). " + ex.Message, ex);
                }
                _process.OutputDataReceived += (s, e) => Append(e.Data);
                _process.ErrorDataReceived += (s, e) => Append(e.Data);
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                AppDomain.CurrentDomain.ProcessExit += (s, e) => Stop();

                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) })
                {
                    for (var i = 0; i < 150; i++)
                    {
                        if (_process.HasExited)
                            throw new InvalidOperationException("The designer host stopped right after starting (is the .NET 8 runtime installed?)." + Environment.NewLine + Tail());
                        try
                        {
                            using (var response = await http.GetAsync(_url).ConfigureAwait(true))
                                return _url;
                        }
                        catch (HttpRequestException) { }
                        catch (TaskCanceledException) { }
                        await Task.Delay(100).ConfigureAwait(true);
                    }
                }
                Stop();
                throw new TimeoutException("The designer host did not answer within 15 seconds." + Environment.NewLine + Tail());
            }
            finally { Gate.Release(); }
        }

        public static void Stop()
        {
            try
            {
                if (_process != null && !_process.HasExited) _process.Kill();
            }
            catch { /* already gone */ }
            _process = null;
        }

        private static void Append(string line)
        {
            if (line == null) return;
            lock (Output) { Output.AppendLine(line); if (Output.Length > 8000) Output.Remove(0, Output.Length - 8000); }
        }

        private static string Tail() { lock (Output) return Output.ToString(); }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try { return ((IPEndPoint)listener.LocalEndpoint).Port; }
            finally { listener.Stop(); }
        }

        private static string FindDotnet()
        {
            foreach (var root in new[] { Environment.GetEnvironmentVariable("DOTNET_ROOT"), Environment.GetEnvironmentVariable("ProgramFiles") + "\\dotnet" })
            {
                if (string.IsNullOrEmpty(root)) continue;
                var candidate = Path.Combine(root, "dotnet.exe");
                if (File.Exists(candidate)) return candidate;
            }
            return "dotnet";
        }
    }
}
