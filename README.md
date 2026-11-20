# Forever Launcher

WPF launcher for the WoW Classic 1.60.1 client on private servers. Starts the game with your
server's portal and swaps in your server's public key. Works for a server on this PC and for
a remote server, with **no patching of the game's code or loader**.

## Build
    cd src
    dotnet build -c Release

Output: `src/bin/Release/net48/ForeverLauncher.exe`

## Configure
Edit `servers.json` next to the exe (an example is written on first run):

| field | meaning |
|---|---|
| `name` | shown in the launcher |
| `portal` | `<name>.actual.battle.net[:port]` - the Battle.net RPC port (default 1119) |
| `address` | IP or host name of the server. The portal name is mapped to it in the hosts file. Use `127.0.0.1` for a server on this PC |
| `caFile` | PEM of the CA that signed the server's portal certificate (relative to `servers.json`) |
| `blockServices` | `true` (default): map Blizzard telemetry / version / news hosts to `127.0.0.1` so nothing reaches Blizzard |
| `publicKey`, `regionGroup`, `realms`, ... | as before |

On **Play** the launcher checks the hosts file and the certificate store. If something is
missing it asks for administrator rights **once** (UAC), writes the lines it needs
(marked `# ForeverLauncher`), and trusts the CA. Nothing happens, and no prompt appears,
when everything is already in place.

### Why not patch the client?
On build 1.60.1.70888 the loader raises `0xC00000E5` about 31 s after start when URL
literals are patched in memory. An untouched client runs fine, so the launcher redirects by
name (hosts) and trust (certificate) instead, and only rewrites the 12 Ed25519 keys.

### Safety of the CA
The launcher only installs a CA that carries a **NameConstraints** extension, so it cannot
vouch for other websites. Generate yours with e.g.
`nameConstraints = critical,permitted;DNS:actual.battle.net` (add `DNS:localhost` /
`IP:127.0.0.1` for a local server).

## What a server operator must provide
- A portal name under `actual.battle.net` (e.g. `play.actual.battle.net`).
- bnetd certificate with **CN and a DNS SAN equal to that name**, signed by the
  name-constrained CA (the client checks the common name; IP SANs are not enough).
- bnetd `Forever.WebHost` set to the **same name**: the login form returns an `srp_url`
  built from it and the client follows it.
- Ports reachable from players: `BattlenetPort` (portal), `LoginREST.ForeverPort`, and the
  world port from `Forever.WorldPort`.
- Give players the CA file (`ca.pem`), the portal name, the server's address and public key.

## Troubleshooting (client error -> cause)
| error | cause |
|---|---|
| `1015 SSL_COMMON_NAME_DOES_NOT_MATCH` | certificate CN/DNS SAN differs from the portal name |
| `1016 SOCKET_CLOSED` right away | nothing listening on the portal port, or wrong port (use the RPC port) |
| stays on "Connecting" | `Forever.WebHost` differs from the portal name |
| login ok, then disconnected at realm join | world server not running / `Forever.WorldPort` closed |

Self-update is disabled in this fork.
