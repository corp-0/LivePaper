# LivePaper

LivePaper is a Wayland-native live wallpaper engine where wallpapers are ordinary web pages.
Currently it only supports KWin, but you should be able to contribute an implementation for your own compositor.

## Why

The Wallpaper engine plugin on KDE store only semi-works, and it will crash your entire desktop when it doesn't. I wanted instead something
reliable and simple. So I built this little app.

I decided using web wallpaper because they have a ton of benefits, such as being responsive and adapt to your resolution, provide interactions and react to data. There
are also plenty of Wallpaper Engine wallpapers that you can just import and expect them to work, because the underlying mechanism is the same.

## Repository layout

```text
src/
  LivePaper.Daemon/    Process supervision and system integrations
  LivePaper.Renderer/  Direct Wayland/WPE renderer, one surface per process
  LivePaper.Protocol/  Versioned messages and wallpaper manifest types
  LivePaper.Platform/  Linux and Wayland integration boundaries
tests/
  LivePaper.Protocol.Tests/
  LivePaper.Platform.Tests/
docs/                  Architecture and protocol decisions
samples/               Wallpapers used during development
```

## Install

I might add pre-built releases and package for AUR, but I'm currently not comfortable doing so since this is in early development.
If you want to try it, you can install the prerequisites and build it yourself.

## Pre-requisites

For now, you need an x86-64 Linux system running KWin on Wayland. The renderer
uses WPE WebKit with its FDO backend.

You also need the .NET 10 SDK and a few native build tools. On Arch Linux and
CachyOS, you can install everything with:

```sh
sudo pacman -S --needed base-devel clang dotnet-sdk libglvnd wayland wpewebkit wpebackend-fdo
```

Other distributions might work, but I haven't tested them yet and their package
names will be different.

## User installation

There is a convenience script that builds and publishes LivePaper, installs it
for your user, and prepares the config and systemd service so you can use it
right away. It has only been tested on my machine, so you might need to tweak it
for your setup:

```sh
./scripts/install-user.sh
```

The installer keeps releases under `~/.local/lib/livepaper`, puts the launcher
at `~/.local/bin/livepaper`, and leaves your config alone if it already exists.
You can then use:

```sh
livepaper status
livepaper logs
livepaper restart
livepaper import /path/to/workshop/project
```

Run `./scripts/uninstall-user.sh` to remove the binaries and service. It keeps
your imported wallpapers and config.

Wallpaper metadata lives in `manifest.toml`, and LivePaper's config uses TOML as
well.

You can pick the wallpaper and configure LivePaper behavior at
`config/livepaper.toml`:

```toml
[wallpaper]
id = "wallpaper-engine.3650880224"

[visibility]
poll_interval_ms = 250
disable_rendering_when = "fully_covered"
mute_audio_when = "partially_covered"
```

Both settings accept `never`, `fully_covered`, `partially_covered`, or `always`.

## Build manually

You can build, test, and run the daemon with:

```sh
dotnet build
dotnet test
dotnet run --project src/LivePaper.Daemon
```

The final binaries use Native AOT, so you won't need the .NET runtime after
installing them. For now, the only publish target is Linux x64:

```sh
dotnet publish src/LivePaper.Daemon -c Release -r linux-x64
dotnet publish src/LivePaper.Renderer -c Release -r linux-x64
```

If you just want to check whether your compositor is supported without loading
a wallpaper:

```sh
dotnet run --project src/LivePaper.Renderer -- --probe
```

Running the renderer without arguments opens the sample wallpaper. You can also
point it at another wallpaper directory:

```sh
dotnet run --project src/LivePaper.Renderer
dotnet run --project src/LivePaper.Renderer -- --wallpaper /path/to/wallpaper
```

Wallpaper Engine projects need to be imported first. The import copies the
project into `$XDG_DATA_HOME/livepaper/wallpapers` (usually
`~/.local/share/livepaper/wallpapers`) and generates a `manifest.toml` file. It
doesn't copy `project.json` or change anything in your Steam Workshop folder.

```sh
dotnet run --project src/LivePaper.Daemon -- \
  --import-wallpaper /path/to/steamapps/workshop/content/431960/1234567890
```

You can use `--import-destination` to pick another wallpaper library. LivePaper
won't overwrite a wallpaper you already imported.

The daemon takes care of starting the renderer, restarting it if it crashes,
and stopping it when the daemon exits:

```sh
dotnet run --project src/LivePaper.Daemon
dotnet run --project src/LivePaper.Daemon -- --wallpaper /path/to/wallpaper
```
