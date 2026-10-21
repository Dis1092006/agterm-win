using System.Text;
using System.Text.Json;
using AgTerm.App;
using AgTerm.Xterm;

namespace AgTerm.Xterm.Tests;

/// <summary>
/// The readline marker against the REAL page. integration.ps1 reports with OSC 7777 when
/// PSReadLine reads input and which key runs its Paste function; while it does, the pane sends
/// that key instead of pasting through the app, so PSReadLine inserts the clip as one undo step.
/// </summary>
[Collection("webview2")]
public class XtermPageReadlineLiveTests
{
    private const string Win32Mode = "\u001b[?9001h";
    private const string ShiftInsertPaste = "\u001b]7777;agterm-readline=1;paste=shift-insert\u0007";
    private const string CtrlVPaste = "\u001b]7777;agterm-readline=1;paste=ctrl-v\u0007";
    private const string NoPasteKey = "\u001b]7777;agterm-readline=1\u0007";
    private const string NotReading = "\u001b]7777;agterm-readline=0\u0007";
    private const string ShiftInsert = "\u001b[2;2~";
    private const string CtrlVDown = "\u001b[86;47;118;1;8;1_";
    private const string CtrlVUp = "\u001b[86;47;118;0;8;1_";

    private readonly WebView2Fixture _webView;

    public XtermPageReadlineLiveTests(WebView2Fixture webView)
    {
        _webView = webView;
    }

    private sealed record Pane(
        XtermPage Page,
        Microsoft.Web.WebView2.WinForms.WebView2 View,
        XtermTerminal Terminal,
        string Id,
        List<string> Data,
        List<string> Pasted);

    private sealed record Key(string Type, string KeyName, string Code, int KeyCode,
        bool Ctrl = false, bool Shift = false, bool Alt = false);

    private static Key Down(string key, string code, int keyCode, bool ctrl = false, bool shift = false, bool alt = false) =>
        new("keydown", key, code, keyCode, ctrl, shift, alt);

    private static Key Up(string key, string code, int keyCode, bool ctrl = false, bool shift = false, bool alt = false) =>
        new("keyup", key, code, keyCode, ctrl, shift, alt);

    private async Task<Pane> OpenAsync(string paneId, bool readlineMarker = true, bool rightClickPaste = false)
    {
        var view = await _webView.CreateWebViewAsync();
        var page = await _webView.RunAsync(async _ =>
        {
            var p = new XtermPage(new Win32ScriptHost(view.CoreWebView2));
            await p.InitializeAsync();
            return p;
        });
        if (rightClickPaste)
        {
            var snapshot = RendererSettingsSnapshot.From(
                new AgTerm.Core.Model.AppSettings { RightClickPaste = true });
            await _webView.RunAsync<object?>(async _ =>
            {
                await page.ApplyRendererSettingsAsync(snapshot);
                return null;
            });
        }
        var terminal = await _webView.RunAsync(_ => page.OpenTerminalAsync(paneId, readlineMarker: readlineMarker));
        var pane = new Pane(page, view, terminal, paneId, [], []);
        terminal.DataReceived += d => { lock (pane.Data) pane.Data.Add(d); };
        page.PasteRequested += id => { lock (pane.Pasted) pane.Pasted.Add(id); };
        // ConPTY turns win32-input-mode on as soon as the shell starts
        await FeedAsync(pane, Win32Mode);
        return pane;
    }

    private async Task FeedAsync(Pane pane, string output)
    {
        await _webView.RunAsync(_ =>
        {
            pane.Terminal.FeedOutput(Encoding.UTF8.GetBytes(output));
            return Task.FromResult(0);
        });
        await Task.Delay(150);
    }

    /// <summary>Dispatches the keys on the pane's textarea and returns the data they produced.</summary>
    private async Task<string> KeysAsync(Pane pane, params Key[] keys)
    {
        lock (pane.Data) pane.Data.Clear();
        var events = JsonSerializer.Serialize(keys.Select(k => new
        {
            type = k.Type, key = k.KeyName, code = k.Code, keyCode = k.KeyCode,
            ctrl = k.Ctrl, shift = k.Shift, alt = k.Alt,
        }));
        await _webView.RunAsync(async _ => await pane.View.CoreWebView2.ExecuteScriptAsync(
            $$"""
            (() => {
              const ta = agterm.textareaFor({{JsonSerializer.Serialize(pane.Id)}});
              for (const e of {{events}}) {
                const ev = new KeyboardEvent(e.type, {
                  key: e.key, code: e.code, ctrlKey: e.ctrl, shiftKey: e.shift, altKey: e.alt,
                  cancelable: true, bubbles: true
                });
                Object.defineProperty(ev, "keyCode", { value: e.keyCode });
                Object.defineProperty(ev, "which", { value: e.keyCode });
                ta.dispatchEvent(ev);
              }
            })()
            """));
        await Task.Delay(200);
        lock (pane.Data) return string.Concat(pane.Data);
    }

