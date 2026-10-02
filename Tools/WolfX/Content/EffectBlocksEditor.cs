using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace WolfEx
{
    /// <summary>
    /// The drag-and-drop editor for EffectScript, for people new to it: Blockly (Content\Blocks, shipped with WolfX) in a WebView2.
    /// <see cref="ShowScript"/> turns a script into blocks (parsed here, EffectScriptTree); every change to the blocks comes back as script
    /// text through <see cref="ScriptChanged"/>, so the Script tab and the Compile button work on the same text.
    /// </summary>
    internal sealed class EffectBlocksEditor : UserControl
    {
        private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
        private readonly Label _problem = new()
        {
            Dock = DockStyle.Fill, Visible = false, TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText, Padding = new Padding(20),
        };
        private bool _started, _ready;
        private string? _pending;

        /// <summary>The blocks were changed: this is the script they make.</summary>
        public event Action<string>? ScriptChanged;

        public EffectBlocksEditor()
        {
            Controls.Add(_web);
            Controls.Add(_problem);
            VisibleChanged += async (_, _) =>
            {
                if (Visible && !_started)
                {
                    _started = true;   // started the first time it is shown: WebView2 costs a process
                    await StartAsync();
                }
            };
        }

        private async Task StartAsync()
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Blocks");
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WolfX", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, data);
                await _web.EnsureCoreWebView2Async(environment);
                var core = _web.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.IsStatusBarEnabled = false;
                core.SetVirtualHostNameToFolderMapping("wolfx.blocks", folder, CoreWebView2HostResourceAccessKind.Allow);
                core.WebMessageReceived += (_, e) =>
                {
                    // {type: "ready"} once the page is up, {type: "script", text} for every change made to the blocks
                    var message = System.Text.Json.Nodes.JsonNode.Parse(e.WebMessageAsJson);
                    switch (message?["type"]?.GetValue<string>())
                    {
                        case "ready":
                            _ready = true;
                            if (_pending != null)
                                Send(_pending);
                            _pending = null;
                            break;
                        case "script" when message["text"]?.GetValue<string>() is { } text:
                            ScriptChanged?.Invoke(text);
                            break;
                    }
                };
                _web.Source = new Uri("https://wolfx.blocks/effects.html");
            }
            catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or IOException or UnauthorizedAccessException or InvalidOperationException
                                           or System.Runtime.InteropServices.COMException)
            {
                _web.Visible = false;
                _problem.Visible = true;
                _problem.Text = ex is WebView2RuntimeNotFoundException
                    ? "The block editor needs the Microsoft Edge WebView2 Runtime (Windows 10 and 11 normally have it). " +
                      "Install it from https://developer.microsoft.com/microsoft-edge/webview2/ and reopen WolfX. The Script tab works without it."
                    : "The block editor could not start: " + ex.Message;
            }
        }

        /// <summary>Shows this script as blocks (a script the blocks can't show yet gets a note, and stays as it is until the blocks are changed).</summary>
        public void ShowScript(string script)
        {
            string message = EffectScriptTree.ToJson(script);
            if (_ready)
                Send(message);
            else
                _pending = message;
        }

        private void Send(string message) => _ = _web.ExecuteScriptAsync($"wolf.load({message})");

        public void SetReadOnly(bool readOnly)
        {
            if (_ready)
                _ = _web.ExecuteScriptAsync($"wolf.setReadOnly({(readOnly ? "true" : "false")})");
        }
    }
}
