#:property PublishAot=false
// Packs a folder into a zip, the folder itself at the top (PhotonCanvas/…), with "/" between folder
// names and plain deflate: what the program's updater reads (src/PhotonCanvas.Core/UpdateDelta.cs).
// tools/publish.sh uses it where there's no zip command (Windows):  dotnet run tools/zipdir.cs -- <folder> <zip>
// PublishAot is off: file-based apps turn it on by default, which pulls in the ILCompiler package, and a
// security advisory on that package fails the restore here (TreatWarningsAsErrors). A zip tool needs no AOT.
using System.IO.Compression;

if (args.Length != 2) { Console.Error.WriteLine("usage: dotnet run tools/zipdir.cs -- <folder> <zip>"); return 2; }
var (folder, zip) = (Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
File.Delete(zip);
ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
return 0;