    private async Task RightClickAsync(Pane pane)
    {
        lock (pane.Data) pane.Data.Clear();
        await _webView.RunAsync(async _ => await pane.View.CoreWebView2.ExecuteScriptAsync(
            $"document.querySelector('[data-pane-id=\"{pane.Id}\"]').dispatchEvent(new MouseEvent('contextmenu', {{ bubbles: true, cancelable: true }}))"));
        await Task.Delay(200);
    }

    private static int PastedCount(Pane pane)
    {
        lock (pane.Pasted) return pane.Pasted.Count;
    }

    private static readonly Key[] CtrlV = [Down("v", "KeyV", 86, ctrl: true)];

    [Fact]
    public async Task Without_a_marker_ctrl_v_asks_the_host_to_paste()
    {
        var pane = await OpenAsync("rl-none");

        var data = await KeysAsync(pane, CtrlV);

        Assert.Equal(1, PastedCount(pane));
        Assert.Equal("", data);
    }

    [Fact]
    public async Task A_pane_opened_without_the_flag_ignores_the_marker()
    {
        var pane = await OpenAsync("rl-deaf", readlineMarker: false);
        await FeedAsync(pane, ShiftInsertPaste);

        var data = await KeysAsync(pane, CtrlV);

        Assert.Equal(1, PastedCount(pane));
        Assert.Equal("", data);
    }

    [Fact]
    public async Task While_psreadline_reads_ctrl_v_sends_shift_insert_and_its_release_sends_nothing()
    {
        var pane = await OpenAsync("rl-shift-insert");
        await FeedAsync(pane, ShiftInsertPaste);

        var press = await KeysAsync(pane, CtrlV);
        var release = await KeysAsync(pane, Up("v", "KeyV", 86, ctrl: true));

        Assert.Equal(ShiftInsert, press);
        Assert.Equal("", release);
        Assert.Equal(0, PastedCount(pane));
    }

    public static TheoryData<string, string, int, bool, bool> PasteKeys() => new()
    {
        { "м", "KeyV", 86, true, false },
        { "V", "KeyV", 86, true, true },
        { "М", "KeyV", 86, true, true },
        { "Insert", "Insert", 45, false, true },
    };

    [Theory]
    [MemberData(nameof(PasteKeys))]
    public async Task Every_paste_key_sends_the_psreadline_paste_key(string key, string code, int keyCode, bool ctrl, bool shift)
    {
        var pane = await OpenAsync("rl-paste-keys");
        await FeedAsync(pane, ShiftInsertPaste);

        var press = await KeysAsync(pane, Down(key, code, keyCode, ctrl: ctrl, shift: shift));
        var release = await KeysAsync(pane, Up(key, code, keyCode, ctrl: ctrl, shift: shift));

        Assert.Equal(ShiftInsert, press);
        Assert.Equal("", release);
        Assert.Equal(0, PastedCount(pane));
    }

    [Fact]
    public async Task Right_click_sends_the_psreadline_paste_key()
    {
        var pane = await OpenAsync("rl-right-click", rightClickPaste: true);
        await FeedAsync(pane, ShiftInsertPaste);

        await RightClickAsync(pane);

        lock (pane.Data) Assert.Equal(ShiftInsert, string.Concat(pane.Data));
        Assert.Equal(0, PastedCount(pane));
    }

    [Fact]
    public async Task In_vi_mode_ctrl_v_sends_the_ctrl_v_record_with_the_latin_letter()
    {
        var pane = await OpenAsync("rl-ctrl-v");
        await FeedAsync(pane, CtrlVPaste);

        var press = await KeysAsync(pane, Down("м", "KeyV", 86, ctrl: true));
        var release = await KeysAsync(pane, Up("м", "KeyV", 86, ctrl: true));

        Assert.Equal(CtrlVDown, press);
        Assert.Equal(CtrlVUp, release);
        Assert.Equal(0, PastedCount(pane));
    }

    [Fact]
    public async Task In_vi_mode_right_click_sends_the_ctrl_v_press_and_release()
    {
        var pane = await OpenAsync("rl-ctrl-v-click", rightClickPaste: true);
        await FeedAsync(pane, CtrlVPaste);

        await RightClickAsync(pane);

        lock (pane.Data) Assert.Equal(CtrlVDown + CtrlVUp, string.Concat(pane.Data));
        Assert.Equal(0, PastedCount(pane));
    }

    [Theory]
    [InlineData(NoPasteKey)]
    [InlineData(NotReading)]
    public async Task Without_a_psreadline_paste_key_ctrl_v_asks_the_host_to_paste(string marker)
    {
        var pane = await OpenAsync("rl-fallback");
        await FeedAsync(pane, ShiftInsertPaste);
        await FeedAsync(pane, marker);

        var data = await KeysAsync(pane, CtrlV);

        Assert.Equal(1, PastedCount(pane));
        Assert.Equal("", data);
    }

