# Sharing your own build

A DLL built straight from source is a development build: it starts any `orchid-host` it finds and logs `Development build of OrchidUI: ... started without a checksum check`. That's fine on your own machine, but if you give your build to other people, pin it to your host files first. A pinned DLL has the SHA-256 of each host built into it and refuses to start anything else:

```
orchid-host at ... isn't a build this OrchidUI.dll trusts (SHA-256 ...). Download the matching release.
```

The release workflow (`.github/workflows/release.yml`) does all of this for the four official hosts.

## Making a pinned build

Publish every host you're going to ship first, once per platform:

```bash
dotnet publish src/OrchidUI.Host -c Release -r win-x64 --self-contained -p:DebugType=none -o dist/win-x64
dotnet publish src/OrchidUI.Host -c Release -r linux-x64 --self-contained -p:DebugType=none -o dist/linux-x64
dotnet publish src/OrchidUI.Host -c Release -r osx-arm64 --self-contained -p:DebugType=none -o dist/osx-arm64
```

If you sign a host (with `codesign` on macOS, say), do it now. Any change to the file afterwards breaks the match.

Then write their hashes into the DLL's source, build the DLL, and put the source file back so the hashes don't get committed:

```bash
build/write-host-hashes.sh dist/win-x64/orchid-host.exe dist/linux-x64/orchid-host dist/osx-arm64/orchid-host
dotnet build src/OrchidUI -c Release -f net481
git checkout src/OrchidUI/Session/HostHashes.g.cs
```

The DLL and the hosts are now one set. If you rebuild a host, rebuild the DLL too.

## Checking it

Open a window with your DLL and host. It should open, and Streamer.bot's log should have no `Development build` line.

Then change one byte of the host and try again. It should be refused:

```bash
cp OrchidUI/orchid-host /tmp/orchid-host.bak
printf x >> OrchidUI/orchid-host        # open a window now
cp /tmp/orchid-host.bak OrchidUI/orchid-host
```

## Checksums for your users

Publish a checksum list next to your downloads:

```bash
sha256sum OrchidUI.dll orchid-host-*.zip > SHA256SUMS
```

People can check it with `sha256sum -c SHA256SUMS` on Linux, `shasum -a 256 -c SHA256SUMS` on macOS, or `Get-FileHash OrchidUI.dll` in PowerShell on Windows.
