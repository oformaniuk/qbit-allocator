# Contributing

Thanks for helping improve qbit-allocator.

## Ground Rules

- Keep examples and tests focused on legal download workflows, such as Linux ISOs, public datasets, backups, or internal artifacts.
- Do not include credentials, private tracker URLs, personal hostnames, or real hashes in issues, tests, examples, screenshots, or logs.
- Keep changes small and focused. Open a discussion or issue first for broad behavior changes.
- Preserve qBittorrent API compatibility unless the change is explicitly about compatibility handling.

## Development

Requirements:

- .NET 10 SDK
- Docker, only if you are changing container behavior

Run tests:

```bash
dotnet test
```

Run locally:

```bash
dotnet run --project src/QbitAllocator
```

Build the container:

```bash
docker build -t qbit-allocator:local .
```

## Pull Requests

Before opening a pull request:

- Add or update focused tests for behavior changes.
- Update `README.md` and `examples/appsettings.example.json` when configuration changes.
- Run `dotnet test`.
- Confirm generated folders such as `bin/`, `obj/`, and `TestResults/` are not included.
