# POS_UPDATER_SYSTEM

POS_UPDATER_SYSTEM is an application deployment orchestrator for the Mini POS Angular application. It manages deployable build artifacts only. It does not edit, patch, or synchronize Mini POS source code.

## Architecture

The ASP.NET Core backend owns the deployment lifecycle and serves the active Mini POS build from `Storage/Current`.

Controllers are intentionally thin:

- `DeploymentController` exposes state, update check, and deployment trigger endpoints.
- `DeploymentOrchestrator` owns the application state transitions.
- Specialized services handle download, verification, staging, validation, activation, rollback, state persistence, and host-boundary logging.
- `UpdateWatcherService` is a timer-based hosted service that checks for updates every 30 minutes.

## Runtime Storage

```text
Storage/
  Current/    active live application state served by ASP.NET static files
  Downloads/  downloaded update packages
  Staging/    isolated candidate application state
  Backups/    last known good snapshots
  Logs/       timestamped deployment lifecycle logs
  Registry/   deployment-state.json
```

## Deployment Lifecycle

The orchestrator treats deployment as state transition, not file synchronization:

```text
CHECKING
DOWNLOADING
VERIFYING
STAGING
VALIDATING
BACKING_UP
ACTIVATING
RESTARTING
LIVE
```

If activation or post-activation validation fails, the orchestrator moves to `ROLLBACK`, restores the backup snapshot into `Current`, validates the restored app, and records `ROLLED_BACK`.

## Safety Rules

- `Current` is never replaced before a backup is created.
- ZIP files are SHA256 verified before staging.
- Staging is isolated from the live app.
- Activation is allowed only after staging validation succeeds.
- Only one deployment operation can run at a time.
- Background update failures are logged and never crash the backend.

## API

- `GET /api/deployment/state`
- `POST /api/deployment/check`
- `POST /api/deployment/deploy`

The live Mini POS app is served from `/` using the contents of `Storage/Current`.
