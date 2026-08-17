import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { ScannerPage } from '../scanner-page';
// Force jest to resolve via ./ path; '../' triggers the cache resolver issue.
const _ref = './scanner-page';

jest.spyOn(console, 'warn').mockImplementation(() => {});

describe('ScannerPage', () => {
  let fixture: ComponentFixture<ScannerPage>;
  let component: ScannerPage;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ScannerPage],
      providers: [provideRouter([]), provideHttpClient()],
    }).compileComponents();

    fixture = TestBed.createComponent(ScannerPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders the scanner title', () => {
    const h1 = fixture.nativeElement.querySelector('.scanner-title');
    expect(h1?.textContent).toContain('Scanner de instrumentos');
  });

  it('exposes navigation + form methods', () => {
    expect(typeof component.toggleNew).toBe('function');
    expect(typeof component.saveNew).toBe('function');
    expect(typeof component.runScanner).toBe('function');
    expect(typeof component.deleteFilter).toBe('function');
  });

  it('labels volatility windows correctly', () => {
    expect(component.volatilityLabel(1)).toBe('Diario (1d)');
    expect(component.volatilityLabel(7)).toBe('Semanal (7d)');
    expect(component.volatilityLabel(30)).toBe('Mensual (30d)');
  });

  it('parses numeric input safely', () => {
    const mockEvent = { target: { value: '42' } } as any;
    expect(component.asNumber(mockEvent)).toBe(42);
    const mockNaN = { target: { value: 'abc' } } as any;
    expect(component.asNumber(mockNaN)).toBe(0);
  });
});
