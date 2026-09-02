[English](README.md) | [简体中文](README.zh-CN.md)

# 1Remote Plus

[![version](https://img.shields.io/github/v/release/chaogei/1Remote-Plus?color=Green&include_prereleases&sort=semver)](https://github.com/chaogei/1Remote-Plus/releases)
[![issues](https://img.shields.io/github/issues/chaogei/1Remote-Plus)](https://github.com/chaogei/1Remote-Plus/issues)
[![license](https://img.shields.io/github/license/chaogei/1Remote-Plus?color=blue)](https://github.com/chaogei/1Remote-Plus/blob/main/LICENSE)
![build](https://github.com/chaogei/1Remote-Plus/actions/workflows/build-on-dev-push.yml/badge.svg)

1Remote Plus is a personal remote session manager and launcher for Windows. It keeps every machine you
connect to — RDP, SSH, VNC, Telnet, SFTP, FTP, serial, RemoteApp — in one searchable list, and opens them in
tabs of a single window or in windows of their own.

> **This is a modified version.**
> 1Remote Plus is forked from [1Remote/1Remote](https://github.com/1Remote/1Remote) by Shawn Veck, and
> maintained by [chaogei](https://github.com/chaogei) since 2026. It is not affiliated with, nor endorsed by,
> the upstream project. Please report problems with **this** build at
> [chaogei/1Remote-Plus/issues](https://github.com/chaogei/1Remote-Plus/issues), not upstream.
>
> The GitHub repository is [`chaogei/1Remote-Plus`](https://github.com/chaogei/1Remote-Plus) (renamed from
> `chaogei/1Remote`; the old URL still redirects).

On disk the product keeps its old name: the executable is `1Remote.exe`, the settings and database files are
`1Remote.json` and `1Remote.db`, and the `AppData` folder is `1Remote`. That is deliberate, so an existing
installation keeps its data, its autostart entry and its saved credentials when it is updated.

---

## Contents

- [What is different in this fork](#what-is-different-in-this-fork)
- [Screenshots](#screenshots)
- [Requirements](#requirements)
- [Download and install](#download-and-install)
- [First run](#first-run)
- [Add your first connection](#add-your-first-connection)
- [The main window](#the-main-window)
- [The launcher](#the-launcher)
- [Sessions, tabs and windows](#sessions-tabs-and-windows)
- [What each server entry can do](#what-each-server-entry-can-do)
- [Protocols](#protocols)
- [Credentials and secrets](#credentials-and-secrets)
- [Proxies, jump hosts and port forwarding](#proxies-jump-hosts-and-port-forwarding)
- [Where your data lives](#where-your-data-lives)
- [Backup and restore](#backup-and-restore)
- [Import and export](#import-and-export)
- [Appearance: themes, frosted glass, language](#appearance-themes-frosted-glass-language)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Updates](#updates)
- [Troubleshooting](#troubleshooting)
- [Building from source](#building-from-source)
- [Contributing](#contributing)
- [License](#license)
- [Credits](#credits)
- [Further reading](#further-reading)

---

## What is different in this fork

This fork branched off upstream in August 2026. Everything below is new here; upstream does not have it.

**Fixes**

- **Proxy actually works.** In the version this forked from, a configured proxy silently built no tunnel at
  all. Proxy handling was rewritten around a local relay, and the session now keeps its real target address
  instead of being rewritten to the local tunnel endpoint.
- **Checked-arithmetic overflows** that could crash icon loading, file transfers, credential prompts and
  desktop-shortcut creation.
- **UI freezes.** Credential reads and writes, SSH port-forward setup, SFTP transfers, periodic data-source
  reloads and server-list rebuilds were moved off the UI thread, so a slow network no longer locks the
  window and every hosted session inside it.

**New features**

- **Real proxy support for every protocol** — SOCKS4, SOCKS4a, SOCKS5 and HTTP CONNECT, plus **SSH jump
  hosts** (the equivalent of OpenSSH `ProxyJump`). Because it works through a local relay, RDP and VNC are
  covered too, not just the terminal protocols.
- **Standing port forwards** — local, remote and dynamic (SOCKS) SSH tunnels that run independently of any
  session and can start with the app.
- **Backup and restore**, plus optional upload of the same archive to a **WebDAV** folder.
- **Host identity verification**, on by default: RDP certificates, SSH host keys on SFTP and FTPS
  certificates are checked against a remembered fingerprint, with an explicit per-server opt-out.
- **Passwords from a password manager** at connect time, via a `cmd://` reference to any CLI.
- **Reachability indicator** — an optional timer that marks each visible server green or red.
- **Wake on LAN** from a server's action menu.
- **Import from `~/.ssh/config`**, including its `ProxyJump` directives.
- **Send command** — type one command into several open terminal sessions at once, with saved snippets.
- **Terminal session recording** to a log file.
- Wider search, and sorting by most recently connected.

**Appearance**

- A translucent **acrylic / frosted-glass** design applied across the app, with a panel-opacity slider.
- Font-glyph icons replaced with vector paths, so they stay sharp at any DPI.
- Eight new colour themes tuned for the frosted look: Mica Slate, Nord Frost, Tokyo Night, Catppuccin Mocha,
  Emerald Glass, Cyber Neon, Rose Pine and macOS Light Glass, alongside the classic ones.

**Packaging**

- Releases are built and published from this repository, and the in-app update check reads this fork's
  releases rather than upstream's.

## Screenshots

The images below come from the upstream documentation site. They show what the app does; they predate this
fork's UI rework, so the chrome looks different from a current build.

<img src="https://1remote.github.io/img/home_override/hero1.png" width="800" />

<p align="center">
    <img src="https://1remote.github.io/img/home_override/protocols.png" width="400" />
</p>
<p align="center">
    <img src="https://1remote.github.io/img/home_override/hero2.gif" width="400"/>
</p>
<p align="center">
    ↑ The launcher opening an RDP session, and resizing it
</p>

<p align="center">
    <img src="https://raw.githubusercontent.com/1Remote/PRemoteM/Doc/DocPic/multi-screen.jpg" width="500"/>
</p>
<p align="center">
    ↑ RDP across multiple monitors
</p>

<p align="center">
    <img src="https://raw.githubusercontent.com/1Remote/PRemoteM/Doc/DocPic/RemoteApp/demo.jpg" width="800"/>
</p>
<p align="center">
    ↑ RemoteApp over RDP
</p>

## Requirements

- **Windows, 64-bit.** This is a WPF desktop application; there is no macOS or Linux build.
- **Windows 10 version 2004 (build 19041) or later**, or Windows 11. That is the Windows SDK the project
  targets.
- The frosted-glass backdrop needs **Windows 10 1803 or later**. Without it the app runs fine, just opaque.
- For the smaller of the two downloads, the **.NET 9 Desktop Runtime (x64)**. The self-contained download
  needs nothing installed.

## Download and install

There is no installer. Both downloads are a zip you unpack and run.

1. Open [Releases](https://github.com/chaogei/1Remote-Plus/releases) and pick the newest build.
   The sidebar is not sorted by date — see [Updates](#updates) — so check the version number, not the
   position in the list.
2. Download one of the two assets:

   | Asset | Size | Needs |
   | --- | --- | --- |
   | `1Remote-<version>-net9-x64.zip` | smaller | .NET 9 Desktop Runtime (x64) installed |
   | `1Remote-<version>-net9-x64-self-contained.zip` | larger | nothing — the runtime is inside |

   If you are unsure, take the self-contained one.
3. Unpack the zip to a folder **you can write to** — for example `D:\Apps\1Remote` or a USB stick. Do not
   run it from inside the zip, and avoid `C:\Program Files` unless you intend to run as administrator.
4. Run `1Remote.exe`.

To update, close the app, unpack the new zip over the old folder, and start it again. Your profile, database
and icons are separate files and are not overwritten. To uninstall, delete the folder.

## First run

On first launch the app asks where to keep your data. Both options are offered on the same screen:

- **Portable mode** — everything is stored next to `1Remote.exe`. Pick this for a USB stick, or if you want
  the whole installation to be one folder you can copy.
- **Install for current Windows account** — everything is stored in your Windows `AppData` folder. Pick this
  if the program folder is read-only.

The choice is recorded as a marker file next to the executable, `FORCE_INTO_PORTABLE_MODE` or
`FORCE_INTO_APPDATA_MODE`. **Renaming one to the other switches modes** the next time the app starts; move
your data files yourself if you do that.

Your servers are stored in a local **SQLite** database, `1Remote.db`. It is created for you and needs no
setup. Note the warning the app shows on the Database settings page: the local database is **not encrypted**,
so protect the file the way you protect anything else with passwords in it. If you would rather keep your
list on a server, `Options → Database` can add a **MySQL** or **PostgreSQL** source and use it alongside the
local one.

## Add your first connection

1. Click the **+** button above the server list and choose **Add**. (The same menu holds the import
   entries.)
2. Choose a protocol — RDP, SSH, VNC, Telnet, SFTP, FTP, RemoteApp, Serial or an external app.
3. Fill in **Hostname** and **Port**. The port is prefilled with the protocol default.
4. Fill in **User** and **Password**, or:
   - leave the password blank to be asked each time you connect, or
   - pick a saved entry from the **Credentials Vault**, or
   - enter `cmd://<command>` to pull the password from a password manager at connect time (see
     [Credentials and secrets](#credentials-and-secrets)).
5. Optional, on the **Common** group:
   - **Tags** — type a name and press Enter. Tags are how the list is grouped and filtered.
   - **Icon** and **Note** — click the icon to replace it with your own image.
6. Optional, on the other groups, depending on protocol:
   - **Connection** — alternative addresses and accounts, tried in order when the primary is unavailable;
     a MAC address for Wake on LAN; the proxy this server should use.
   - **Display** (RDP) — window / single-monitor / multi-monitor full screen, resolution and scaling,
     performance level.
   - **Advanced Settings** (RDP) — clipboard, drives, printers, smart cards, sound, key combinations, RD Gateway.
   - **Advanced Settings** (SSH) — private key, startup command, port-forwarding rules, agent forwarding, X11.
   - **Script before connect / after disconnected** — a command line or `.bat` run around the session.
7. **Save**. Double-click the new entry to connect.

The very first time you reach a host, its identity is unknown and the app shows its fingerprint and asks
whether to remember it. Accepted fingerprints go into `.locality/known_hosts.json`; if one later changes you
are warned loudly, because that means either the server was rebuilt or someone is intercepting the
connection. A server you cannot fix — a lab box with a self-signed certificate, say — can opt out with **Do
not verify this host's identity** in its editor.

## The main window

**Views.** The main menu's **View** submenu switches between **List**, **Card** and **Tree**. Tree groups by
tag folders; List and Card are flat and can be grouped by a pinned tag.

**Search.** Press **Ctrl + F** to jump to the filter box, then type. The box matches names, addresses,
usernames and notes, and understands tag syntax:

| Typed | Meaning |
| --- | --- |
| `web` | anything matching "web" |
| `#prod` | only servers tagged `prod` |
| `#prod db` | tagged `prod` **and** matching "db" |
| `-#prod` | everything **except** servers tagged `prod` |

Extra keyword matchers — pinyin and initials, for instance — can be enabled under `Options → Launcher →
Keyword-Matchers`. They apply to this box as well as to the launcher.

**Tags.** Right-click a tag for its menu: **Pin** it to the header strip, **Rename**, **Connect** to
everything under it at once, or **Delete**. On a tag chip on a row, plain click sets it as the filter,
**Ctrl + click** adds it to the include list, **Alt + click** adds it to the exclude list.

**Sorting.** The main menu's **Sorting** submenu orders the list by Id (your own drag order), protocol, name,
address, or **Recently connected**. Protocol, name and address toggle between ascending and descending.

**Reachability.** Turn on `Options → General → Show whether each server is reachable` and the app opens a
connection to each visible server's port on a timer (60 s by default) and marks it green or red. It uses
whatever proxy the server is configured with; servers behind an SSH jump host are not probed. It is off by
default because it is traffic to every configured host on a schedule.

**Tray.** Closing the window minimises to the tray by default (`Options → General → Close button behavior`).
The tray menu can list recently used sessions, reset the main window position, open the issue tracker, show
About and exit.

## The launcher

The launcher is the reason to keep this app running. Press its hotkey anywhere in Windows and a search box
appears over whatever you were doing.

- **Alt + M** — open it. Change the hotkey at `Options → Launcher`, or switch it off there entirely.
- Type to filter. The same `#tag` syntax as the main window works here.
- **↑ / ↓** — move the selection. **PageUp / PageDown** move it by five.
- **Enter** — connect to the selected server.
- **→** — open that server's action list (new window, alternative credentials, a specific runner, copy
  password, Wake on LAN, and the rest). **←** goes back.
- **Tab** — switch between the server list and **Quick Connect**.
- **Esc**, or clicking elsewhere — hide the launcher.

**Quick Connect** is the launcher's second mode: type an ad-hoc target such as `192.168.0.100:3389` and
connect without creating an entry first. Previous quick connections are listed underneath and can be picked
with **↑ / ↓**. Ticking **Remember the information above** turns one into a saved server; that option can be
disabled entirely at `Options → Launcher`.

`Options → Launcher` also controls whether notes and credentials are shown in the results, and which
**Keyword-Matchers** are active. For the hotkey to be there when you want it, turn on
`Options → General → Run automatically at OS startup (minimized)`.

## Sessions, tabs and windows

By default every session opens as a tab in one shared session window; drag a tab out to tear it into its own
window, or drop it back onto another window's tab strip to merge. `Options → General → Always open in new
window` changes the default, and any entry can be opened once in a new window from its action menu
(**Connect (New window)**).

Inside a session window:

- **Ctrl + 1 … Ctrl + 9** — jump to that tab.
- **F11**, or right-clicking the maximise button — put the *window* full screen. The toolbar's full-screen
  button instead moves the current session into a dedicated full-screen window, which is the mode
  multi-monitor RDP uses.
- **Middle-click a tab** — close it. The tab strip can also show a per-tab icon, reconnect and close button
  (`Options → General`).
- **Send command** — the toolbar button opens a dialog that types one command into every terminal session
  you tick. It works with SSH, Telnet and serial sessions; RDP and VNC cannot be typed into this way.
  Commands you use often can be saved as named snippets.
- A dropped RDP session shows a *Reconnecting…* counter and retries by itself; other protocols can show a
  **Reconnect** button on the tab.
- SFTP and FTP sessions get a file browser with **F5** refresh, **F2** rename, **Del** delete,
  **Ctrl + S** download and **Ctrl + V** upload from the clipboard.
- **Session recording** — `Options → General → Record terminal session output to a file` writes everything
  SSH, Telnet and serial sessions print into `.sessionlogs/` (or a folder you choose). Off by default: a
  session log holds whatever crossed the screen, which regularly includes output nobody meant to keep.

## What each server entry can do

Right-click a row (or press **→** in the launcher):

| Action | Notes |
| --- | --- |
| Connect | Double-click does the same |
| Connect (New window) | Bypasses the shared tab window once |
| Connect (with alternative `<name>`) | Uses one of the entry's alternative accounts |
| Connect (via `<runner>`) | Uses PuTTY, KiTTY, WinSCP, FreeRDP … instead of the built-in client |
| Edit / Duplicate / Delete | Multi-select supported, including bulk editing |
| Copy hostname / username / password | Copying the password asks for Windows credential verification |
| Wake on LAN | Shown when the entry has a MAC address |
| Create desktop shortcut | A `.lnk` that opens this session directly |
| Open SFTP | On SSH entries, opens a file browser to the same host |
| Export \*.rdp | On RDP entries |

## Protocols

| Protocol | Client used by default | Notes |
| --- | --- | --- |
| **RDP** | Built-in `AxMsRdpClient`, or `mstsc.exe` mode | Multi-monitor, HiDPI, RD Gateway, drive/printer/clipboard redirection |
| **RemoteApp** | RDP | Publishes a single remote application instead of a desktop |
| **SSH** | PuTTY (bundled) | Private keys, startup command, port forwarding, agent and X11 forwarding |
| **Telnet** | PuTTY | |
| **Serial** | PuTTY | Local COM ports |
| **VNC** | Built-in VNC client | |
| **SFTP** | Built-in file browser | OpenSSH-format keys with the internal client |
| **FTP** | Built-in file browser | FTPS certificates take part in host verification |
| **App** | Any executable | Presets for Chrome, NoMachine, FreeRDP, PuTTY, Windows Terminal, WinSCP, UltraVNC, TightVNC |

Any protocol's client can be swapped at `Options → Protocol`: point a runner at your own executable, give it
a command-line template and environment variables, and choose whether its window is hosted inside a
1Remote Plus tab or left standalone. See the upstream [runner documentation](https://1remote.github.io/usage/protocol/runner/)
— it still applies here.

## Credentials and secrets

**Credentials Vault** (`Options → Credentials Vault`) holds named username/password/private-key entries.
A server entry can reference one by name instead of storing its own copy, so rotating a password is one edit
rather than twenty. Each server can also carry **alternative** credentials, offered from its action menu.

**Ask every time.** Leave a password blank and you are prompted at connect time.

**From a password manager.** Enter `cmd://<command>` in a password field and the command is run when you
connect; whatever it prints on stdout is used as the secret. This works with any CLI:

```
cmd://bw get password my-server
cmd://keepassxc-cli show -a Password ~/vault.kdbx my-server
cmd://pass show servers/my-server
cmd://op read op://Private/my-server/password
```

The command runs through `cmd.exe`, so pipes and quoting behave as they do in a shell. A trailing newline is
stripped, the result is cached for the rest of the session (so a vault that prompts for a fingerprint only
asks once), and a 20-second timeout applies. There is a **Test** button next to the field that runs the
command without caching and reports how many characters came back.

**Viewing a stored password** — from the vault or the copy-password action — requires Windows credential
verification when `Options → General → Security` has it switched on.

**A caution.** The local database and the profile are not encrypted, and neither is a backup archive. Store
them accordingly.

## Proxies, jump hosts and port forwarding

Define proxies once at `Options → Proxy`, then pick one per server in the server editor. Everything goes
through a local relay, so **all protocols are covered, RDP and VNC included** — not just the ones with
native proxy support.

| Type | Notes |
| --- | --- |
| SOCKS5 | IPv4, IPv6, remote name resolution, username/password auth |
| SOCKS4 | Names are resolved locally; the proxy only ever sees an IPv4 address |
| SOCKS4a | SOCKS4 with remote name resolution |
| HTTP CONNECT | Optional Basic proxy authentication |
| SSH jump host | The session travels inside a channel the SSH server opens to the target, like OpenSSH `ProxyJump`. Private key first, then password. |

Each proxy has a **Test** button: give it a `host:port` target and it reports whether the proxy reached it.
**Connect directly when the address is this machine** skips the proxy for local addresses. If a proxy a
server points at has been deleted or is incomplete, connecting offers to fall back to a direct connection
rather than failing silently.

**Port forwarding** (`Options → Port forwarding`) runs standing SSH tunnels that are independent of any
session. Each one points at an **SSH jump host** entry from the Proxy page — define the bastion once and
both features use it, sharing a single login.

| Direction | What it does |
| --- | --- |
| Local | Listens here, sends to `destination:port` through the SSH host |
| Remote | Listens on the SSH host, sends back to a destination reachable from here |
| Dynamic | A SOCKS proxy on a local port, no fixed destination |

Forwards can **start automatically when the app launches**. Binding to `0.0.0.0` instead of `127.0.0.1`
publishes the tunnel to your whole network, and the editor says so.

Per-session SSH forwards also exist, in an SSH entry's **Advanced Settings** group: one rule per line, either
`L 8080 intranet:80` / `R 9000 localhost:9000` / `D 1080`, or PuTTY's own `L8080=intranet:80` syntax.

## Where your data lives

Relative to the folder chosen at first run — next to `1Remote.exe` in portable mode, or your `AppData`
folder otherwise.

| Path | Contents |
| --- | --- |
| `1Remote.json` | All settings: proxies, port forwards, theme, launcher hotkey, WebDAV destination, saved commands |
| `1Remote.db` | The local SQLite database: servers, tags, credentials vault |
| `1Remote.dataSources.json` | Additional MySQL / PostgreSQL sources |
| `Protocols/` | Custom protocol runners |
| `.locality/` | Window positions, connection history, `known_hosts.json` |
| `.icons/` | Server icons |
| `.logs/1Remote.log.md` | Application log |
| `.sessionlogs/` | Recorded terminal output, when that is enabled |
| `PuTTY/`, `KiTTY/` | Settings for those runners |

Only one instance runs at a time; starting a second one activates the first.

## Backup and restore

`Options → Backup` packs everything in the table above — servers, credentials vault, all settings, tags,
connection history, custom runners and icons — into a single archive.

- **Create a backup** writes the archive wherever you choose.
- **Restore from a backup** replaces your current data with the archive's, then closes the app; start it
  again to use the restored data. This cannot be undone.
- **Off-machine copy (WebDAV)** uploads the same archive to a WebDAV folder — Nextcloud, ownCloud, Synology
  and most NAS boxes speak it. Give it a folder URL such as
  `https://cloud.example.com/remote.php/dav/files/me/1Remote/` plus a username and password, then use
  **Back up and upload**, **Refresh list** and **Download and restore**.

Servers kept in a remote MySQL or PostgreSQL source stay in that database; the backup only records the
details needed to reach it.

The archive contains your configuration **unencrypted**. Use an HTTPS destination and a folder only you can
read.

## Import and export

From the **+** menu above the server list:

- **Import json** — a list exported from 1Remote Plus, or from upstream 1Remote.
- **Import mRemoteNG csv** — see the upstream
  [notes on mRemoteNG](https://1remote.github.io/usage/overview/#importing-from-mremoteng).
- **Import from `~/.ssh/config`** — reads your OpenSSH config; any `ProxyJump` directives are turned into
  SSH jump host entries on the Proxy page automatically.
- **Import \*.rdp** — a Remote Desktop file.
- **Import PRemoteM db** — a database from the app's earlier name.

**Export** writes the currently selected servers to JSON. Individual RDP entries can also be exported as
`.rdp` from their action menu.

## Appearance: themes, frosted glass, language

`Options → Theme` picks a palette, a font family and a font size (10–20).

Frosted-glass palettes: **Dark**, Mica Slate, Nord Frost, Tokyo Night, Catppuccin Mocha, Emerald Glass,
Cyber Neon, Rose Pine, macOS Light Glass.
Classic palettes: Light, PRemoteM, SecretKey, Greystone, Asphalt, Wine, Forest, Soil and more. Any of them
can be edited colour by colour.

**Frosted glass** has its own switch — *Blur the desktop behind panels (Windows 10 1803 and later)* — and a
**Panel opacity** slider. Below roughly 120 the text starts losing contrast against a busy desktop; above
roughly 230 the blur stops being visible.

Two honest caveats about the frost. It turns itself off in a **remote desktop session** and under **high
contrast**, because DWM would sample the remote framebuffer instead of the real desktop and the panels would
wash out; the app snaps to opaque surfaces instead, which stay readable. And the **session window, the
full-screen session window and the crash reporter stay opaque by design** — the first two embed a
remote-desktop HWND that DWM blur fogs over, and the third draws a transparent halo that would frost into a
square bloom.

**Language.** `Options → General → Language` ships English, 简体中文, 繁體中文, 日本語, Deutsch, Français,
Español (AR), Galego, Italiano, Polski, Português (BR/PT), Русский and Čeština.

## Keyboard shortcuts

**Anywhere in Windows**

| Key | Action |
| --- | --- |
| `Alt + M` | Open the launcher (configurable at `Options → Launcher`) |

**Launcher**

| Key | Action |
| --- | --- |
| `↑` / `↓` | Move the selection |
| `PageUp` / `PageDown` | Move by five |
| `Enter` | Connect |
| `→` | Open the selected server's action list |
| `←` | Back, or show the note field |
| `Tab` | Switch between the server list and Quick Connect |
| `Esc` | Hide the launcher |

**Main window**

| Key | Action |
| --- | --- |
| `Ctrl + F` | Focus the search box |

**Session window**

| Key | Action |
| --- | --- |
| `Ctrl + 1` … `Ctrl + 9` | Switch to that tab |
| `F11` | Toggle full screen |

**SFTP / FTP browser**

| Key | Action |
| --- | --- |
| `F5` | Refresh |
| `F2` | Rename |
| `Del` | Delete |
| `Backspace` | Parent folder |
| `Ctrl + S` | Download to… |
| `Ctrl + V` | Upload from the clipboard |

## Updates

The app checks for a new version once an hour against **this fork's** releases page,
`https://github.com/chaogei/1Remote-Plus/releases`. It does not look at upstream. Turn it off at
`Options → General → Do not check for new version`. There is no auto-updater; the check just tells you a
newer build exists and links to it.

**A quirk worth knowing when you download manually.** GitHub orders the releases sidebar by tag name as a
*string*, so `v1.3.0.12-beta` sorts between `v1.3.0.1-beta` and `v1.3.0.2-beta` rather than at the top. The
newest build is not necessarily the first one listed. Sort by publication date, or read the version numbers
and take the highest. (The in-app check already compares numerically, so it is not fooled by this. The
version badge at the top of this page uses `sort=semver` for the same reason.)

## Troubleshooting

**A connection fails only when a proxy is set.** Open `Options → Proxy`, select the entry, put the target in
**Test target** as `host:port`, and press **Test**. It reports whether the proxy itself reached the target.

**"Unverified host" on every connect.** The fingerprint is not being remembered — usually because the app
cannot write `.locality/known_hosts.json`. Check that the data folder is writable.

**Windows' own "the identity of the remote computer cannot be verified" warning.** The app checks the RDP
certificate itself before the session starts and remembers it per address *and* port, so Windows only gets
to ask when that check could not run: through an RD Gateway, in mstsc mode, or against a server still on the
legacy RDP security layer. Windows files its answer under the address alone, which is why ticking "don't ask
me again" never sticks when one hostname forwards a port per machine.

**The window has no frosted background.** Expected inside a remote desktop session, under high contrast, on
Windows 10 before 1803, and when the theme's frosted-glass switch is off. It is also expected on the session
window itself, which is opaque by design.

**"We don't have write permissions for…" at startup.** The folder holding the profile is read-only. Move the
app somewhere writable, or restart it and choose *Install for current Windows account* to keep data in
`AppData`.

**Getting more detail.** Set `Options → General → Log level` to a more verbose setting and read
`.logs/1Remote.log.md`. Attach the relevant part to an issue.

## Building from source

See **[DEVELOP.md](DEVELOP.md)**. In short: Visual Studio 2022 on Windows, restore NuGet packages, build
`1Remote.sln`. Release builds are produced by
[`.github/workflows/build-on-dev-push.yml`](.github/workflows/build-on-dev-push.yml), which publishes the
same two zips linked above.

Note that a fork build does not inherit the upstream repository's build secrets. Telemetry is inert without
them, and the string-encryption salt falls back to a publicly known constant — so a build you make yourself
must not be pointed at a password store created by an official release.

## Contributing

Bug reports and ideas are welcome in [issues](https://github.com/chaogei/1Remote-Plus/issues). Pull requests are
welcome too; please read [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) first.

Issues about **this** build belong here, not upstream. Behaviour that also exists in
[1Remote/1Remote](https://github.com/1Remote/1Remote) is better reported there, where it can be fixed for
everyone.

## License

GPL-3.0. See [LICENSE](LICENSE).

Copyright (C) Shawn Veck and the 1Remote contributors, for the original work.
Modifications in this fork are copyright (C) chaogei.

## Credits

- Original author: [Shawn Veck](https://github.com/VShawn) — [1Remote/1Remote](https://github.com/1Remote/1Remote)
- <a href="http://www.jetbrains.com/resharper/"><img src="http://www.tom-englert.de/Images/icon_ReSharper.png" alt="ReSharper" width="24" height="24" /></a> ReSharper

## Further reading

This fork has not forked the documentation site, so upstream's docs remain the reference for everything that
was not changed here:

- [Quick start](https://1remote.github.io/usage/quick-start/)
- [Overview](https://1remote.github.io/usage/overview/)
- [Custom runners](https://1remote.github.io/usage/protocol/runner/)
- [RemoteApp](https://1remote.github.io/usage/protocol/especial/remoteapp/)
- [NoMachine and other apps](https://1remote.github.io/usage/protocol/especial/app/)

Where those pages and this README disagree, this README describes the fork.