    [Fact]
    public async Task An_unknown_payload_does_not_change_the_route()
    {
        var pane = await OpenAsync("rl-unknown-keeps");
        await FeedAsync(pane, ShiftInsertPaste);
        await FeedAsync(pane, "\u001b]7777;something-else\u0007");

        Assert.Equal(ShiftInsert, await KeysAsync(pane, CtrlV));
        Assert.Equal(0, PastedCount(pane));
    }

    [Fact]
    public async Task An_unknown_value_does_not_start_the_psreadline_route()
    {
        var pane = await OpenAsync("rl-unknown");
        await FeedAsync(pane, "\u001b]7777;agterm-readline=2;paste=shift-insert\u0007");

        var data = await KeysAsync(pane, CtrlV);

        Assert.Equal(1, PastedCount(pane));
        Assert.Equal("", data);
    }

    [Fact]
    public async Task In_vi_mode_without_win32_input_mode_ctrl_v_asks_the_host_to_paste()
    {
        var pane = await OpenAsync("rl-ctrl-v-kitty");
        await FeedAsync(pane, CtrlVPaste);
        // a program pushed kitty keyboard flags: the page turns the win32 encoding off
        await FeedAsync(pane, "\u001b[>1u");

        var data = await KeysAsync(pane, CtrlV);

        Assert.Equal(1, PastedCount(pane));
        Assert.Equal("", data);
    }

    // Ctrl+letter on a non-Latin layout. PSReadLine names a key whose character is a control
    // character through the keyboard layout, so on Russian xterm's Ctrl+Z record (character 0x1a)
    // reads as Ctrl+я and Undo does not run. While PSReadLine reads input the pane sends the record
    // with the Latin letter instead.

    private const string Russian = "фисвуапршолдьтщзйкыегмцчня";

    private static Key CyrillicCtrl(char latin, string type = "keydown", bool shift = false, bool alt = false)
    {
        var cyrillic = Russian[latin - 'a'].ToString();
        return new(type, shift ? cyrillic.ToUpperInvariant() : cyrillic, $"Key{char.ToUpperInvariant(latin)}",
            char.ToUpperInvariant(latin), Ctrl: true, Shift: shift, Alt: alt);
    }

    private const string CtrlZDown = "\u001b[90;44;122;1;8;1_";
    private const string CtrlZUp = "\u001b[90;44;122;0;8;1_";

    [Fact]
    public async Task While_psreadline_reads_cyrillic_ctrl_z_sends_the_record_with_the_latin_letter()
    {
        var pane = await OpenAsync("rl-ctrl-z");
        await FeedAsync(pane, NoPasteKey);

        var press = await KeysAsync(pane, CyrillicCtrl('z'));
        var release = await KeysAsync(pane, CyrillicCtrl('z', "keyup"));

        Assert.Equal(CtrlZDown, press);
        Assert.Equal(CtrlZUp, release);
    }

    [Fact]
    public async Task The_record_matches_xterms_own_latin_record_except_the_character()
    {
        var pane = await OpenAsync("rl-ctrl-letters");
        await FeedAsync(pane, NoPasteKey);

        // Ctrl+C copies or interrupts and Ctrl+V pastes; neither is a Ctrl+letter rewrite
        foreach (var latin in "abdefghijklmnopqrstuwxyz")
        {
            var upper = char.ToUpperInvariant(latin);
            var xterm = await KeysAsync(pane, Down(latin.ToString(), $"Key{upper}", upper, ctrl: true));
            await KeysAsync(pane, Up(latin.ToString(), $"Key{upper}", upper, ctrl: true));
            var bridge = await KeysAsync(pane, CyrillicCtrl(latin));
            await KeysAsync(pane, CyrillicCtrl(latin, "keyup"));

            var fields = xterm.TrimStart('\u001b', '[').TrimEnd('_').Split(';');
            Assert.Equal(6, fields.Length);
            fields[2] = ((int)latin).ToString();
            Assert.Equal($"\u001b[{string.Join(';', fields)}_", bridge);
        }
    }

    /// <summary>What a pane without the readline marker sends for the keys: today's encoding.</summary>
    private async Task<string> TodayAsync(Pane reference, params Key[] keys) => await KeysAsync(reference, keys);

