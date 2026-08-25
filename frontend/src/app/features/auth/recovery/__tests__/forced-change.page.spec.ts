import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { ForcedChangePage } from '../forced-change.page';
import { AuthState } from '@core/state/auth.state';

describe('ForcedChangePage', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<ForcedChangePage>>;
  let auth: AuthState;

  beforeEach(async () => {
    sessionStorage.clear();
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ForcedChangePage],
      providers: [provideHttpClient(), provideRouter([])],
    }).compileComponents();
    auth = TestBed.inject(AuthState);
    auth.markPasswordChangeRequired('grant-jti-abc', 1);
    fixture = TestBed.createComponent(ForcedChangePage);
    fixture.detectChanges();
  });

  it('DisablesSubmitWhileLoading', () => {
    const btn = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(btn).not.toBeNull();
    expect(btn.disabled).toBe(true);

    const page = fixture.componentInstance;
    page.form.controls.newPassword.setValue('Aa1!aaaaaaaaa');
    fixture.detectChanges();
    expect(btn.disabled).toBe(false);
    expect(btn.getAttribute('aria-busy')).toBe('false');

    page.loading.set(true);
    fixture.detectChanges();
    expect(btn.disabled).toBe(true);
    expect(btn.getAttribute('aria-busy')).toBe('true');
  });

  it('AnnouncesStatus_AriaLive', () => {
    const status = fixture.nativeElement.querySelector('#status') as HTMLElement;
    expect(status).not.toBeNull();
    expect(status.getAttribute('aria-live')).toBe('assertive');
    expect(status.getAttribute('role')).toBe('alert');

    const page = fixture.componentInstance;
    page.status.set('No pudimos cambiar la contraseña. Intentá nuevamente.');
    fixture.detectChanges();

    expect(status.textContent?.trim()).toContain('No pudimos cambiar');
  });

  it('KeyboardOrder_MobileViewport_360px', () => {
    const input = fixture.nativeElement.querySelector('input[type="password"]') as HTMLInputElement;
    const submit = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(input).not.toBeNull();
    expect(submit).not.toBeNull();

    expect(input.tabIndex).toBeGreaterThanOrEqual(0);
    expect(submit.tabIndex).toBeGreaterThanOrEqual(0);

    expect(input.compareDocumentPosition(submit) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    const root = fixture.nativeElement as HTMLElement;
    const labels = Array.from(root.querySelectorAll<HTMLLabelElement>('label'));
    const labelForInput = labels.find((l) => l.htmlFor === 'newPassword');
    expect(labelForInput).toBeDefined();
    expect(labelForInput!.compareDocumentPosition(input) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
