# LivePaper

LivePaper is a Wayland-native live wallpaper engine where wallpapers are ordinary web pages.
It currently ships with a KWin platform backend and a PipeWire audio backend. These are implementations of replaceable interfaces, so support for other compositors and audio systems can be contributed without changing the wallpaper API.

## Features

- Your wallpaper is just a web page, so you can build it with regular HTML, CSS and JavaScript.
- You can import web wallpapers from Wallpaper Engine without touching the original Workshop files.
- Wallpapers can react to clicks, scrolling and the position of your mouse.
- Audio-reactive web wallpapers can visualize the system's current playback.
- LivePaper can pause rendering or mute audio when a fullscreen or maximized window covers the wallpaper.
- Changes to the wallpaper manifest or LivePaper config are picked up automatically.
- Wallpapers run in their own process, and the daemon starts them again if they crash.

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
packages/
  livepaper-web/       TypeScript API for native wallpaper authors
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

The only tested environment is my machine, so x86-64 CachyOS and KDE Plasma on Wayland. If you have the same configuration, you can
install dependencies to build and operate LivePaper with:

```sh
sudo pacman -S --needed clang dotnet-sdk wpewebkit
```

Other configurations will have to figure out what they need. Sorry!

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
livepaper doctor
livepaper dep-fix wallpaper-engine.example --dependency /path/to/workshop/dependency
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

If a web wallpaper loads files from another Workshop item, pass that item's
directory with `--dependency`. LivePaper copies it into the import under its
Workshop directory ID:

```sh
dotnet run --project src/LivePaper.Daemon -- \
  --import-wallpaper /path/to/steamapps/workshop/content/431960/1234567890 \
  --dependency /path/to/steamapps/workshop/content/431960/9876543210
```

Run `livepaper doctor` to check rendering libraries, layer-shell support,
PipeWire, installed manifests, entry files, and dependencies. To add a missed
dependency, pass the installed wallpaper ID and the dependency's Workshop
directory:

```sh
livepaper dep-fix wallpaper-engine.kei \
  --dependency /path/to/steamapps/workshop/content/431960/9876543210
```

The daemon takes care of starting the renderer, restarting it if it crashes,
and stopping it when the daemon exits:

```sh
dotnet run --project src/LivePaper.Daemon
dotnet run --project src/LivePaper.Daemon -- --wallpaper /path/to/wallpaper
```

## License

LivePaper is available under the [MIT License](LICENSE).
