# 1Remote

[![version](https://img.shields.io/github/v/release/chaogei/1Remote?color=Green&include_prereleases)](https://github.com/chaogei/1Remote/releases)
[![issues](https://img.shields.io/github/issues/chaogei/1Remote)](https://github.com/chaogei/1Remote/issues)
[![license](https://img.shields.io/github/license/chaogei/1Remote?color=blue)](https://github.com/chaogei/1Remote/blob/main/LICENSE)
![](https://github.com/chaogei/1Remote/actions/workflows/build-on-dev-push.yml/badge.svg)

1Remote is a modern personal remote session manager and launcher. It is a single place to manage all your remote sessions supporting a number of different protocols.

> **This is a modified version.**
> Forked from [1Remote/1Remote](https://github.com/1Remote/1Remote) by Shawn Veck, and maintained by [chaogei](https://github.com/chaogei) since 2026.
> It is not affiliated with, nor endorsed by, the upstream project. Please report issues with this build
> [here](https://github.com/chaogei/1Remote/issues) rather than upstream.
>
> 本仓库是 1Remote 的修改版本，基于原作者 Shawn Veck 的 [1Remote/1Remote](https://github.com/1Remote/1Remote) 分支而来，由 [chaogei](https://github.com/chaogei) 维护。
> 与上游项目无隶属关系，本版本的问题请提交到[本仓库的 issues](https://github.com/chaogei/1Remote/issues)。

## Changes in this fork

- Fixed remote connections failing whenever a proxy was configured, and reworked proxy handling so the
  session keeps its real address instead of being overwritten by the local tunnel endpoint.
- Fixed a set of checked-arithmetic overflows that could crash icon loading, file transfers, credential
  prompts and shortcut creation.
- Reworked the UI around a translucent acrylic design language, replaced font-glyph icons with vector
  paths, and added eight colour themes tuned for the frosted-glass look.

## Features

- Supports RDP, SSH, VNC, Telnet, (S)FTP, [RemoteApp](https://1remote.github.io/usage/protocol/especial/remoteapp/), [NoMachine and other app](https://1remote.github.io/usage/protocol/especial/app/)
- Quick and convenient remote session launcher (Alt + M)
- Multi-screen and HiDPI RDP connection (Test on **Win10 + 4k monitor *2** RDP TO **Win2016**)
- Detailed connection configuration: tags, icons, colors, connection scripts etc.
- Multiple languages, themes and tabbed interface
- SOCKS4 / SOCKS5 / HTTP CONNECT proxy support for any protocol
- [Import connections from mRemoteNG](https://1remote.github.io/usage/overview/#importing-from-mremoteng)
- Customizable runners, in SFTP \ FTP \ VNC \ etc. protocols, you can replace the internal runner with your favourite tools. [wiki](https://1remote.github.io/usage/protocol/runner/)
- Portable - just unpack and run

## Installation

Latest version: see [Releases](https://github.com/chaogei/1Remote/releases). Download the zip, unpack, and run.

Upstream documentation still applies to most features:
[Quick start](https://1remote.github.io/usage/quick-start/)

## Overview

<img src="https://1remote.github.io/img/home_override/hero1.png" width="800" />

<p align="center">
    <img src="https://1remote.github.io/img/home_override/protocols.png" width="400" />
</p>
<p align="center">
    <img src="https://1remote.github.io/img/home_override/hero2.gif" width="400"/>
</p>

<p align="center">
    ↑ Launcher(Alt + M) open RDP connection & resizing
</p>

<p align="center">
    <img src="https://raw.githubusercontent.com/1Remote/PRemoteM/Doc/DocPic/multi-screen.jpg" width="500"/>
</p>

<p align="center">
    ↑ RDP with Multi-monitors
</p>

<p align="center">
    <img src="https://raw.githubusercontent.com/1Remote/PRemoteM/Doc/DocPic/RemoteApp/demo.jpg" width="800"/>
</p>

<p align="center">
    ↑ RemoteApp via RDP
</p>

## Contributing

Bug reports and ideas are welcome in [issues](https://github.com/chaogei/1Remote/issues). If you want to
build from source, see [DEVELOP.md](DEVELOP.md).

## License

GPL-3.0. See [LICENSE](LICENSE).

Copyright (C) Shawn Veck and the 1Remote contributors, for the original work.
Modifications in this fork are copyright (C) chaogei.

## Credits

- Original author: [Shawn Veck](https://github.com/VShawn) — [1Remote/1Remote](https://github.com/1Remote/1Remote)
- <a href="http://www.jetbrains.com/resharper/"><img src="http://www.tom-englert.de/Images/icon_ReSharper.png" alt="ReSharper" width="24" height="24" /></a> ReSharper
