using Android.App;
using Android.Content;
using Android.Gms.Tasks;
using Android.OS;
using PhotonCanvas.Remote.App;
using Xamarin.Google.MLKit.Vision.Barcode.Common;
using Xamarin.Google.MLKit.Vision.CodeScanner;

namespace PhotonCanvas.Remote.Android;

/// <summary>What the Android phone does for the app: its storage, the QR scanner, buzzing, the browser.</summary>
sealed class AndroidPhone(Activity activity) : IPhone
{
    public string DataFolder => activity.FilesDir!.AbsolutePath;

    public string Version => typeof(AndroidPhone).Assembly.GetName().Version?.ToString(3) ?? "";

    public bool CanScan => true;

    /// <summary>Google Play services' code scanner: its own camera screen, no camera permission for the app.</summary>
    public System.Threading.Tasks.Task<string?> ScanQrAsync()
    {
        var done = new TaskCompletionSource<string?>();
        try
        {
            var options = new GmsBarcodeScannerOptions.Builder().SetBarcodeFormats(Barcode.FormatQrCode).Build();
            var scan = GmsBarcodeScanning.GetClient(activity, options).StartScan();
            scan.AddOnSuccessListener(new Listener(r => done.TrySetResult((r as Barcode)?.RawValue)));
            scan.AddOnCanceledListener(new Listener(_ => done.TrySetResult(null)));
            scan.AddOnFailureListener(new Listener(_ => done.TrySetResult(null)));
        }
        catch (Java.Lang.Exception)
        {
            done.TrySetResult(null);
        }
        return done.Task;
    }

    public void Vibrate(bool alarm)
    {
        var vibrator = Build.VERSION.SdkInt >= BuildVersionCodes.S
            ? (activity.GetSystemService(Context.VibratorManagerService) as VibratorManager)?.DefaultVibrator
            : activity.GetSystemService(Context.VibratorService) as Vibrator;
        if (vibrator is not { HasVibrator: true }) return;
        vibrator.Vibrate(alarm
            ? VibrationEffect.CreateWaveform([0, 400, 150, 400, 150, 400], -1)
            : VibrationEffect.CreateOneShot(200, VibrationEffect.DefaultAmplitude));
    }

    public void OpenWeb(string url)
    {
        var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
        intent.AddFlags(ActivityFlags.NewTask);
        activity.StartActivity(intent);
    }

    /// <summary>One listener for success, cancel and failure: the scanner's result (or nothing).</summary>
    sealed class Listener(Action<Java.Lang.Object?> then) : Java.Lang.Object, IOnSuccessListener, IOnCanceledListener, IOnFailureListener
    {
        public void OnSuccess(Java.Lang.Object? result) => then(result);
        public void OnCanceled() => then(null);
        public void OnFailure(Java.Lang.Exception e) => then(null);
    }
}
