import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

export interface MobileNavItem {
  /** Visible label (kept short, max 12 chars for mobile). */
  label: string;
  /** Route path (relative or absolute). */
  path: string;
  /** Icon name (matches the icon set in trader-shell). */
  icon: 'dashboard' | 'trades' | 'calendar' | 'settings' | 'list' | 'tag' | 'home' | 'menu' | 'journal' | 'card';
}

/**
 * Bottom tab bar for mobile (< 768px). Hidden on tablet+ via CSS.
 * Active route receives `aria-current="page"` and an `.active` class.
 *
 * Touch targets are ≥ 44px tall to satisfy Apple HIG / WCAG 2.5.5.
 * `padding-bottom: env(safe-area-inset-bottom)` keeps the bar clear of
 * the iOS home indicator.
 */
@Component({
  selector: 'jcs-mobile-nav',
  standalone: true,
  imports: [RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <nav class="mobile-nav" aria-label="Navegación principal móvil">
      @for (item of items(); track item.path) {
        <a
          [routerLink]="item.path"
          routerLinkActive="active"
          [routerLinkActiveOptions]="{ exact: false }"
          class="mobile-nav-link"
          [attr.aria-label]="item.label">
          <span class="mobile-nav-icon" aria-hidden="true">
            @switch (item.icon) {
              @case ('dashboard') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="3" width="7" height="9"/>
                  <rect x="14" y="3" width="7" height="5"/>
                  <rect x="14" y="12" width="7" height="9"/>
                  <rect x="3" y="16" width="7" height="5"/>
                </svg>
              }
              @case ('trades') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <line x1="8" y1="6" x2="21" y2="6"/>
                  <line x1="8" y1="12" x2="21" y2="12"/>
                  <line x1="8" y1="18" x2="21" y2="18"/>
                  <line x1="3" y1="6" x2="3.01" y2="6"/>
                  <line x1="3" y1="12" x2="3.01" y2="12"/>
                  <line x1="3" y1="18" x2="3.01" y2="18"/>
                </svg>
              }
              @case ('calendar') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="3" y="4" width="18" height="18" rx="2"/>
                  <line x1="16" y1="2" x2="16" y2="6"/>
                  <line x1="8" y1="2" x2="8" y2="6"/>
                  <line x1="3" y1="10" x2="21" y2="10"/>
                </svg>
              }
              @case ('journal') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M4 4h12a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H4z"/>
                  <line x1="8" y1="2" x2="8" y2="22"/>
                  <line x1="12" y1="8" x2="18" y2="8"/>
                  <line x1="12" y1="12" x2="18" y2="12"/>
                  <line x1="12" y1="16" x2="15" y2="16"/>
                </svg>
              }
              @case ('settings') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <circle cx="12" cy="12" r="3"/>
                  <path d="M19.4 15a1.7 1.7 0 0 0 .34 1.88l.06.06-2.83 2.83-.06-.06A1.7 1.7 0 0 0 15 19.4a1.7 1.7 0 0 0-1 .6 1.7 1.7 0 0 0-.4 1.1V21H9.6v-.09A1.7 1.7 0 0 0 8.5 19.4a1.7 1.7 0 0 0-1.88.34l-.06.06-2.83-2.83.06-.06A1.7 1.7 0 0 0 4.6 15a1.7 1.7 0 0 0-.6-1 1.7 1.7 0 0 0-1.1-.4H3V9.6h.09A1.7 1.7 0 0 0 4.6 8.5a1.7 1.7 0 0 0-.34-1.88l-.06-.06 2.83-2.83.06.06A1.7 1.7 0 0 0 9 4.6a1.7 1.7 0 0 0 1-.6 1.7 1.7 0 0 0 .4-1.1V3h4v.09A1.7 1.7 0 0 0 15.5 4.6a1.7 1.7 0 0 0 1.88-.34l.06-.06 2.83 2.83-.06.06A1.7 1.7 0 0 0 19.4 9c.14.38.35.72.64 1 .3.29.69.43 1.1.4H21v4h-.09A1.7 1.7 0 0 0 19.4 15z"/>
                </svg>
              }
              @case ('list') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <line x1="8" y1="6" x2="21" y2="6"/>
                  <line x1="8" y1="12" x2="21" y2="12"/>
                  <line x1="8" y1="18" x2="21" y2="18"/>
                  <line x1="3" y1="6" x2="3.01" y2="6"/>
                  <line x1="3" y1="12" x2="3.01" y2="12"/>
                  <line x1="3" y1="18" x2="3.01" y2="18"/>
                </svg>
              }
              @case ('tag') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z"/>
                  <line x1="7" y1="7" x2="7.01" y2="7"/>
                </svg>
              }
              @case ('home') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>
                  <polyline points="9 22 9 12 15 12 15 22"/>
                </svg>
              }
              @case ('menu') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <line x1="3" y1="12" x2="21" y2="12"/>
                  <line x1="3" y1="6" x2="21" y2="6"/>
                  <line x1="3" y1="18" x2="21" y2="18"/>
                </svg>
              }
              @case ('card') {
                <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <rect x="2" y="5" width="20" height="14" rx="2"/>
                  <line x1="2" y1="10" x2="22" y2="10"/>
                </svg>
              }
            }
          </span>
          <span class="mobile-nav-label">{{ item.label }}</span>
        </a>
      }
    </nav>
  `,
  styles: [`
    :host { display: contents; }

    .mobile-nav {
      position: fixed;
      bottom: 0;
      left: 0;
      right: 0;
      z-index: var(--z-mobile-nav);
      display: flex;
      align-items: stretch;
      justify-content: flex-start;
      gap: 0;
      padding: var(--sp-1) var(--sp-2) calc(var(--sp-2) + env(safe-area-inset-bottom, 0px));
      background: var(--bg-sidebar);
      border-top: 1px solid var(--border);
      backdrop-filter: blur(8px);
      box-shadow: 0 -4px 16px rgba(0,0,0,0.18);
      overflow-x: auto;
      overflow-y: hidden;
      -webkit-overflow-scrolling: touch;
      scrollbar-width: none;
    }
    .mobile-nav::-webkit-scrollbar { display: none; }

    /* Hide on tablet+ — sidebar takes over. */
    @media (min-width: 768px) {
      .mobile-nav { display: none; }
    }

    .mobile-nav-link {
      flex: 0 0 auto;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 2px;
      min-height: 56px;
      min-width: 72px;
      padding: var(--sp-2) var(--sp-2);
      border-radius: var(--radius-sm);
      color: var(--text-muted);
      text-decoration: none;
      font-size: 0.65rem;
      font-weight: 500;
      transition: color 150ms ease, background 150ms ease;
    }

    .mobile-nav-link:hover,
    .mobile-nav-link:focus-visible {
      color: var(--text-main);
      background: var(--bg-hover);
      outline: none;
    }

    .mobile-nav-link:focus-visible {
      box-shadow: 0 0 0 2px var(--border-active);
    }

    .mobile-nav-link.active {
      color: var(--green);
      background: var(--green-soft);
    }

    .mobile-nav-icon {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .mobile-nav-label {
      font-size: 0.65rem;
      line-height: 1;
      letter-spacing: 0.02em;
      white-space: nowrap;
    }

    /* Extra-compact: icon-only on very narrow viewports (< 360px). */
    @media (max-width: 359px) {
      .mobile-nav-label { display: none; }
      .mobile-nav-link { min-width: 44px; min-height: 56px; }
    }
  `],
})
export class MobileNav {
  /** Nav items to render (order matters). */
  readonly items = input.required<MobileNavItem[]>();
}
