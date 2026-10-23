using Caero;

if (args.Length != 2 || !Directory.Exists(args[0]))
    throw new ArgumentException("Usage: ContentPacker <source Content directory> <output Content.dat>");

string source = Path.GetFullPath(args[0]);
string destination = Path.GetFullPath(args[1]);
var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
    .ToDictionary(path => Path.GetRelativePath(source, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);
if (files.Count == 0) throw new InvalidDataException("Content directory is empty.");
if (files.Keys.Any(name => Path.GetFullPath(Path.Combine(source, name)).Equals(destination, StringComparison.OrdinalIgnoreCase)))
    throw new ArgumentException("Output must not overwrite a source asset.");
byte[] package = ContentArchive.Encode(files);
var restored = ContentArchive.Decode(package);
if (restored.Count != files.Count || files.Any(file => !restored[file.Key].AsSpan().SequenceEqual(file.Value)))
    throw new InvalidDataException("Content package verification failed.");
// Every build verifies that modified headers, nonces, tags and payloads fail closed.
foreach (int offset in new[] { 0, 8, 20, package.Length - 1 })
{
    var damaged = (byte[])package.Clone(); damaged[offset] ^= 1;
    bool rejected = false;
    try { ContentArchive.Decode(damaged); } catch (InvalidDataException) { rejected = true; }
    if (!rejected) throw new InvalidDataException("Content package integrity check failed.");
}
if (ContentArchive.Encode(files).AsSpan().SequenceEqual(package))
    throw new InvalidDataException("Content package nonce was reused.");
File.WriteAllBytes(destination, package);
Console.WriteLine($"CONTENT PACK: {files.Count} assets, {package.Length:N0} bytes; round-trip and integrity checks passed.");
