import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { ForgotPasswordPage } from '../forgot-password.page';

describe('ForgotPasswordPage', () => {
  let fixture: ReturnType<typeof TestBed.createComponent<ForgotPasswordPage>>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ForgotPasswordPage],
      providers: [provideHttpClient(), provideRouter([])],
    }).compileComponents();
    fixture = TestBed.createComponent(ForgotPasswordPage);
    fixture.detectChanges();
  });

  it('DisablesSubmitWhileLoading', async () => {
    const page = fixture.componentInstance;
    page.form.controls.email.setValue('user@example.com');
    fixture.detectChanges();

    const btn = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(btn.disabled).toBe(false);
    expect(btn.getAttribute('aria-busy')).toBe('false');

    page.loading.set(true);
    fixture.detectChanges();

    expect(btn.disabled).toBe(true);
    expect(btn.getAttribute('aria-busy')).toBe('true');

    page.loading.set(false);
    fixture.detectChanges();
    expect(btn.disabled).toBe(false);
  });

  it('AnnouncesStatus_AriaLive', () => {
    const status = fixture.nativeElement.querySelector('#status') as HTMLElement;
    expect(status).not.toBeNull();
    expect(status.getAttribute('aria-live')).toBe('polite');
    expect(status.getAttribute('role')).toBe('status');

    const page = fixture.componentInstance;
    page.status.set('Si el correo está registrado, recibirás las instrucciones en breve.');
    fixture.detectChanges();

    expect(status.textContent?.trim()).toContain('recibirás las instrucciones');
  });

  it('KeyboardOrder_MobileViewport_360px', () => {
    const focusables = Array.from(
      fixture.nativeElement.querySelectorAll<HTMLElement>(
        'a[href], input:not([type="hidden"]), button:not([disabled])',
      ),
    );
    expect(focusables.length).toBeGreaterThanOrEqual(2);

    const tabOrder = focusables.map((el) => {
      const style = getComputedStyle(el).order;
      const rect = el.getBoundingClientRect();
      return {
        tag: el.tagName.toLowerCase(),
        top: Math.round(rect.top),
        order: style === 'normal' || style === '0' ? 0 : Number(style),
      };
    });

    for (let i = 1; i < tabOrder.length; i++) {
      const prev = tabOrder[i - 1];
      const cur = tabOrder[i];
      if (prev.order === cur.order) {
        expect(cur.top).toBeGreaterThanOrEqual(prev.top);
      } else {
        expect(cur.order).toBeGreaterThanOrEqual(prev.order);
      }
    }

    const back = fixture.nativeElement.querySelector('a.back') as HTMLAnchorElement;
    const input = fixture.nativeElement.querySelector('input[type="email"]') as HTMLInputElement;
    const submit = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(back.compareDocumentPosition(input) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(input.compareDocumentPosition(submit) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });
});
