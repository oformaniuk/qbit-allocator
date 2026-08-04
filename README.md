# qBittorrent Allocator

Lightweight Docker-first daemon for assigning new qBittorrent downloads to Docker-visible download mount paths. It exposes a qBittorrent-compatible `/api/v2/...` proxy and a read-only status UI at `/`.

This project is intended for lawful download and storage-management workflows, such as Linux ISOs, public datasets, backups, and internal artifacts. Do not use the examples, tests, or issue tracker to share copyrighted content, private tracker details, credentials, or private URLs.

## Features

- qBittorrent-compatible proxy for `/api/v2/...`.
- Add-request allocation across configured disk roots.
- Configurable allocation policies, category pins, and disk pin tags.
- Optional background reconciliation for paused never-started torrents.
- Relocation by tag for already-added torrents.
- Read-only status UI and JSON status endpoint.
- Docker-first deployment with local `dotnet run` support for development.

## Behavior

- Proxies qBittorrent Web API calls to `Allocator:UpstreamUrl`.
- Intercepts only `POST /api/v2/torrents/add`.
- Rewrites intercepted add requests with an allocated `savepath` and `autoTMM=false`.
- Adds allocator tags to rewritten add requests by default: `qbit-assigned` and `disk:<label>`.
- Preserves the caller's paused/start preference unless `Allocator:ForcePausedOnAdd` is explicitly configured.
- Reconciles every 30 seconds by default, moving only paused incomplete torrents that appear never-started and do not already have `qbit-assigned`.
- Tags reconciled torrents with `qbit-assigned` and `disk:<label>`, then starts them.
- Leaves already-started paused torrents alone by default.
- Moves any torrent tagged with `qbit-relocate`, updates allocator tags, removes `qbit-relocate`, and leaves its current start/pause state unchanged.

## Configuration

Configure container paths, not host paths. qBittorrent and this daemon must see the same disk roots at the same paths.

If `Disks` is empty and `AutoDetect.Enabled` is true, the allocator scans each `AutoDetect.RootPaths` entry and treats immediate child directories as disks. With the default `/data`, mounts such as `/data/disk-a` and `/data/disk-b` are discovered automatically. Manual `Disks` entries take precedence when present.

```json
{
  "Allocator": {
    "UpstreamUrl": "http://qbittorrent:8080",
    "Policy": "most_free",
    "UnknownSizeBytes": 107374182400,
    "DefaultCategory": "default",
    "SavePathTemplate": "{disk}/{category}",
    "AssignmentTag": "qbit-assigned",
    "DiskTagPrefix": "disk:",
    "RelocateTag": "qbit-relocate",
    "DiskPinTagPrefix": "root:",
    "CategoryPins": {},
    "TagOnProxyAdd": true,
    "RemoveStaleDiskTagsOnProxyAdd": false,
    "OnAllocationFailure": "reject",
    "ReconcilerEnabled": true,
    "ReconcileIntervalSeconds": 30,
    "StartAfterReconcile": true,
    "StartAfterRelocate": false,
    "QbitClientTimeoutSeconds": 30,
    "ProxyTimeoutSeconds": 300,
    "AutoDetect": {
      "Enabled": true,
      "RootPaths": [ "/data" ],
      "IncludeRootPathsAsDisks": false,
      "MinFreeBytes": 214748364800
    },
    "Disks": []
  }
}
```

Manual disk entries can be used instead of auto-detection:

```json
{
  "Label": "disk-a",
  "Path": "/data/disk-a",
  "SavePathTemplate": "{disk}/torrents",
  "Enabled": true,
  "MinFreeBytes": 214748364800,
  "Categories": [ "linux-isos", "datasets" ],
  "Weight": 1
}
```

Policies:

- `most_free`: choose the eligible disk with the most free bytes after reserve.
- `fill`: keep using the current disk until reserve or fit fails.
- `balance_percent`: choose the eligible disk with the lowest used percentage.

Save paths are built as `<disk.path>/<category-or-default>`. Categories are sanitized to path-safe names.

`SavePathTemplate` controls the final qBittorrent save path:

- `{disk}/{category}` keeps the original category folder behavior.
- `{disk}/torrents` puts every assignment for that disk into one fixed folder.
- Disk entries can override the global value with their own `SavePathTemplate`.

Disk pin tags:

- `DiskPinTagPrefix` defaults to `root:`.
- If an add request contains exactly one tag using that prefix, such as `root:disk-a`, the allocator only considers the disk whose `Label` is `disk-a`.
- If the pinned disk is unknown, disabled, filtered out, or does not have enough space after reserve, allocation fails instead of silently choosing another disk.
- If multiple different pin tags are present, allocation fails.
- The allocator still adds its own assignment tags, such as `qbit-assigned` and `disk:disk-a`, after placement.

Allocator tags:

- `AssignmentTag` defaults to `qbit-assigned`.
- `DiskTagPrefix` defaults to `disk:`, producing tags such as `disk:disk-a`.
- `TagOnProxyAdd` controls whether intercepted add requests include allocator tags before they are forwarded to qBittorrent.
- `RemoveStaleDiskTagsOnProxyAdd` removes incoming tags that match `DiskTagPrefix` before adding the selected disk tag.

