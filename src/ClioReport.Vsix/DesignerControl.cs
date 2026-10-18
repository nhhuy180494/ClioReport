using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ClioReport.Vsix
{
    /// <summary>
    /// WebView2 showing the designer served by <see cref="HostProcess"/>. The file belongs to Visual Studio: the designer posts
    /// plain-text messages ("save\n&lt;json&gt;", "dirty", "clean") and this control reads and writes the file.
    /// </summary>
    internal sealed class DesignerControl : UserControl, IDisposable
    {
        private const string DefaultTitle = "ClioReport Designer";

        private readonly WebView2 _web = new WebView2();
        private readonly TextBlock _message = new TextBlock { Margin = new Thickness(16), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        private string _path;
        private string _pendingPath;
        private bool _started;
        private bool _pageReady;
        private bool _dirty;

        public event Action<string> TitleChanged;

        public DesignerControl()
        {
            var root = new Grid();
            root.Children.Add(_web);
            root.Children.Add(_message);
            Content = root;
            Loaded += (s, e) => { _ = StartAsync(); };   // StartAsync reports its own failures
        }

        public void OpenFile(string path)
        {
            if (string.Equals(_path, path, StringComparison.OrdinalIgnoreCase)) return;   // already open here: keep unsaved edits
            _pendingPath = path;
            _path = path;
            _dirty = false;
            UpdateTitle();
            if (_pageReady) _ = PushFileAsync();
        }

        private async Task StartAsync()
        {
            if (_started) return;
            _started = true;
            try
            {
                var baseUrl = await HostProcess.EnsureStartedAsync();
                PreloadLoader();
                var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClioReport", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(environment);
                _web.CoreWebView2.WebMessageReceived += OnMessage;
                _web.CoreWebView2.NavigationCompleted += (s, e) =>
                {
                    _pageReady = e.IsSuccess;
                    if (_pageReady && _pendingPath != null) _ = PushFileAsync();   // PushFileAsync reports its own failures
                };
                _web.CoreWebView2.Navigate(baseUrl);
            }
            catch (Exception ex)
            {
                _started = false;   // a later activation tries again
                Fail(ex.Message);
            }
        }

        private async Task PushFileAsync()
        {
            var path = _pendingPath;
            _pendingPath = null;
            try
            {
                var text = File.ReadAllText(path, Encoding.UTF8);
                await _web.CoreWebView2.ExecuteScriptAsync(
                    "window.__designer.openFromHost(" + JsLiteral(text) + "," + JsLiteral(Path.GetFileName(path)) + ")");
            }
            catch (Exception ex)
            {
                Fail("Cannot open " + path + ": " + ex.Message);
            }
        }

        private void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string message;
            try { message = e.TryGetWebMessageAsString(); }
            catch (ArgumentException) { return; }

            if (message == "dirty" || message == "clean")
            {
                _dirty = message == "dirty";
                UpdateTitle();
            }
            else if (message.StartsWith("save\n", StringComparison.Ordinal))
            {
                Save(message.Substring(5));
            }
        }

        private void Save(string text)
        {
            try
            {
                if (_path == null)
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "ClioReport report (*.clio)|*.clio|JSON (*.json)|*.json", FileName = "report.clio" };
                    if (dialog.ShowDialog() != true) return;
                    _path = dialog.FileName;
                }
                File.WriteAllText(_path, text, new UTF8Encoding(false));
                _dirty = false;
                UpdateTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot save " + _path + ": " + ex.Message, DefaultTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateTitle()
        {
            var name = _path == null ? DefaultTitle : Path.GetFileName(_path);
            TitleChanged?.Invoke(_dirty ? name + " \u25CF" : name);   // the dot marks unsaved changes
        }

        private void Fail(string text)
        {
            _message.Text = text;
            _message.Visibility = Visibility.Visible;
            _web.Visibility = Visibility.Collapsed;
        }

        // WebView2Loader.dll is a native file; Visual Studio does not look for it next to this extension, so load it by full path
        // once. Later DllImport("WebView2Loader.dll") calls then find the module already loaded.
        private static bool _loaderLoaded;
        private static void PreloadLoader()
        {
            if (_loaderLoaded) return;
            _loaderLoaded = true;
            string arch;
            switch (RuntimeInformation.ProcessArchitecture)
            {
                case Architecture.Arm64: arch = "arm64"; break;
                case Architecture.X86: arch = "x86"; break;
                default: arch = "x64"; break;
            }
            var path = Path.Combine(HostProcess.ExtensionDirectory, "webview2", arch, "WebView2Loader.dll");
            if (File.Exists(path)) LoadLibrary(path);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string fileName);

        private static string JsLiteral(string s)
        {
            var sb = new StringBuilder(s.Length + 16).Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        public void Dispose()
        {
            try { _web.Dispose(); } catch { /* shutting down */ }
        }
    }
}
