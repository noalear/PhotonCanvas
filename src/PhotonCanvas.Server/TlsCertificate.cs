using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PhotonCanvas.Doc;
using PhotonCanvas.Remote;

namespace PhotonCanvas.Server;

/// <summary>
/// The server's own certificate for HTTPS: made once (ECDSA P-256, good for 20 years) and kept in
/// remote\tls.pfx, so phones that scanned its fingerprint keep trusting it. No certificate authority
/// vouches for it; the phone trusts it because the QR code carries its fingerprint.
/// </summary>
public static class TlsCertificate
{
    static string PfxPath(string dir) => Path.Combine(dir, "tls.pfx");

    /// <summary>The certificate (made the first time) and what to tell phones about it.</summary>
    public static (X509Certificate2 Certificate, TlsState State) LoadOrCreate(int port, string? dir = null)
    {
        dir ??= RemoteConfig.Dir;
        Directory.CreateDirectory(dir);
        if (TlsState.Read(dir) is { } state && File.Exists(PfxPath(dir)))
        {
            try
            {
                var cert = X509CertificateLoader.LoadPkcs12FromFile(PfxPath(dir), state.Password, X509KeyStorageFlags.UserKeySet);
                var s = state with { Port = port, Fingerprint = Fingerprints.Of(cert.RawData) };
                if (s != state) s.Write(dir);
                return (cert, s);
            }
            catch (CryptographicException e)
            {
                ServerLog.Write("tls", $"The certificate couldn't be read ({e.Message}); making a new one. Phones must scan the QR code again.");
            }
        }
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var req = new CertificateRequest("CN=PhotonCanvas remote server", key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false)); // server authentication
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("photoncanvas.local");
        req.CertificateExtensions.Add(san.Build());
        using var made = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(20));
        // Through a PFX file: Windows' TLS needs the key stored, not just in memory.
        File.WriteAllBytes(PfxPath(dir), made.Export(X509ContentType.Pfx, password));
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(PfxPath(dir), password, X509KeyStorageFlags.UserKeySet);
        var created = new TlsState(port, Fingerprints.Of(certificate.RawData), password);
        created.Write(dir);
        ServerLog.Write("tls", "Made the server's certificate for encrypted connections.");
        return (certificate, created);
    }
}
