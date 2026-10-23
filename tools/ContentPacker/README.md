# Content packaging

`Release.ps1` runs this tool after publishing and before creating the distribution ZIP.
It packs every file under `src/CAERO/Content/`, including relative filenames, into an
in-memory ZIP and encrypts it with AES-256-GCM. A fresh 12-byte nonce is generated for
each package; the version header is authenticated and the authentication tag is 16 bytes.
The tool checks every file byte-for-byte after decryption and checks tamper rejection
before writing `Content.dat`. Published loose Content files are then removed by the script.

The runtime reads `Content.dat` next to the executable, caches decoded assets in memory,
and loads images from memory. It never extracts plaintext assets onto disk. If the package
exists but is invalid, loading fails rather than falling back to loose files.
Without a package, development builds use the editable loose Content files as before.

The codec is shared with the game through a linked source file. Its embedded key is
an obstacle to casual inspection, not protection against executable or memory analysis.
Music, shaders and save files are outside the scope of this package.

Manual invocation from the repository root:

```powershell
dotnet run --project tools/ContentPacker -c Release -- src/CAERO/Content tmp/Content.dat
```

Normal `dotnet build` / `dotnet publish` do not package assets; use `Release.ps1` for
distribution. `Clean.bat` and `Release.bat` are unchanged.
