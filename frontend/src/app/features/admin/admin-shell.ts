import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MobileNav, MobileNavItem } from '@shared/mobile-nav';

@Component({
  selector: 'jcs-admin-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MobileNav],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="admin-shell">
      <header class="admin-header">
        <h1 class="admin-title">Administración</h1>
        <!-- Tablet+ nav links (mobile uses bottom-nav instead). -->
        <nav class="admin-nav">
          <a routerLink="subscriptions" routerLinkActive="is-active">Suscripciones</a>
        </nav>
      </header>
      <main class="admin-main">
        <router-outlet />
      </main>
      <!-- Mobile bottom-nav (< 768px only). -->
      <jcs-mobile-nav [items]="navItems" />
    </div>
  `,
  styles: [`
    .admin-shell {
      max-width: 1100px;
      margin: 0 auto;
      padding: 32px 24px;
    }
    /* Mobile (< 768px): tighter padding + room at bottom for bottom-nav. */
    @media (max-width: 767px) {
      .admin-shell {
        padding: var(--sp-4);
        padding-bottom: calc(76px + env(safe-area-inset-bottom, 0px));
      }
    }

    .admin-header {
      display: flex;
      align-items: baseline;
      gap: 24px;
      margin-bottom: 24px;
      border-bottom: 1px solid var(--border);
      padding-bottom: 16px;
    }
    /* Mobile (< 768px): stack the header (title on top, nav hidden). */
    @media (max-width: 767px) {
      .admin-header {
        flex-direction: column;
        align-items: flex-start;
        gap: var(--sp-2);
        padding-bottom: var(--sp-3);
      }
    }

    .admin-title {
      margin: 0;
      font-size: 1.5rem;
    }

    .admin-nav {
      display: flex;
      gap: 16px;
    }
    .admin-nav a {
      color: var(--text-secondary);
      text-decoration: none;
      padding: 6px 12px;
      border-radius: var(--radius-sm);
    }
    .admin-nav a:hover { color: var(--green); }
    .admin-nav a.is-active { background: var(--green-soft); color: var(--green); }

    .admin-main { min-height: 60vh; }
  `],
})
export class AdminShell {
  readonly navItems: MobileNavItem[] = [
    { label: 'Suscripciones', path: 'subscriptions', icon: 'list' },
  ];
}
