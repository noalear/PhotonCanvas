using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static PhotonCanvas.Remote.App.Ui;

namespace PhotonCanvas.Remote.App;

/// <summary>Where the server is: scan the QR code in PhotonCanvas (Laser → Remote access), or type the address and the code. Or try the demo.</summary>
public sealed class ConnectPage : UserControl
{
    readonly MainView _host;
    readonly IPhone _phone;
    readonly TextBox _address = new() { Watermark = "192.168.1.20", AcceptsReturn = false };
    readonly TextBox _code = new() { Watermark = "XXXX-XXXX-XXXX-XXXX", FontFamily = new FontFamily("monospace") };
    readonly TextBlock _error = Text("", "error");
    readonly Button _connect;
    readonly Button _scan;

    public ConnectPage(MainView host, IPhone phone, ConnectionInfo? last, string? error)
    {
        _host = host;
        _phone = phone;
        _error.Text = error ?? "";
        _error.IsVisible = error is not null;
        if (last is not null)
        {
            _address.Text = last.Display;
            _code.Text = last.Code;
        }
        _scan = Btn("Scan the QR code", "primary", async () => await Scan());
        _scan.IsVisible = phone.CanScan;
        _connect = Btn("Connect", phone.CanScan ? null : "primary", async () => await Connect(ConnectionInfo.TryParse(_address.Text, _code.Text), "Type the computer's address (as Remote access shows it) and the access code."));
        _code.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) await Connect(ConnectionInfo.TryParse(_address.Text, _code.Text), "Type the address and the access code."); };

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new(16, 24, 16, 16),
                Spacing = 14,
                Children =
                {
                    Text("PhotonCanvas Remote").With(t => { t.FontSize = 26; t.FontWeight = FontWeight.Bold; }),
                    Text("Watch and run your laser from here: what it's doing, its camera, pause and stop, your designs, and starting a job. The phone talks to PhotonCanvas on your computer, which talks to the machine.", "muted"),
                    Card("Connect to your computer",
                        Text("In PhotonCanvas on your computer: Laser → Remote access → Start server. Then scan the QR code it shows."),
                        _scan,
                        Text(phone.CanScan ? "Or type the address and the access code:" : "Type the address and the access code it shows:", "muted"),
                        Labelled("Address", _address),
                        Labelled("Access code", _code),
                        _connect,
                        _error),
                    Card("No laser to hand?",
                        Text("Try everything on a pretend machine. Nothing real moves or fires.", "muted"),
                        Btn("Try the demo machine", null, () => _host.Connect(new DemoRemote()))),
                    Text("The phone and the computer need to be on the same Wi-Fi. To use it from anywhere, put Tailscale (free) on both: the app then switches between home Wi-Fi and Tailscale by itself.", "muted"),
                },
            },
        };
    }

    static Control Labelled(string label, Control editor) => new StackPanel { Spacing = 4, Children = { Text(label, "muted"), editor } };

    async Task Scan()
    {
        var scanned = await _phone.ScanQrAsync();
        if (scanned is null) return;
        if (ConnectionInfo.TryParse(scanned) is not { } c)
        {
            Show("That QR code isn't the one from Remote access. Scan the code in PhotonCanvas: Laser → Remote access.");
            return;
        }
        _address.Text = c.Display;
        _code.Text = c.Code;
        await Connect(c, "");
    }

    async Task Connect(ConnectionInfo? c, string ifMissing)
    {
        if (c is null)
        {
            Show(ifMissing);
            return;
        }
        _connect.IsEnabled = _scan.IsEnabled = false;
        _connect.Content = "Connecting…";
        HttpRemote? remote = null;
        try
        {
            // Whichever of its addresses answers (scanned away from home, only the Tailscale one may), then
            // encrypted: the QR code carries the certificate; a typed address asks for it once.
            c = await HttpRemote.ReachAsync(c) ?? c;
            c = await HttpRemote.SecureAsync(c);
            remote = new HttpRemote(c);
            var info = await remote.InfoAsync();
            if (info.Api > ServerInfo.CurrentApi)
                Show("PhotonCanvas on the computer is newer than this app. Update the app; most things should still work.");
            if (info.Addresses is { Length: > 0 } all) c = c.Using(c.Address, all.Append(c.Address));
            _host.Remember(c);
            _host.Connect(remote, c);
        }
        catch (RemoteError e)
        {
            remote?.Dispose();
            Show(e.Message);
        }
        finally
        {
            _connect.IsEnabled = _scan.IsEnabled = true;
            _connect.Content = "Connect";
        }
    }

    void Show(string error)
    {
        _error.Text = error;
        _error.IsVisible = error.Length > 0;
    }
}
