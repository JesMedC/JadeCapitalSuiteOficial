import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthState } from '@core/state/auth.state';

@Component({
  selector: 'jcs-trader-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <aside class="sidebar">
        <span class="brand">Jade Capital</span>
        <nav class="jcs-stack">
          <a routerLink="dashboard" routerLinkActive="active">Dashboard</a>
          <a routerLink="trades" routerLinkActive="active">Trades</a>
          <a routerLink="calendar" routerLinkActive="active">Calendario</a>
        </nav>
        <div class="user">
          <span class="jcs-muted">{{ auth.user()?.email }}</span>
          <button class="jcs-btn jcs-btn--ghost" (click)="logout()">Salir</button>
        </div>
      </aside>
      <main class="main">
        <router-outlet></router-outlet>
      </main>
    </div>
  `,
  styles: [`
    .shell { display: grid; grid-template-columns: 240px 1fr; min-height: 100vh; }
    .sidebar { background: var(--bg-sidebar); border-right: 1px solid var(--border); padding: var(--space-6); display: flex; flex-direction: column; gap: var(--space-6); }
    .brand { font-weight: 700; color: var(--green); }
    nav a { display: block; padding: var(--space-2) var(--space-3); border-radius: var(--radius-sm); color: var(--text-muted); }
    nav a.active { background: var(--bg-elevated); color: var(--text-main); }
    .user { margin-top: auto; display: flex; flex-direction: column; gap: var(--space-2); }
    .plan { font-size: var(--font-size-xs); }
    .main { padding: var(--space-8); }
    @media (max-width: 768px) { .shell { grid-template-columns: 1fr; } .sidebar { display: none; } }
  `],
})
export class TraderShell {
  readonly auth = inject(AuthState);
  private readonly router = inject(Router);

  logout(): void {
    this.auth.logout();
    this.router.navigateByUrl('/auth/login');
  }
}
