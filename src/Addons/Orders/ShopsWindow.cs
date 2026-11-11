using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PhotonCanvas.App;
using PhotonCanvas.App.Addons;
using static PhotonCanvas.App.UiKit;

namespace PhotonCanvas.Addon.Orders;

/// <summary>
/// Connecting the shops: Etsy (an app of your own on Etsy's developer site, then Connect in the
/// browser) and Shopify (an app with read_orders on your shop). Only orders are read; nothing in a
/// shop is changed. Also the packing list's paper and whether shipped orders come in too.
/// </summary>
public sealed class ShopsWindow : Window
{
    readonly IAddonHost _host;
    readonly string _path;
    readonly ShopSettings _s;
    readonly TextBox _keystring = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "Your Etsy app's keystring" };
    readonly TextBox _etsySecret = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PasswordChar = '•', Watermark = "Its shared secret" };
    readonly TextBlock _etsyStatus = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button _connect = new() { Content = "Connect to Etsy", Classes = { "primary" } };
    readonly Button _disconnect = new() { Content = "Disconnect" };
    readonly TextBox _store = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "my-shop.myshopify.com" };
    readonly TextBox _clientId = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Watermark = "From the app's Settings in the Dev Dashboard" };
    readonly TextBox _clientSecret = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PasswordChar = '•' };
    readonly TextBox _token = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PasswordChar = '•', Watermark = "shpat_… (an older custom app only)" };
    readonly TextBlock _shopifyStatus = new() { TextWrapping = TextWrapping.Wrap };
    readonly CheckBox _shipped = new() { Content = "Also bring in orders already shipped (for a reprint)" };
    readonly ComboBox _paper = new() { ItemsSource = new[] { "US Letter", "A4" }, HorizontalAlignment = HorizontalAlignment.Stretch };
    CancellationTokenSource? _waiting;

    public ShopsWindow(IAddonHost host, string path)
    {
        _host = host;
        _path = path;
        _s = ShopSettings.Load(path);
        Title = "Shops";
        Width = 640;
        Height = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush("Bg1");

        _keystring.Text = _s.EtsyKeystring;
        _etsySecret.Text = Secret.Open(_s.EtsySecret);
        _store.Text = _s.ShopifyStore;
        _clientId.Text = _s.ShopifyClientId;
        _clientSecret.Text = Secret.Open(_s.ShopifySecret);
        _token.Text = Secret.Open(_s.ShopifyToken);
        _shipped.IsChecked = _s.IncludeShipped;
        _paper.SelectedIndex = _s.PaperA4 ? 1 : 0;
        Content = Layout();
        ShowEtsy();
        _shopifyStatus.Text = _s.ShopifyReady ? "Set up. Get Shopify orders reads it." : "Not set up yet.";
        Closing += (_, _) => { _waiting?.Cancel(); Keep(); };
    }

    Control Layout()
    {
        var callback = new TextBox { Text = EtsyApi.RedirectUri, IsReadOnly = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(callback, "Enter this exactly as the app's callback URL on Etsy.");
        ToolTip.SetTip(_keystring, "On etsy.com/developers → Your apps: the app's keystring (also called its API key).");
        ToolTip.SetTip(_etsySecret, "The app's shared secret, shown beside the keystring. Kept locked to your Windows account.");
        ToolTip.SetTip(_connect, "Opens Etsy in your browser to allow PhotonCanvas to read your shop's orders. Then come back here.");
        _connect.Click += async (_, _) => await ConnectEtsy();
        _disconnect.Click += (_, _) =>
        {
            _s.EtsyAccess = _s.EtsyRefresh = _s.EtsyShopName = "";
            _s.EtsyShopId = 0;
            Keep();
            ShowEtsy();
        };
        var etsy = Explained("Etsy",
            "Etsy lets a shop owner read their own orders through an app of their own. Once: on etsy.com/developers, create an app (any name; it's for your shop only), and enter this callback URL in its settings. Then copy its keystring and shared secret here and Connect.",
            Field("Callback URL", callback),
            Field("Keystring", _keystring),
            Field("Shared secret", _etsySecret),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _connect, _disconnect } },
            _etsyStatus);

        ToolTip.SetTip(_store, "Your shop's address: my-shop.myshopify.com (or just my-shop).");
        ToolTip.SetTip(_clientId, "In the Shopify Dev Dashboard: create an app with the read_orders permission (read_customers too for names and addresses), install it on your shop, then copy its Client ID.");
        ToolTip.SetTip(_clientSecret, "The app's secret, beside the Client ID. Kept locked to your Windows account.");
        ToolTip.SetTip(_token, "Only for a custom app made in the shop's admin before 2026: its Admin API access token. Leave empty to use the Client ID and secret.");
        var test = Button("Try it", tip: "Read the open orders once, to check the app is set up (nothing is changed in the shop).");
        test.Click += async (_, _) => await TryShopify(test);
        var shopify = Explained("Shopify",
            "Shopify reads orders through an app on your shop. In the Dev Dashboard (dev.shopify.com), create an app, give it the read_orders permission (and read_customers, for names and addresses), install it on your shop, and copy its Client ID and secret here.",
            Field("Shop", _store),
            Field("Client ID", _clientId),
            Field("Client secret", _clientSecret),
            Field("Access token", _token),
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { test } },
            _shopifyStatus);

        ToolTip.SetTip(_shipped, "Normally only orders still to be sent are brought in. Tick to bring in shipped ones too, to make one again.");
        ToolTip.SetTip(_paper, "The paper the packing list is laid out for.");
        var general = Explained("Orders", "For every shop and for CSV files.",
            _shipped,
            Field("Paper", _paper));

        var close = new Button { Content = "Close", IsCancel = true, MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        return new DockPanel
        {
            Children =
            {
                close.With(b => { DockPanel.SetDock(b, Dock.Bottom); b.Margin = new Thickness(16); }),
                new ScrollViewer
                {
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Margin = new Thickness(16, 16, 16, 0),
                        Children =
                        {
                            new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Bring orders straight in from your shop instead of downloading a CSV. PhotonCanvas only reads orders: it never changes anything in the shop, and only talks to it when you click Get orders." },
                            etsy, shopify, general,
                        },
                    },
                },
            },
        };
    }

    void ShowEtsy()
    {
        bool on = _s.EtsyConnected;
        _disconnect.IsVisible = on;
        _connect.Content = on ? "Connect again" : "Connect to Etsy";
        _etsyStatus.Text = on ? $"Connected{(_s.EtsyShopName.Length > 0 ? " to " + _s.EtsyShopName : "")}. Get Etsy orders reads it." : "Not connected.";
    }

    /// <summary>What's typed, kept (secrets locked to this Windows account).</summary>
    void Keep()
    {
        _s.EtsyKeystring = (_keystring.Text ?? "").Trim();
        _s.EtsySecret = Secret.Lock((_etsySecret.Text ?? "").Trim());
        _s.ShopifyStore = (_store.Text ?? "").Trim();
        _s.ShopifyClientId = (_clientId.Text ?? "").Trim();
        _s.ShopifySecret = Secret.Lock((_clientSecret.Text ?? "").Trim());
        _s.ShopifyToken = Secret.Lock((_token.Text ?? "").Trim());
        _s.IncludeShipped = _shipped.IsChecked == true;
        _s.PaperA4 = _paper.SelectedIndex == 1;
        try { _s.Save(_path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { _etsyStatus.Text = $"Couldn't save: {e.Message}"; }
    }

    async Task ConnectEtsy()
    {
        if (_waiting is not null) { _waiting.Cancel(); return; }
        Keep();
        if (_s.EtsyKeystring.Length == 0) { _etsyStatus.Text = "Enter your Etsy app's keystring first."; return; }
        var (verifier, challenge) = EtsyApi.Pkce();
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
        using var cts = _waiting = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        _connect.Content = "Stop waiting";
        _etsyStatus.Text = "Allow PhotonCanvas in the browser window that opened, then come back here…";
        try
        {
            var waiting = EtsyApi.WaitForCode(state, cts.Token);
            await Launcher.LaunchUriAsync(new Uri(EtsyApi.AuthorizeUrl(_s.EtsyKeystring, challenge, state)));
            var code = await waiting;
            _etsyStatus.Text = "Connecting…";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            await EtsyApi.Connect(http, _s, code, verifier, cts.Token);
            _s.Save(_path);
            Activate();
        }
        catch (OperationCanceledException) { _etsyStatus.Text = "Stopped waiting for Etsy."; _waiting = null; ShowEtsy(); return; }
        catch (Exception e) when (e is ShopException or HttpRequestException or System.Text.Json.JsonException or InvalidOperationException or IOException or System.Net.Sockets.SocketException)
        {
            _etsyStatus.Text = e is ShopException ? e.Message : $"Couldn't connect: {e.Message}";
            _waiting = null;
            _connect.Content = _s.EtsyConnected ? "Connect again" : "Connect to Etsy";
            return;
        }
        finally { if (_waiting == cts) _waiting = null; }
        ShowEtsy();
    }

    async Task TryShopify(Button b)
    {
        Keep();
        if (!_s.ShopifyReady) { _shopifyStatus.Text = "Enter the shop, and the app's Client ID and secret (or an access token)."; return; }
        b.IsEnabled = false;
        _shopifyStatus.Text = "Asking Shopify…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var (items, noCustomers) = await ShopifyApi.Fetch(http, _s, false, CancellationToken.None);
            _shopifyStatus.Text = $"It works: {items.Count} line{(items.Count == 1 ? "" : "s")} waiting to be made." +
                (noCustomers ? " Names and addresses are left out until the app may read customers' details." : "");
        }
        catch (Exception e) when (e is ShopException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or IOException)
        {
            _shopifyStatus.Text = e is ShopException ? e.Message : $"Couldn't reach Shopify: {e.Message}";
        }
        finally { b.IsEnabled = true; }
    }
}
