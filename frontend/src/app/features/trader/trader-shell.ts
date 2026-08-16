import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthState } from '@core/state/auth.state';
import { MobileNav, MobileNavItem } from '@shared/mobile-nav';

interface NavItem {
  label: string;
  path: string;
  icon: MobileNavItem['icon'] | 'journal';
}

@Component({
  selector: 'jcs-trader-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MobileNav],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="shell">
      <!-- ============== Sidebar ============== -->
      <aside class="sidebar">
        <!-- Brand -->
        <a class="brand" routerLink="/">
          <span class="brand-mark" aria-hidden="true">
            <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M3 3v18h18"/>
              <path d="M7 14l4-4 3 3 5-6"/>
            </svg>
          </span>
          <span class="brand-text">
            <span class="brand-name">JadeCapital<strong>Suite</strong></span>
            <span class="brand-meta">Trading Journal</span>
          </span>
          <span class="brand-tag jcs-badge jcs-badge--neutral">PRO</span>
        </a>

        <!-- Nav -->
        <nav class="nav">
          <span class="nav-section">Trading</span>
          @for (item of navItems; track item.path) {
            <a
              [routerLink]="item.path"
              routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: false }"
              class="nav-link">
              <span class="nav-icon" aria-hidden="true">
                @switch (item.icon) {
                  @case ('dashboard') {
                    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <rect x="3" y="3" width="7" height="9"/>
                      <rect x="14" y="3" width="7" height="5"/>
                      <rect x="14" y="12" width="7" height="9"/>
                      <rect x="3" y="16" width="7" height="5"/>
                    </svg>
                  }
                  @case ('trades') {
                    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <line x1="8" y1="6" x2="21" y2="6"/>
                      <line x1="8" y1="12" x2="21" y2="12"/>
                      <line x1="8" y1="18" x2="21" y2="18"/>
                      <line x1="3" y1="6" x2="3.01" y2="6"/>
                      <line x1="3" y1="12" x2="3.01" y2="12"/>
                      <line x1="3" y1="18" x2="3.01" y2="18"/>
                    </svg>
                  }
                  @case ('journal') {
                    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <path d="M4 4h12a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H4z"/>
                      <line x1="8" y1="2" x2="8" y2="22"/>
                      <line x1="12" y1="8" x2="18" y2="8"/>
                      <line x1="12" y1="12" x2="18" y2="12"/>
                      <line x1="12" y1="16" x2="15" y2="16"/>
                    </svg>
                  }
                  @case ('list') {
                    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <path d="M3 4h6a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H3z"/>
                      <line x1="11" y1="6" x2="21" y2="6"/>
                      <line x1="11" y1="12" x2="21" y2="12"/>
                      <line x1="11" y1="18" x2="21" y2="18"/>
                    </svg>
                  }
                  @case ('calendar') {
                    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <rect x="3" y="4" width="18" height="18" rx="2"/>
                      <line x1="16" y1="2" x2="16" y2="6"/>
                      <line x1="8" y1="2" x2="8" y2="6"/>
                      <line x1="3" y1="10" x2="21" y2="10"/>
                    </svg>
                  }
                  @case ('settings') {
                    <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                      <circle cx="12" cy="12" r="3"/>
                      <path d="M19.4 15a1.7 1.7 0 0 0 .34 1.88l.06.06-2.83 2.83-.06-.06A1.7 1.7 0 0 0 15 19.4a1.7 1.7 0 0 0-1 .6 1.7 1.7 0 0 0-.4 1.1V21H9.6v-.09A1.7 1.7 0 0 0 8.5 19.4a1.7 1.7 0 0 0-1.88.34l-.06.06-2.83-2.83.06-.06A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-.6-1 1.7 1.7 0 0 0-1.1-.4H3V9.6h.09A1.7 1.7 0 0 0 4.6 8.5a1.7 1.7 0 0 0-.34-1.88l-.06-.06 2.83-2.83.06.06A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-.6 1.7 1.7 0 0 0 .4-1.1V3h4v.09A1.7 1.7 0 0 0 15.5 4.6a1.7 1.7 0 0 0 1.88-.34l.06-.06 2.83 2.83-.06.06A1.7 1.7 0 0 0 19.4 9c.14.38.35.72.64 1 .3.29.69.43 1.1.4H21v4h-.09A1.7 1.7 0 0 0 19.4 15z"/>
                    </svg>
                  }
                }
              </span>
              <span class="nav-label">{{ item.label }}</span>
              <span class="nav-pulse" aria-hidden="true"></span>
            </a>
          }
        </nav>

        <!-- User card at bottom -->
        <div class="user">
          <div class="user-card">
            <div class="user-avatar" aria-hidden="true">
              {{ userInitial() }}
            </div>
            <div class="user-info">
              <span class="user-name">{{ userShort() }}</span>
              <span class="user-email jcs-muted">{{ auth.user()?.email }}</span>
            </div>
            <button class="logout" (click)="logout()" aria-label="Cerrar sesión">
              <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/>
                <polyline points="16 17 21 12 16 7"/>
                <line x1="21" y1="12" x2="9" y2="12"/>
              </svg>
            </button>
          </div>
        </div>
      </aside>

      <!-- ============== Main content ============== -->
      <main class="main">
        <router-outlet></router-outlet>
      </main>

      <!-- ============== Mobile bottom-nav (< 768px only) ============== -->
      <jcs-mobile-nav [items]="navItems" />
    </div>
  `,
  styles: [`
    :host { display: block; min-height: 100vh; }

    .shell {
      display: grid;
      grid-template-columns: 260px 1fr;
      min-height: 100vh;
      background: var(--bg-main);
    }
    /* Mobile (< 768px): sidebar hidden, single-column shell. */
    @media (max-width: 767px) {
      .shell { grid-template-columns: 1fr; }
    }

    /* ===== Sidebar ===== */
    .sidebar {
      position: sticky;
      top: 0;
      align-self: start;
      height: 100vh;
      background: var(--bg-sidebar);
      border-right: 1px solid var(--border);
      display: flex;
      flex-direction: column;
      padding: var(--sp-5) var(--sp-4);
      gap: var(--sp-6);
      overflow-y: auto;
    }
    /* Mobile (< 768px): hide the sidebar; bottom-nav replaces it. */
    @media (max-width: 767px) {
      .sidebar { display: none; }
    }

    /* Brand */
    .brand {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-2);
      border-radius: var(--radius-sm);
      color: var(--text-main);
      text-decoration: none;
      transition: background 150ms;
    }
    .brand:hover { background: var(--bg-hover); }

    .brand-mark {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 36px;
      height: 36px;
      background: var(--green-soft);
      color: var(--green);
      border: 1px solid var(--border-active);
      border-radius: var(--radius-sm);
      flex-shrink: 0;
    }
    .brand-text {
      display: flex;
      flex-direction: column;
      gap: 2px;
      flex: 1;
      min-width: 0;
    }
    .brand-name {
      font-weight: 600;
      letter-spacing: -0.02em;
      font-size: var(--fs-base);
      line-height: 1.1;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .brand-name strong { color: var(--green); font-weight: 700; }
    .brand-meta {
      font-size: 0.65rem;
      color: var(--text-muted);
      text-transform: uppercase;
      letter-spacing: 0.06em;
      font-weight: 500;
    }
    .brand-tag {
      font-size: 0.6rem;
      padding: 2px var(--sp-2);
      flex-shrink: 0;
    }

    /* Nav */
    .nav {
      display: flex;
      flex-direction: column;
      gap: var(--sp-1);
      flex: 1;
    }
    .nav-section {
      font-size: 0.65rem;
      font-weight: 600;
      text-transform: uppercase;
      letter-spacing: 0.1em;
      color: var(--text-soft);
      padding: 0 var(--sp-3);
      margin-bottom: var(--sp-2);
    }

    .nav-link {
      position: relative;
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-3);
      border-radius: var(--radius-sm);
      color: var(--text-muted);
      text-decoration: none;
      font-size: var(--fs-sm);
      font-weight: 500;
      transition: all 150ms ease;
      overflow: hidden;
    }
    .nav-link:hover {
      background: var(--bg-hover);
      color: var(--text-main);
    }
    .nav-link.active {
      background: var(--green-soft);
      color: var(--green);
    }
    .nav-link.active::before {
      content: '';
      position: absolute;
      left: 0;
      top: var(--sp-2);
      bottom: var(--sp-2);
      width: 3px;
      background: var(--green);
      border-radius: 0 2px 2px 0;
    }

    .nav-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
      transition: transform 200ms;
    }
    .nav-link:hover .nav-icon { transform: scale(1.08); }
    .nav-link.active .nav-icon { color: var(--green); }

    .nav-label { flex: 1; }

    .nav-pulse {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: var(--green);
      opacity: 0;
      box-shadow: 0 0 0 0 rgba(47, 219, 120, 0.6);
      flex-shrink: 0;
    }
    .nav-link.active .nav-pulse {
      opacity: 1;
      animation: nav-pulse 2s ease-out infinite;
    }
    @keyframes nav-pulse {
      0%   { box-shadow: 0 0 0 0 rgba(47, 219, 120, 0.6); }
      70%  { box-shadow: 0 0 0 8px rgba(47, 219, 120, 0); }
      100% { box-shadow: 0 0 0 0 rgba(47, 219, 120, 0); }
    }

    /* User card */
    .user {
      margin-top: auto;
    }
    .user-card {
      display: flex;
      align-items: center;
      gap: var(--sp-3);
      padding: var(--sp-3);
      background: var(--bg-card-soft);
      border: 1px solid var(--border-soft);
      border-radius: var(--radius-md);
      transition: border-color 200ms;
    }
    .user-card:hover { border-color: var(--border-active); }

    .user-avatar {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 36px;
      height: 36px;
      background: linear-gradient(135deg, var(--green) 0%, var(--green-hover) 100%);
      color: #050B10;
      border-radius: 50%;
      font-weight: 700;
      font-size: var(--fs-sm);
      flex-shrink: 0;
    }
    .user-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
      flex: 1;
      min-width: 0;
    }
    .user-name {
      font-size: var(--fs-sm);
      font-weight: 600;
      color: var(--text-main);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .user-email {
      font-size: 0.7rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .logout {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      width: 32px;
      height: 32px;
      background: transparent;
      border: 1px solid var(--border);
      border-radius: var(--radius-sm);
      color: var(--text-muted);
      cursor: pointer;
      transition: all 150ms;
      flex-shrink: 0;
    }
    .logout:hover {
      background: rgba(255, 64, 87, 0.12);
      border-color: var(--red);
      color: var(--red);
    }

    /* ===== Main ===== */
    .main {
      padding: var(--sp-6);
      min-width: 0;
      overflow-x: hidden;
    }
    /* Mobile (< 768px): tighter padding + extra room at bottom for the
       fixed bottom-nav (76px = ~56px nav + safe-area-inset fallback). */
    @media (max-width: 767px) {
      .main {
        padding: var(--sp-4);
        padding-bottom: calc(76px + env(safe-area-inset-bottom, 0px));
      }
    }
  `],
})
export class TraderShell {
  readonly auth = inject(AuthState);
  private readonly router = inject(Router);

  readonly navItems: NavItem[] = [
    { label: 'Dashboard',   path: 'dashboard', icon: 'dashboard' },
    { label: 'Operaciones', path: 'trades',    icon: 'trades' },
    { label: 'Strategies',  path: 'strategies', icon: 'list' },
    { label: 'Diario',      path: 'journal',   icon: 'journal' },
    { label: 'Patrones',    path: 'patterns',  icon: 'list' },
    { label: 'Settings',    path: 'settings',  icon: 'settings' },
  ];

  userInitial(): string {
    const email = this.auth.user()?.email ?? '?';
    return email.charAt(0).toUpperCase();
  }

  userShort(): string {
    const email = this.auth.user()?.email ?? '';
    const local = email.split('@')[0] ?? email;
    return local.length > 16 ? local.slice(0, 14) + '…' : local;
  }

  logout(): void {
    this.auth.logout();
    this.router.navigateByUrl('/auth/login');
  }
}
