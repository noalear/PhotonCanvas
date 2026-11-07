// Packs a folder into a zip, the folder itself at the top (PhotonCanvas/…), with "/" between folder
// names and plain deflate: what the program's updater reads (src/PhotonCanvas.Core/UpdateDelta.cs).
// tools/publish.sh uses it where there's no zip command (Windows):  dotnet run tools/zipdir.cs -- <folder> <zip>
using System.IO.Compression;

if (args.Length != 2) { Console.Error.WriteLine("usage: dotnet run tools/zipdir.cs -- <folder> <zip>"); return 2; }
var (folder, zip) = (Path.GetFullPath(args[0]), Path.GetFullPath(args[1]));
File.Delete(zip);
ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
return 0;
