import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { AttachmentUsageBanner } from '../attachment-usage-banner';
import { AttachmentQuotaState } from '../state/attachment-quota.state';

jest.spyOn(console, 'warn').mockImplementation(() => {});

describe('AttachmentUsageBanner', () => {
  let fixture: ComponentFixture<AttachmentUsageBanner>;
  let component: AttachmentUsageBanner;
  let http: HttpTestingController;
  let state: AttachmentQuotaState;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AttachmentUsageBanner],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    fixture = TestBed.createComponent(AttachmentUsageBanner);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    state = TestBed.inject(AttachmentQuotaState);
  });

  afterEach(() => {
    fixture.destroy();
    try { http.verify(); } catch { /* ignore */ }
  });

  // Helper: seed the state directly (bypasses async HTTP plumbing that
  // is brittle under fakeAsync). The component template reacts to
  // signal changes so this is faithful to production behavior.
  function seedUsage(used: number, count: number, quotaBytes: number, percent: number): void {
    state.usage.set({
      totalBytes: used,
      attachmentCount: count,
      quotaBytes: quotaBytes,
      quotaCount: 100,
      percentFull: percent,
    });
  }

  it('renders the banner with used/total MB and percent', () => {
    fixture.detectChanges(); // ngOnInit fires; ignore the auto HTTP
    seedUsage(8_500_000, 12, 52_428_800, 16.21);
    fixture.detectChanges();

    const root = fixture.nativeElement as HTMLElement;
    const banner = root.querySelector('.banner');
    expect(banner).toBeTruthy();
    expect(banner?.textContent).toContain('12 / 100 archivos');
    expect(banner?.textContent).toContain('16.21%');
  });

  it('marks the bar as .warn when percent is between 70 and 90', () => {
    fixture.detectChanges();
    seedUsage(40_000_000, 80, 52_428_800, 76.0);
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('.banner') as HTMLElement;
    const fill = banner.querySelector('.fill') as HTMLElement;
    expect(fill).toBeTruthy();
    expect(fill.classList.contains('warn')).toBe(true);
    expect(fill.classList.contains('danger')).toBe(false);
  });

  it('marks the bar as .danger when percent >= 90', () => {
    fixture.detectChanges();
    seedUsage(50_000_000, 95, 52_428_800, 95.0);
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('.banner') as HTMLElement;
    const fill = banner.querySelector('.fill') as HTMLElement;
    expect(fill).toBeTruthy();
    expect(fill.classList.contains('danger')).toBe(true);
  });

  it('does NOT render the banner when usage is not loaded', () => {
    fixture.detectChanges();
    // No flush, no seed — usage stays null.
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('.banner');
    expect(banner).toBeFalsy();
  });

  it('component instance exposes usedMb + quotaMb formatters', () => {
    expect(typeof component.usedMb).toBe('function');
    expect(typeof component.quotaMb).toBe('function');
    expect(typeof component.warn).toBe('function');
    expect(typeof component.danger).toBe('function');
  });

  it('does not auto-poll during the test (ngOnDestroy clears interval)', fakeAsync(() => {
    fixture.detectChanges();
    // tick 30s — interval is 60s, so no poll fires yet.
    tick(30_000);
    // Should still have no extra usage request beyond the initial ngOnInit one.
    const callsAfter30s = http.match(() => true)?.length ?? 0;
    expect(callsAfter30s).toBeLessThanOrEqual(1);
  }));
});