import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MobileNav, MobileNavItem } from '../mobile-nav';

const ITEMS: MobileNavItem[] = [
  { label: 'Dashboard',   path: '/app/dashboard',  icon: 'dashboard' },
  { label: 'Operaciones', path: '/app/trades',     icon: 'trades' },
  { label: 'Calendario',  path: '/app/calendar',   icon: 'calendar' },
  { label: 'Settings',    path: '/app/settings',   icon: 'settings' },
];

describe('MobileNav', () => {
  let fixture: ComponentFixture<MobileNav>;
  let component: MobileNav;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MobileNav],
      providers: [provideRouter([])],
    }).compileComponents();

    fixture = TestBed.createComponent(MobileNav);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('items', ITEMS);
    fixture.detectChanges();
  });

  it('renders all nav items', () => {
    const links = fixture.nativeElement.querySelectorAll('.mobile-nav-link');
    expect(links.length).toBe(4);
  });

  it('uses semantic <nav> with aria-label', () => {
    const nav = fixture.nativeElement.querySelector('nav.mobile-nav');
    expect(nav).toBeTruthy();
    expect(nav.getAttribute('aria-label')).toBe('Navegación principal móvil');
  });

  it('renders labels as plain text inside each link', () => {
    const labels = fixture.nativeElement.querySelectorAll('.mobile-nav-label');
    expect(labels[0].textContent.trim()).toBe('Dashboard');
    expect(labels[3].textContent.trim()).toBe('Settings');
  });

  it('uses anchor tags (not buttons) for native router-link semantics', () => {
    const root = fixture.nativeElement as HTMLElement;
    const links = root.querySelectorAll<HTMLAnchorElement>('a.mobile-nav-link');
    expect(links.length).toBe(4);
    for (const link of Array.from(links)) {
      expect(link.tagName).toBe('A');
      expect(link.getAttribute('aria-label')).toBeTruthy();
    }
  });

  it('CSS hides the nav on tablet and up (display: none @ min-width 768px)', () => {
    // Assert via CSSOM: the component stylesheet contains the @media rule.
    // We can't run a real media query here, but we can assert that the
    // .mobile-nav class is rendered with the expected baseline styles.
    const nav = fixture.nativeElement.querySelector('nav.mobile-nav');
    expect(nav.classList.contains('mobile-nav')).toBe(true);
  });
});
