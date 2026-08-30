import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CookieConsentService } from './cookie-consent.service';

describe('CookieConsentService', () => {
  let service: CookieConsentService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(CookieConsentService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    localStorage.clear();
  });

  it('setChoice("all") writes localStorage.jade.consent and POSTs /api/auth/consent', async () => {
    expect(service.shouldShowBanner()).toBe(true);

    const promise = service.setChoice('all');

    const req = httpMock.expectOne('/api/auth/consent');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ choice: 'all' });
    req.flush({ choice: 'all', cookieConsentAcceptedAt: new Date().toISOString() });

    await promise;

    expect(service.choice()).toBe('all');
    expect(service.shouldShowBanner()).toBe(false);

    const stored = JSON.parse(localStorage.getItem('jade.consent') ?? '{}');
    expect(stored.choice).toBe('all');
    expect(typeof stored.decidedAt).toBe('string');
  });

  it('canLoadAnalytics returns true for "all" choice and false for "essential" choice', () => {
    expect(service.canLoadAnalytics()).toBe(false);

    localStorage.setItem(
      'jade.consent',
      JSON.stringify({ choice: 'all', decidedAt: new Date().toISOString() }),
    );
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const allSvc = TestBed.inject(CookieConsentService);
    expect(allSvc.canLoadAnalytics()).toBe(true);

    localStorage.setItem(
      'jade.consent',
      JSON.stringify({ choice: 'essential', decidedAt: new Date().toISOString() }),
    );
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const essentialSvc = TestBed.inject(CookieConsentService);
    expect(essentialSvc.canLoadAnalytics()).toBe(false);

    // Cleanup for downstream tests in this suite
    localStorage.clear();
  });

  it('shouldShowBanner returns false when localStorage.jade.consent is present', () => {
    localStorage.setItem(
      'jade.consent',
      JSON.stringify({ choice: 'essential', decidedAt: new Date().toISOString() }),
    );
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const svc = TestBed.inject(CookieConsentService);
    expect(svc.shouldShowBanner()).toBe(false);
    expect(svc.choice()).toBe('essential');

    localStorage.clear();
  });
});
