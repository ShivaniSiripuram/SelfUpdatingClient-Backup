import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Observable } from 'rxjs';

type DeploymentStatus =
  | 'LIVE'
  | 'CHECKING'
  | 'DOWNLOADING'
  | 'VERIFYING'
  | 'STAGING'
  | 'VALIDATING_STATIC'
  | 'VALIDATING_RUNTIME'
  | 'BACKING_UP'
  | 'ACTIVATING'
  | 'RESTARTING'
  | 'ROLLBACK'
  | 'ROLLED_BACK'
  | 'FAILED';

interface DeploymentState {
  currentVersion: string;
  status: DeploymentStatus;
  lastCheckTime?: string;
  lastUpdateTime?: string;
  lastRollbackTime?: string;
  isUpdating: boolean;
  lastKnownGoodVersion: string;
  lastBackupPath?: string;
  isRollbackAvailable: boolean;
  lastError?: string;
  lastOperationLogFile?: string;
  rejectedVersion?: string;
  rejectedReason?: string;
  rejectedVersionAt?: string;
  corruptedVersion?: string;
  corruptedReason?: string;
  corruptedVersionAt?: string;
}

interface LatestUpdateInfo {
  version: string;
  package: string;
  sha256: string;
}

interface UpdateCheckResult {
  currentVersion: string;
  latestVersion: string;
  isUpdateAvailable: boolean;
  latest?: LatestUpdateInfo;
  logFile?: string;
  rejectedVersion?: string;
  rejectedReason?: string;
  isLatestRejected: boolean;
  corruptedVersion?: string;
  corruptedReason?: string;
  isLatestCorrupted: boolean;
  remoteLatestVersion?: string;
  isLatestSuppressed: boolean;
}

interface DeploymentResult {
  succeeded: boolean;
  rolledBack: boolean;
  message: string;
  version?: string;
  logFile?: string;
}

interface DeploymentLogSummary {
  fileName: string;
  lastModifiedUtc: string;
  sizeBytes: number;
}

interface DeploymentLogContent {
  fileName: string;
  content: string;
}

type ApiAction = 'check' | 'deploy' | 'rollback';

const apiBaseKey = 'pos-updater-api-base';
const defaultApiBase = 'http://localhost:5010';
const visualDeploymentDurationMs = 30_000;

