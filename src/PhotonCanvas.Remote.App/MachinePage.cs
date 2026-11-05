using Avalonia.Controls;
using Avalonia.Threading;
using static PhotonCanvas.Remote.App.Ui;

namespace PhotonCanvas.Remote.App;

/// <summary>What the machine is doing, the job on it (pause, stop, or look and start), and its camera.</summary>
public sealed class MachinePage : UserControl
{
    readonly MainView _host;
    readonly Session _s;
    readonly TextBlock _state = Text("–", "big");
    readonly TextBlock _line = Text("", "muted");
    readonly TextBlock _problem = Text("", "error");
    readonly Border _jobCard;
    readonly TextBlock _jobName = Text("", "big");
    readonly TextBlock _jobFrom = Text("", "muted");
    readonly ProgressBar _bar = new() { Minimum = 0, Maximum = 1 };
    readonly TextBlock _times = Text("", "muted");
    readonly StackPanel _startArea = new() { Spacing = 8 };
    readonly Image _startPic = PictureBox();
    readonly TextBlock _startHint = Text("", "muted");
    readonly Button _look, _start, _pause, _stop;
    readonly Image _pic = PictureBox(360);
    readonly TextBlock _picLine = Text("No picture yet.", "muted");
    readonly TextBlock _picBlank = Text("The machine blanks its cameras while a job runs, so pictures come out black until it ends. You'll get a picture of the result when it finishes.", "muted");
    readonly Button _take, _live;
    readonly Button _overview, _closeup;
    bool _liveOn;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(5) };
    string _camera = "overview";
    ShownPicture? _cameraPic;

    public MachinePage(MainView host, Session s)
    {
        _host = host;
        _s = s;
        _look = Btn("Look at the bed", "primary", async () => await Look());
        _start = Btn("Start job", "go", async () => await Start());
        _pause = Btn("Pause", null, async () => await Control(_s.Status?.Job?.Stage == "paused" ? "resume" : "pause"));
        _stop = Btn("Stop", "danger", async () => await Stop());
        _take = Btn("Take picture", "primary", async () => await TakePicture());
        _live = Btn("Live", null, ToggleLive);
        _overview = Btn("Overview", "seg", () => Camera("overview"));
        _closeup = Btn("Close-up", "seg", () => Camera("closeup"));
        _overview.Classes.Add("on");
        _picBlank.IsVisible = false;
        _startPic.PointerReleased += (_, _) => { if (_s.LastPicture is { } p) _host.ShowPicture(p.Image, $"The bed at {p.TakenAt.ToLocalTime():T}"); };
        _pic.PointerReleased += (_, _) => { if (_cameraPic is { } p) _host.ShowPicture(p.Image, $"{(_camera == "overview" ? "Overview" : "Close-up")} at {p.TakenAt.ToLocalTime():T}"); };

        _startArea.Children.Add(_startPic);
        _startArea.Children.Add(_startHint);
        _startArea.Children.Add(Row(_look, _start));
        _jobCard = Card("Job", _jobName, _jobFrom, _bar, _times, _startArea, Row(_pause, _stop));

        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new(12),
                Spacing = 12,
                Children =
                {
                    Card("Machine", _state, _line, _problem),
                    _jobCard,
                    Card("Camera",
                        new Border { Background = Brush("Bg2"), CornerRadius = new(10), Padding = new(3), Child = Row(_overview, _closeup) },
                        _pic, _picLine, _picBlank, Row(_take, _live)),
                },
            },
        };
        _tick.Tick += (_, _) => UpdateStart();
        AttachedToVisualTree += (_, _) => _tick.Start();
        DetachedFromVisualTree += (_, _) =>
        {
            _tick.Stop();
            SetLive(false);
        };
        Update();
    }

    /// <summary>The status changed: show it.</summary>
    public void Update()
    {
        var st = _s.Status;
        bool on = _s.Live && st is { Connected: true };
        _state.Text = !_s.Live ? "Not connected" : st?.State ?? "–";
        _line.Text = on ? string.Join(" · ", new[] { st!.Ip, st.FocusDistanceMm is { } f ? $"focus distance {f:0.0} mm" : null }.Where(x => x is not null)) : "";
        _problem.Text = !_s.Live ? _s.Problem ?? "" : st is { Connected: false } ? st.Problem ?? "The computer isn't connected to the machine." : "";
        _problem.IsVisible = _problem.Text.Length > 0;
        _take.IsEnabled = on;

        var j = on ? st!.Job : null;
        _jobCard.IsVisible = j is not null;
        if (j is null) return;
        bool loaded = j.Stage == "loaded";
        _jobName.Text = j.Design;
        _jobFrom.Text = SourceText(j.Source) + (j.StartedFromPhone ? " · started from the phone" : "");
        _bar.IsVisible = !loaded;
        _bar.Value = j.Fraction ?? (j.RemainingS is { } r && j.ElapsedS + r > 0 ? j.ElapsedS / (j.ElapsedS + r) : 0);
        _times.Text = loaded
            ? j.EstimateS is { } est ? $"About {Clock(est)} once started." : "Waiting to start."
            : $"Elapsed {Clock(j.ElapsedS)}" + (j.RemainingS is { } left ? $" · about {Clock(left)} left" : "") + (j.Stage == "paused" ? " · paused" : "")
              + (JobTimes.FinishText(j, DateTimeOffset.Now) is { } done ? $" · {done}" : "");
        _pause.Content = j.Stage == "paused" ? "Continue" : "Pause";
        _pause.IsVisible = !loaded;
        _stop.Content = loaded ? "Cancel job" : "Stop";
        _startArea.IsVisible = loaded;
        UpdateStart();
    }

    void UpdateStart()
    {
        var job = _s.Status?.Job;
        bool ok = _s.LastPicture is { } p && p.StartsJob(job, DateTimeOffset.Now);
        _start.IsEnabled = ok;
        _startPic.IsVisible = ok;
        if (ok && !ReferenceEquals(_startPic.Tag, _s.LastPicture))
        {
            _startPic.Source = Picture(_s.LastPicture!.Image);
            _startPic.Tag = _s.LastPicture;
        }
        _look.Content = ok ? "New picture" : "Look at the bed";
        _startHint.Text = ok
            ? $"The bed at {_s.LastPicture!.TakenAt.ToLocalTime():T}. Is the material in place and nothing in the way? Start when ready: the laser will fire."
            : "Look at the bed first: the job only starts after that (a picture less than 2 minutes old).";
    }

    async Task Look()
    {
        _look.IsEnabled = false;
        _look.Content = "Taking…";
        try { await _s.TakePictureAsync("overview", 1280); }
        catch (RemoteError e) { _host.Toast(e.Message); }
        _look.IsEnabled = true;
        UpdateStart();
    }

    async Task Start()
    {
        if (_s.Status?.Job is not { } job || _s.LastPicture?.Id is not { } pic) return;
        if (!await _host.Confirm($"Start \"{job.Design}\" now?", "The laser will fire. You looked at the bed a moment ago: the material is in place and nothing is in the way?", "Start job", "go")) return;
        _start.IsEnabled = false;
        try
        {
            await _s.Remote.StartAsync(pic);
            _host.Toast("Started.");
        }
        catch (RemoteError e) { await _host.Confirm("It didn't start", e.Message, "OK"); }
        UpdateStart();
    }

    async Task Stop()
    {
        if (_s.Status?.Job is not { } job) return;
        bool loaded = job.Stage == "loaded";
        if (!await _host.Confirm(loaded ? $"Cancel \"{job.Design}\"?" : $"Stop \"{job.Design}\" now?",
                loaded ? "It's loaded but not started. It comes off the machine." : "It can't be continued afterwards.",
                loaded ? "Cancel job" : "Stop", "danger")) return;
        await Control("stop");
        _host.Toast(loaded ? "Cancelled." : "Stopped.");
    }

    async Task Control(string action)
    {
        try { await _s.Remote.ControlAsync(action); }
        catch (RemoteError e) { _host.Toast(e.Message); }
    }

    void Camera(string camera)
    {
        _camera = camera;
        _overview.Classes.Set("on", camera == "overview");
        _closeup.Classes.Set("on", camera == "closeup");
    }

    void ToggleLive() => SetLive(!_liveOn);

    void SetLive(bool on)
    {
        if (on == _liveOn) return;
        _liveOn = on;
        _live.Classes.Set("primary", on);
        _live.Content = on ? "Stop live" : "Live";
        _take.IsVisible = !on;
        if (on) _ = LiveAsync();
    }

    /// <summary>Pictures one after another (a few a second) while Live is on and the page is showing.</summary>
    async Task LiveAsync()
    {
        while (_liveOn)
        {
            if (_s.Status?.Mode == "P_WORKING")
            {
                // Blanked while the laser runs: no point asking.
                _picBlank.IsVisible = true;
                _picLine.Text = "Live view waits while the job runs (the machine blanks its cameras).";
                await Task.Delay(2000);
                continue;
            }
            try
            {
                var p = _cameraPic = await _s.TakePictureAsync(_camera, 960);
                if (!_liveOn) break;
                _pic.Source = Picture(p.Image);
                _pic.IsVisible = true;
                _picLine.Text = $"Live · {(_camera == "overview" ? "overview" : "close-up")} · {p.TakenAt.ToLocalTime():T}";
                _picBlank.IsVisible = p.Blanked;
                UpdateStart();
                await Task.Delay(150);
            }
            catch (RemoteError)
            {
                await Task.Delay(1500);
            }
        }
    }

    async Task TakePicture()
    {
        _take.IsEnabled = false;
        _take.Content = "Taking…";
        try
        {
            var p = _cameraPic = await _s.TakePictureAsync(_camera, 1280);
            _pic.Source = Picture(p.Image);
            _pic.IsVisible = true;
            _picLine.Text = $"{(_camera == "overview" ? "Overview" : "Close-up")} at {p.TakenAt.ToLocalTime():T} · tap to enlarge";
            _picBlank.IsVisible = p.Blanked;
            UpdateStart();
        }
        catch (RemoteError e) { _host.Toast(e.Message); }
        _take.IsEnabled = true;
        _take.Content = "Take picture";
    }
}
