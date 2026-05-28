# IIS Deployment

## Pieces

- Updater API: this ASP.NET Core project. It checks `latest.json`, downloads packages, validates them, switches `Storage/Current`, and rolls back from `Storage/Backups`.
- Mini POS: the Angular build that is deployed by the updater into `Storage/Current`. The API serves this folder as the live Mini POS site.
- Update files: static version files hosted by IIS from `C:\inetpub\wwwroot\updates`.

## Update Feed Folder

Create this folder:

```powershell
New-Item -ItemType Directory -Force C:\inetpub\wwwroot\updates
```

Place files like this:

```text
C:\inetpub\wwwroot\updates\
  latest.json
  MiniPOS-v1.2.0.zip
```

Example `latest.json`:

```json
{
  "version": "1.2.0",
  "package": "MiniPOS-v1.2.0.zip",
  "sha256": "PUT_ZIP_SHA256_HERE"
}
```

The updater reads `http://localhost/updates/latest.json`. If `package` is relative, the ZIP is downloaded from the same IIS folder.

## Publish Updater API

Install the .NET Hosting Bundle on the IIS server, then publish:

```powershell
dotnet publish POS_UPDATER_SYSTEM.Api.csproj -c Release -o C:\inetpub\wwwroot\pos-updater
```

Create an IIS site or application pointing to:

```text
C:\inetpub\wwwroot\pos-updater
```

Set the app pool to:

```text
No Managed Code
```

Give the app pool identity modify permission on the updater storage folder:

```powershell
icacls C:\inetpub\wwwroot\pos-updater\Storage /grant "IIS AppPool\YOUR_APP_POOL_NAME:(OI)(CI)M"
```

Use this `appsettings.json` in the published folder:

```json
{
  "Updater": {
    "StorageRoot": "Storage",
    "LatestJsonUrl": "http://localhost/updates/latest.json",
    "LiveAppBaseUrl": "http://localhost:5010",
    "CheckIntervalMinutes": 30,
    "MainScriptPattern": "main*.js"
  }
}
```

If the updater API is the same IIS site that serves Mini POS, set `LiveAppBaseUrl` to that site URL.

## Publish Updater Frontend

From `POS_UPDATER_SYSTEM.UpdaterFrontend`:

```powershell
npm install
npm run build
```

Copy the build output to an IIS folder, for example:

```text
C:\inetpub\wwwroot\pos-updater-ui
```

Create an IIS site/application for that folder. In the UI, set the API textbox to the updater API base URL, for example:

```text
http://localhost:5000
```

## Runtime Flow

1. UI calls `POST /api/deployment/check`.
2. Updater API downloads `C:\inetpub\wwwroot\updates\latest.json` through `http://localhost/updates/latest.json`.
3. UI calls `POST /api/deployment/deploy`.
4. Updater API downloads the ZIP, verifies SHA256, extracts to `Storage/Staging`, validates it, backs up `Storage/Current`, then copies the staged Mini POS to `Storage/Current`.
5. UI calls `POST /api/deployment/rollback` when an explicit rollback is needed. The API restores the latest backup from `Storage/Backups`.
6. UI reads per-operation logs from `GET /api/deployment/logs/{fileName}`.