@Component({
  selector: 'app-root',
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App {
  private readonly http = inject(HttpClient);
  private autoCheckInFlight = false;
  private autoDeployVersion: string | null = null;
  private visualDeploymentStartedAt: number | null = null;
  private visualDeploymentCurrentVersion: string | null = null;
  private visualDeploymentTargetVersion: string | null = null;

  readonly apiBase = signal(localStorage.getItem(apiBaseKey) ?? defaultApiBase);
  readonly state = signal<DeploymentState | null>(null);
  readonly latest = signal<UpdateCheckResult | null>(null);
  readonly lastResult = signal<DeploymentResult | null>(null);
  readonly logs = signal<DeploymentLogSummary[]>([]);
  readonly selectedLogFile = signal<string | null>(null);
  readonly selectedLogContent = signal('');
  readonly busyAction = signal<ApiAction | null>(null);
  readonly autoUpdateStatus = signal('Automatic update checks run every 5 seconds.');
  readonly visualTick = signal(0);
  readonly error = signal<string | null>(null);

  readonly stages: DeploymentStatus[] = [
    'CHECKING',
    'DOWNLOADING',
    'VERIFYING',
    'STAGING',
    'VALIDATING_STATIC',
    'VALIDATING_RUNTIME',
    'BACKING_UP',
    'ACTIVATING',
    'RESTARTING',
    'LIVE'
  ];

  readonly progressPercent = computed(() => {
    this.visualTick();
    const current = this.state()?.status ?? 'LIVE';
    if (this.isVisualDeploymentActive()) {
      const elapsed = Date.now() - this.visualDeploymentStartedAt!;
      return Math.max(5, Math.min(99, Math.round((elapsed / visualDeploymentDurationMs) * 100)));
    }

    const index = this.stages.indexOf(current);
    if (current === 'FAILED') {
      return 100;
    }

    if (current === 'ROLLBACK' || current === 'ROLLED_BACK') {
      return 100;
    }

    return index < 0 ? 0 : Math.round(((index + 1) / this.stages.length) * 100);
  });

  readonly latestVersion = computed(() => {
    this.visualTick();
    const state = this.state();
    const latest = this.latest();
    const rejectedVersion = this.rejectedVersion();
    if (state && rejectedVersion && latest?.remoteLatestVersion === rejectedVersion) {
      return state.currentVersion ?? 'Unknown';
    }

    return this.isVisualDeploymentActive()
      ? this.visualDeploymentTargetVersion ?? 'Unknown'
      : latest?.latestVersion ?? state?.currentVersion ?? 'Unknown';
  });
  readonly currentVersion = computed(() => {
    this.visualTick();
    return this.isVisualDeploymentActive()
      ? this.visualDeploymentCurrentVersion ?? 'Unknown'
      : this.state()?.currentVersion ?? 'Unknown';
  });
  readonly currentStatus = computed(() => this.state()?.status ?? 'LIVE');
  readonly progressClass = computed(() => {
    this.visualTick();
    if (this.isVisualDeploymentActive()) {
      return this.visualStageClass();
    }

    const status = this.currentStatus();
    if (status === 'FAILED') {
      return 'failed';
    }

    if (status === 'LIVE' || status === 'ROLLED_BACK') {
      return 'live';
    }

    if (status === 'DOWNLOADING' || status === 'VERIFYING') {
      return 'download';
    }

    if (status === 'STAGING' || status === 'VALIDATING_STATIC' || status === 'VALIDATING_RUNTIME') {
      return 'staging';
    }

    if (status === 'BACKING_UP') {
      return 'backup';
    }

    if (status === 'ACTIVATING' || status === 'RESTARTING') {
      return 'activation';
    }

    return 'checking';
  });
  readonly currentStageText = computed(() => {
    this.visualTick();
    if (this.isVisualDeploymentActive()) {
      return this.visualStageText();
    }

    const label: Record<DeploymentStatus, string> = {
      LIVE: 'Live',
      CHECKING: 'Checking for updates...',
      DOWNLOADING: 'Downloading package...',
      VERIFYING: 'Verifying package...',
      STAGING: 'Staging update...',
      VALIDATING_STATIC: 'Validating staged files...',
      VALIDATING_RUNTIME: 'Validating staged runtime...',
      BACKING_UP: 'Backing up current version...',
      ACTIVATING: 'Activating new version...',
      RESTARTING: 'Restarting live application...',
      ROLLBACK: 'Rolling back...',
      ROLLED_BACK: 'Rolled back',
      FAILED: 'Update failed'
    };

    return label[this.currentStatus()];
  });
  readonly isUpdateAvailable = computed(() => {
    const latest = this.latest();
    if (!latest) {
      return false;
    }

    return latest.isUpdateAvailable && latest.currentVersion !== latest.latestVersion;
  });
  readonly canDeploy = computed(() => this.isUpdateAvailable() && !this.busyAction() && !this.state()?.isUpdating);
  readonly canRollback = computed(() => !!this.state()?.isRollbackAvailable && !this.busyAction() && !this.state()?.isUpdating);
  readonly rejectedVersion = computed(() => this.state()?.rejectedVersion ?? this.state()?.corruptedVersion ?? null);
  readonly rejectedReason = computed(() => this.state()?.rejectedReason ?? this.state()?.corruptedReason ?? null);

  constructor() {
    this.refresh();
    this.autoCheckForUpdate();
    window.setInterval(() => this.autoCheckForUpdate(), 5000);
    window.setInterval(() => {
      if (this.busyAction() || this.state()?.isUpdating) {
        this.refreshState();
      }
    }, 2000);
    window.setInterval(() => {
      this.visualTick.update((value) => value + 1);
      this.clearCompletedVisualDeployment();
    }, 1000);
  }

  setApiBase(value: string): void {
    const normalized = value.trim().replace(/\/$/, '');
    this.apiBase.set(normalized || defaultApiBase);
    localStorage.setItem(apiBaseKey, this.apiBase());
    this.latest.set(null);
    this.autoDeployVersion = null;
  }

  refresh(): void {
    this.refreshState();
    this.refreshLogs();
  }

  refreshState(): void {
    this.http.get<DeploymentState>(this.url('/api/deployment/state')).subscribe({
      next: (state) => {
        this.state.set(state);
        const latest = this.latest();
        if (latest && state.currentVersion === latest.latestVersion) {
          this.latest.set({ ...latest, currentVersion: state.currentVersion, isUpdateAvailable: false });
        }
        const rejectedVersion = state.rejectedVersion ?? state.corruptedVersion;
        const rejectedReason = state.rejectedReason ?? state.corruptedReason;
        if (latest && rejectedVersion && rejectedVersion === latest.latestVersion) {
          this.latest.set({
            ...latest,
            currentVersion: state.currentVersion,
            latestVersion: state.currentVersion,
            isUpdateAvailable: false,
            latest: undefined,
            rejectedVersion,
            rejectedReason,
            isLatestRejected: true,
            corruptedVersion: rejectedVersion,
            corruptedReason: rejectedReason,
            isLatestCorrupted: true
          });
        }
        this.error.set(null);
      },
      error: (error) => this.captureError('State refresh failed', error)
    });
  }

  check(): void {
    this.runAction('check', this.http.post<UpdateCheckResult>(this.url('/api/deployment/check'), {}));
  }

  deploy(): void {
    this.runAction('deploy', this.http.post<DeploymentResult>(this.url('/api/deployment/deploy'), {}));
  }

  rollback(): void {
    this.runAction('rollback', this.http.post<DeploymentResult>(this.url('/api/deployment/rollback'), {}));
  }

  

  selectLog(fileName: string): void {
    this.selectedLogFile.set(fileName);
    this.http.get<DeploymentLogContent>(this.url(`/api/deployment/logs/${encodeURIComponent(fileName)}`)).subscribe({
      next: (log) => {
        this.selectedLogContent.set(log.content);
        this.error.set(null);
      },
      error: (error) => this.captureError('Log load failed', error)
    });
  }

  refreshLogs(): void {
    this.http.get<DeploymentLogSummary[]>(this.url('/api/deployment/logs')).subscribe({
      next: (logs) => {
        this.logs.set(logs);
        if (!this.selectedLogFile() && logs.length > 0) {
          this.selectLog(logs[0].fileName);
        }
      },
      error: (error) => this.captureError('Log list refresh failed', error)
    });
  }

  stageClass(stage: DeploymentStatus): string {
    const status = this.currentStatus();
    if (status === 'FAILED') {
      return 'failed';
    }

    if (status === stage) {
      return 'active';
    }

    const currentIndex = this.stages.indexOf(status);
    const stageIndex = this.stages.indexOf(stage);
    return currentIndex > stageIndex ? 'done' : '';
  }

  private runAction<T extends UpdateCheckResult | DeploymentResult>(action: ApiAction, request: Observable<T>): void {
    this.busyAction.set(action);
    this.error.set(null);
    if (action === 'deploy') {
      this.startVisualDeployment(
        this.latest()?.currentVersion ?? this.state()?.currentVersion ?? null,
        this.latest()?.latestVersion ?? null
      );
    }

    request.subscribe({
      next: (result: T) => {
        if ('isUpdateAvailable' in result) {
          this.latest.set(result);
          if (result.logFile) {
            this.selectLog(result.logFile);
          }
        } else {
          this.lastResult.set(result);
          if (result.rolledBack) {
            this.latest.set(null);
          }
          if (result.logFile) {
            this.selectLog(result.logFile);
          }
        }

        this.refreshState();
        this.refreshLogs();
      },
      error: (error: unknown) => this.captureError(`${action} failed`, error),
      complete: () => this.busyAction.set(null)
    });
  }

  private autoCheckForUpdate(): void {
    if (this.autoCheckInFlight || this.busyAction() || this.state()?.isUpdating) {
      this.refreshState();
      return;
    }

    this.autoCheckInFlight = true;
    this.autoUpdateStatus.set('Checking for updates...');

    this.http.post<UpdateCheckResult>(this.url('/api/deployment/check'), {}).subscribe({
      next: (result) => {
        this.latest.set(result);
        this.error.set(null);

        if (!result.isUpdateAvailable || result.currentVersion === result.latestVersion || !result.latest) {
          this.autoDeployVersion = null;
          const rejectedVersion = result.rejectedVersion ?? result.corruptedVersion;
          this.autoUpdateStatus.set((result.isLatestRejected || result.isLatestCorrupted) && rejectedVersion
            ? `Version ${rejectedVersion} is rejected. Waiting for a newer update.`
            : 'No update available. Next check in 5 seconds.');
          return;
        }

        if (this.autoDeployVersion === result.latestVersion) {
          this.autoUpdateStatus.set(`Update ${result.latestVersion} is already being handled.`);
          return;
        }

        this.autoDeployVersion = result.latestVersion;
        this.autoUpdateStatus.set(`Update ${result.latestVersion} found. Starting automatic deployment...`);
        this.startVisualDeployment(result.currentVersion, result.latestVersion);
        this.runAction('deploy', this.http.post<DeploymentResult>(this.url('/api/deployment/deploy'), {}));
      },
      error: (error: unknown) => {
        this.autoCheckInFlight = false;
        this.captureError('Automatic update check failed', error);
      },
      complete: () => {
        this.autoCheckInFlight = false;
        this.refreshState();
      }
    });
  }

  private url(path: string): string {
    return `${this.apiBase()}${path}`;
  }

  private captureError(context: string, error: unknown): void {
    const message = typeof error === 'object' && error && 'error' in error
      ? JSON.stringify((error as { error: unknown }).error)
      : String(error);
    this.error.set(`${context}: ${message}`);
    this.busyAction.set(null);
  }

  private startVisualDeployment(currentVersion: string | null, targetVersion: string | null): void {
    this.visualDeploymentStartedAt = Date.now();
    this.visualDeploymentCurrentVersion = currentVersion;
    this.visualDeploymentTargetVersion = targetVersion;
  }

  private isVisualDeploymentActive(): boolean {
    if (!this.visualDeploymentStartedAt || this.currentStatus() === 'FAILED') {
      return false;
    }

    return Date.now() - this.visualDeploymentStartedAt < visualDeploymentDurationMs;
  }

  private clearCompletedVisualDeployment(): void {
    if (!this.visualDeploymentStartedAt) {
      return;
    }

    if (Date.now() - this.visualDeploymentStartedAt >= visualDeploymentDurationMs || this.currentStatus() === 'FAILED') {
      this.visualDeploymentStartedAt = null;
      this.visualDeploymentCurrentVersion = null;
      this.visualDeploymentTargetVersion = null;
      this.refreshState();
    }
  }

  private visualStageClass(): string {
    const progress = this.progressPercent();
    if (progress < 30) {
      return 'download';
    }

    if (progress < 55) {
      return 'staging';
    }

    if (progress < 75) {
      return 'backup';
    }

    return 'activation';
  }

  private visualStageText(): string {
    const progress = this.progressPercent();
    if (progress < 20) {
      return 'Downloading package...';
    }

    if (progress < 35) {
      return 'Verifying package...';
    }

    if (progress < 55) {
      return 'Staging update...';
    }

    if (progress < 75) {
      return 'Backing up current version...';
    }

    if (progress < 92) {
      return 'Activating new version...';
    }

    return 'Restarting live application...';
  }
}