    [Fact]
    public async Task Without_the_marker_cyrillic_ctrl_z_is_encoded_as_today()
    {
        var reference = await OpenAsync("rl-today-ref", readlineMarker: false);
        var today = await TodayAsync(reference, CyrillicCtrl('z'));
        Assert.NotEqual(CtrlZDown, today);

        var notReading = await OpenAsync("rl-today-off");
        await FeedAsync(notReading, NoPasteKey);
        await FeedAsync(notReading, NotReading);
        var deaf = await OpenAsync("rl-today-deaf", readlineMarker: false);
        await FeedAsync(deaf, NoPasteKey);

        Assert.Equal(today, await KeysAsync(notReading, CyrillicCtrl('z')));
        Assert.Equal(today, await KeysAsync(deaf, CyrillicCtrl('z')));
    }

    public static TheoryData<string, string, string, int, bool, bool, bool> UnchangedKeys() => new()
    {
        { "latin ctrl+z", "z", "KeyZ", 90, true, false, false },
        { "ctrl+c without a selection", "с", "KeyC", 67, true, false, false },
        { "ctrl+alt+letter", "я", "KeyZ", 90, true, false, true },
        { "ctrl+shift+cyrillic z", "Я", "KeyZ", 90, true, true, false },
    };

    [Theory]
    [MemberData(nameof(UnchangedKeys))]
    public async Task With_the_marker_other_ctrl_keys_are_encoded_as_today(
        string name, string key, string code, int keyCode, bool ctrl, bool shift, bool alt)
    {
        _ = name;
        var reference = await OpenAsync("rl-unchanged-ref", readlineMarker: false);
        var pane = await OpenAsync("rl-unchanged");
        await FeedAsync(pane, NoPasteKey);
        Key[] keys =
        [
            Down(key, code, keyCode, ctrl, shift, alt),
            Up(key, code, keyCode, ctrl, shift, alt),
        ];

        Assert.Equal(await TodayAsync(reference, keys), await KeysAsync(pane, keys));
    }

    [Fact]
    public async Task With_kitty_flags_pushed_cyrillic_ctrl_z_is_encoded_as_today()
    {
        var reference = await OpenAsync("rl-kitty-ref", readlineMarker: false);
        await FeedAsync(reference, "\u001b[>1u");
        var pane = await OpenAsync("rl-kitty");
        await FeedAsync(pane, NoPasteKey);
        await FeedAsync(pane, "\u001b[>1u");
        Key[] keys = [CyrillicCtrl('z'), CyrillicCtrl('z', "keyup")];

        Assert.Equal(await TodayAsync(reference, keys), await KeysAsync(pane, keys));
    }

    [Fact]
    public async Task A_rewritten_press_gets_the_latin_release_after_the_marker_clears()
    {
        var pane = await OpenAsync("rl-release-after-clear");
        await FeedAsync(pane, NoPasteKey);

        Assert.Equal(CtrlZDown, await KeysAsync(pane, CyrillicCtrl('z')));
        await FeedAsync(pane, NotReading);

        Assert.Equal(CtrlZUp, await KeysAsync(pane, CyrillicCtrl('z', "keyup")));
    }

    private async Task BlurAsync(Pane pane)
    {
        await _webView.RunAsync(async _ => await pane.View.CoreWebView2.ExecuteScriptAsync(
            $"agterm.textareaFor({JsonSerializer.Serialize(pane.Id)}).dispatchEvent(new FocusEvent('blur'))"));
    }

    [Fact]
    public async Task A_release_after_the_focus_was_lost_is_encoded_as_today()
    {
        var reference = await OpenAsync("rl-blur-ref", readlineMarker: false);
        await TodayAsync(reference, CyrillicCtrl('z'));
        var todayRelease = await TodayAsync(reference, CyrillicCtrl('z', "keyup"));
        Assert.NotEqual(CtrlZUp, todayRelease);

        var pane = await OpenAsync("rl-blur");
        await FeedAsync(pane, NoPasteKey);
        Assert.Equal(CtrlZDown, await KeysAsync(pane, CyrillicCtrl('z')));
        await BlurAsync(pane);
        await FeedAsync(pane, NotReading);

        Assert.Equal(todayRelease, await KeysAsync(pane, CyrillicCtrl('z', "keyup")));
    }

    [Fact]
    public async Task A_new_press_of_the_same_key_replaces_the_rewritten_release()
    {
        var reference = await OpenAsync("rl-repress-ref", readlineMarker: false);
        var todayPress = await TodayAsync(reference, CyrillicCtrl('z'));
        var todayRelease = await TodayAsync(reference, CyrillicCtrl('z', "keyup"));

        var pane = await OpenAsync("rl-repress");
        await FeedAsync(pane, NoPasteKey);
        Assert.Equal(CtrlZDown, await KeysAsync(pane, CyrillicCtrl('z')));
        await FeedAsync(pane, NotReading);

        Assert.Equal(todayPress, await KeysAsync(pane, CyrillicCtrl('z')));
        Assert.Equal(todayRelease, await KeysAsync(pane, CyrillicCtrl('z', "keyup")));
    }
}
