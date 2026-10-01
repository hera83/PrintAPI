# Print API

A small REST API that sits between your own programs and your HP network printers. A program
sends a PDF to the API, and the API queues the job, converts the document to a format the printer
understands and sends it over IPP, then tracks it until the printer reports it done.

Your programs only need an HTTP client and an API key. They don't need printer drivers, a print
spooler or any knowledge of the printer.

## Features

- **Printer discovery and registration**: finds HP printers on the local network (mDNS/Bonjour,
  optionally a subnet scan) and registers them by IP address or hostname.
- **Print queue**: accepts PDFs (base64) with copies, color/monochrome, duplex and page ranges.
  Jobs are processed in the background, one job per printer at a time, in order.
- **Automatic conversion**: if a printer doesn't accept PDF directly, every page is rendered at
  300 dpi and sent as PWG raster.
- **Safe retries**: an unreachable or busy printer is retried, but a job is never sent twice.
- **Job status**: follow each job from `Queued` through `Processing` and `Printing` to `Completed`,
  `Failed` or `Canceled`, including what the printer is waiting for (e.g. out of paper).
- **Test page**: prints a page with printer details, color patches and ink levels.
- **API keys**: one master key for administration, plus standard keys for client programs that may
  only print.
- **Health check**: `GET /Health` reports whether the printers are online.
- **Swagger UI**: built-in documentation where you can try every endpoint.

## Installation with Docker

### Requirements

- Docker with Docker Compose (Docker Desktop on Windows/macOS, or Docker Engine on Linux).
- A network connection from the Docker host to the printers (IPP, port 631).

### 1. Get the code

```bash
git clone <repository-url> PrintAPI
cd PrintAPI
```

### 2. Set the master key

Copy the example file and set `MASTER_KEY` to a long random value:

```bash
cp .env.example .env
```

Generate a key, for example with:

```bash
openssl rand -hex 32
```

or in PowerShell:

```powershell
-join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Maximum 256) })
```

`.env` then looks like this:

```env
MASTER_KEY=3f9c...your-random-value...
```

The master key can do everything, so keep it secret and don't use it in your client programs.
Compose refuses to start if `MASTER_KEY` is empty.

### 3. Start the API

```bash
docker compose up -d --build
```

Open <http://localhost:8080>. This redirects to Swagger UI. The API always runs on port 8080.
Click **Authorize** and enter your master key to try the endpoints.

### Data and volumes

The container keeps its data in two named Docker volumes, so it survives restarts and updates:

| Volume           | Path in the container | Contents                                           |
| ---------------- | --------------------- | -------------------------------------------------- |
| `printapi-dbs`   | `/app/app_dbs`        | `app.db` (printers, keys, print jobs) and `log.db` |
| `printapi-files` | `/app/app_files`      | PDFs waiting to be printed                         |

The database is created and migrated automatically when the container starts.

### Network: finding printers automatically

By default the container runs on Docker's bridge network. **Printing to a registered printer works
fine there**, but `GET /Hp/Discover` can't see the local network's mDNS traffic, so it won't find
anything. You can do either of the following:

- **Register printers by IP address** (recommended for Docker Desktop on Windows/macOS): use
  `POST /Hp/Register` with the printer's IP. Give the printer a fixed IP or a DHCP reservation in
  your router.
- **Use the host's network** (Linux hosts): in `docker-compose.yml`, remove the `ports:` section and
  uncomment `network_mode: host`. Discovery then works, and the API listens directly on port 8080
  of the host.

### Update

```bash
git pull
docker compose up -d --build
```

### Useful commands

```bash
docker compose logs -f printapi   # follow the log
docker compose restart printapi   # restart
docker compose down               # stop (the volumes and data are kept)
```

## Getting started

Every request needs an `x-api-key` header. The examples below use `curl`; everything can also be
done from Swagger UI.

**1. Find printers** (requires host networking, see above):

```bash
curl -H "x-api-key: $MASTER_KEY" "http://localhost:8080/Hp/Discover?timeoutSeconds=5"
```

**2. Register a printer** by IP or hostname:

```bash
curl -X POST http://localhost:8080/Hp/Register \
  -H "x-api-key: $MASTER_KEY" -H "Content-Type: application/json" \
  -d '{ "host": "192.168.1.50", "name": "Office printer" }'
```

The response includes the printer's `id`.

**3. Print a test page**:

```bash
curl -X POST -H "x-api-key: $MASTER_KEY" http://localhost:8080/Hp/PrintTestPage/1
```

**4. Create an API key for a client program**:

```bash
curl -X POST http://localhost:8080/Keys/Create \
  -H "x-api-key: $MASTER_KEY" -H "Content-Type: application/json" \
  -d '{ "name": "Invoice system" }'
```

The `key` in the response is shown **only once**, so save it. Standard keys can only use the
`/Print` endpoints, and they only see their own jobs.

**5. Print a PDF** from the client program:

```bash
curl -X POST http://localhost:8080/Print/Submit \
  -H "x-api-key: ak_..." -H "Content-Type: application/json" \
  -d '{
        "printerId": 1,
        "documentBase64": "<the PDF, base64-encoded>",
        "copies": 1,
        "color": false,
        "duplex": true,
        "pages": "1-3"
      }'
```

Only `printerId` and `documentBase64` are required. The API answers `202 Accepted` with the job's
`id`.

**6. Follow the job**:

```bash
curl -H "x-api-key: ak_..." http://localhost:8080/Print/GetStatus/<job-id>
```

## Endpoint overview

| Endpoint                              | Access     | Description                                               |
| ------------------------------------- | ---------- | --------------------------------------------------------- |
| `GET /Health`                         | Anonymous  | Are the printers online? 200 if at least one is, else 503 |
| `POST /Print/Submit`                  | Any key    | Queue a PDF for printing                                  |
| `GET /Print/GetStatus/{id}`           | Any key    | Status of a print job                                     |
| `GET /Print/GetAll`                   | Any key    | List print jobs (standard keys only see their own)        |
| `POST /Print/Cancel/{id}`             | Any key    | Cancel a print job                                        |
| `GET /Hp/Discover`, `GET /Hp/Inspect` | Master key | Find or inspect printers on the network                   |
| `/Hp/...`                             | Master key | Register, update, refresh, delete printers; test page     |
| `/Keys/...`                           | Master key | Create, update, roll over and delete API keys             |
| `GET /Log`                            | Master key | Search the application log                                |

The full documentation, with every field, is in Swagger UI at `/swagger`, and the OpenAPI document
is at `/openapi/v1.json`.

## Security

- The container serves **plain HTTP** on port 8080. Keep it on your local network, or put it behind a
  reverse proxy with HTTPS (e.g. Caddy, Traefik or nginx) if it has to be reachable from outside.
- API keys are stored only as SHA-256 hashes. A lost key can't be recovered; create a new one or
  roll it over with `/Keys/Rollover`.
- Never commit `.env` (it's in `.gitignore`).

## Development without Docker

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet restore
dotnet run --project api
```

The API then runs on <http://localhost:5193> in the `Development` environment. Set the master key in
`api/appsettings.Development.json` (`ApiKeys:MasterKey`) or, preferably, with user secrets:

```bash
dotnet user-secrets init --project api
dotnet user-secrets set "ApiKeys:MasterKey" "<your-key>" --project api
```

The project includes native libraries for both Windows and Linux (PDFium, SkiaSharp and SQLite), so
it runs the same way on a developer PC and in the container.