Allocation failure handling:

- `OnAllocationFailure` defaults to `reject`, returning `507 Insufficient Storage`.
- `passthrough` forwards the original form add request unchanged when no disk fits.
- `pause-and-passthrough` forwards the original form add request with `paused=true` when no disk fits.

Reconciler controls:

- `ReconcilerEnabled` can disable background reconciliation for proxy-only setups.
- `StartAfterReconcile` controls whether newly reconciled never-started torrents are started after assignment.
- `StartAfterRelocate` controls whether torrents moved via `RelocateTag` are started after relocation. It defaults to false, preserving the torrent's current start/pause state.
- `QbitClientTimeoutSeconds` controls qBittorrent API calls made by the status and reconciler paths.
- `ProxyTimeoutSeconds` controls proxied qBittorrent API calls.

Category pins:

- `CategoryPins` maps incoming qBittorrent categories to disk labels.
- This is useful when separate download clients or automation profiles use different categories.
- Example: `"CategoryPins": { "datasets-disk-a": "disk-a", "datasets-disk-b": "disk-b" }`.
- If both a category pin and a disk pin tag are present, they must point at the same disk.

## Docker

```bash
docker compose up --build
```

The example compose file exposes the allocator on `http://localhost:8088` and qBittorrent on `http://localhost:8080`.

To build only the allocator image:

```bash
docker build -t qbit-allocator:local .
```

When published on GitHub, the included Docker workflow builds pull requests and
publishes images to GitHub Container Registry on pushes to `main` and tags such
as `v0.1.0`.

Typical image names:

```text
ghcr.io/oformaniuk/qbit-allocator:latest
ghcr.io/oformaniuk/qbit-allocator:0.1.0
ghcr.io/oformaniuk/qbit-allocator:sha-<commit>
```

## Automation Wiring

Point your download automation tool at the allocator instead of qBittorrent:

- Host: allocator container name or host IP
- Port: allocator port, such as `8080` inside Docker or `8088` from the example host mapping
- URL base: leave as your existing qBittorrent API base if one is configured
- Username/password: keep the qBittorrent credentials; auth requests are proxied upstream

Disable qBittorrent Automatic Torrent Management for allocator-managed torrents. The allocator writes `autoTMM=false` on intercepted adds.

### Root-Folder-Aware Routing

qBittorrent add requests may not include the final import root folder used by external automation. To keep same-disk moves or hardlinks working across multiple disks, use one download client entry per disk with a disk-specific category.

Example layout:

```text
/data/disk-a/downloads/datasets
/data/disk-a/imports/datasets
/data/disk-b/downloads/datasets
/data/disk-b/imports/datasets
```

Allocator disks:

```json
{
  "Label": "disk-a",
  "Path": "/data/disk-a",
  "SavePathTemplate": "{disk}/downloads/datasets",
  "Enabled": true
}
```

```json
{
  "Label": "disk-b",
  "Path": "/data/disk-b",
  "SavePathTemplate": "{disk}/downloads/datasets",
  "Enabled": true
}
```

Allocator category pins:

```json
"CategoryPins": {
  "datasets-disk-a": "disk-a",
  "datasets-disk-b": "disk-b"
}
```

Automation setup:

- Add import roots `/data/disk-a/imports/datasets` and `/data/disk-b/imports/datasets`.
- Create two automation profiles or tags, for example `disk-a` and `disk-b`.
- Assign each item to the import root for its disk and tag it with the matching disk label.
- Create two qBittorrent download clients in your automation tool, both pointing to qbit-allocator:
  - disk-a client: automation tag filter `disk-a`, qBittorrent category `datasets-disk-a`
  - disk-b client: automation tag filter `disk-b`, qBittorrent category `datasets-disk-b`

When the automation tool grabs a legal dataset tagged `disk-a`, it uses the disk-a download client, sends category `datasets-disk-a`, and qbit-allocator downloads to `/data/disk-a/downloads/datasets`. Since the import root is also on disk-a, the automation tool can use same-disk moves or hardlinks on import.

Without that explicit category mapping, qbit-allocator can balance by free space, but it cannot know which import root folder will be used later.

## Development

Requirements:

- .NET 10 SDK
- Docker, if you are changing container behavior

```bash
dotnet test
dotnet run --project src/QbitAllocator
```

Open `http://localhost:5000/` or the URL printed by `dotnet run` for the read-only UI.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for development and pull request guidance.

Please keep examples and test data neutral and lawful. Prefer categories such as `linux-isos`, `datasets`, `backups`, or `internal-artifacts`.

## Support And Security

- For bugs and feature requests, use GitHub Issues after publishing the repository.
- For configuration help, include sanitized config and logs as described in [SUPPORT.md](SUPPORT.md).
- For vulnerabilities, follow [SECURITY.md](SECURITY.md).

## License

MIT. See [LICENSE](LICENSE).
