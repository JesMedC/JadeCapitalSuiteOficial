import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'jcs-admin-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="admin-shell">
      <header class="admin-header">
        <h1 class="admin-title">Administración</h1>
        <nav class="admin-nav">
          <a routerLink="subscriptions" routerLinkActive="is-active">Suscripciones</a>
        </nav>
      </header>
      <main class="admin-main">
        <router-outlet />
      </main>
    </div>
  `,
  styles: [`
    .admin-shell { max-width: 1100px; margin: 0 auto; padding: 32px 24px; }
    .admin-header { display: flex; align-items: baseline; gap: 24px; margin-bottom: 24px; border-bottom: 1px solid var(--border); padding-bottom: 16px; }
    .admin-title { margin: 0; font-size: 1.5rem; }
    .admin-nav { display: flex; gap: 16px; }
    .admin-nav a { color: var(--text-secondary); text-decoration: none; padding: 6px 12px; border-radius: var(--radius-sm); }
    .admin-nav a:hover { color: var(--green); }
    .admin-nav a.is-active { background: var(--green-soft); color: var(--green); }
    .admin-main { min-height: 60vh; }
  `],
})
export class AdminShell {}
