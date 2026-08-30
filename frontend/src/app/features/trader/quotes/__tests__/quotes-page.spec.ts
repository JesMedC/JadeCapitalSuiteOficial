import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { QuotesPage } from '../quotes-page';
import { QuotesService } from '../api/quotes.service';
import { QuoteDto } from '../api/quotes.types';

jest.spyOn(console, 'warn').mockImplementation(() => {});

describe('QuotesPage', () => {
  let fixture: ComponentFixture<QuotesPage>;
  let component: QuotesPage;
  let svcMock: jest.Mocked<QuotesService>;

  const sampleQuote: QuoteDto = {
    symbol: 'EURUSD',
    bid: 1.0850,
    ask: 1.0851,
    spread: 0.0001,
    volume24h: 150000,
    timestamp: '2026-08-19T14:00:00Z',
    source: 0,
  };

  beforeEach(async () => {
    svcMock = {
      getBySymbol: jest.fn(),
      getBulk: jest.fn(),
    } as unknown as jest.Mocked<QuotesService>;

    svcMock.getBulk.mockResolvedValue([sampleQuote]);

    await TestBed.configureTestingModule({
      imports: [QuotesPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: QuotesService, useValue: svcMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(QuotesPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('renders the quotes title', () => {
    const h1 = fixture.nativeElement.querySelector('.quotes-title');
    expect(h1?.textContent).toContain('Cotizaciones');
  });

  it('exposes method bindings for the toolbar', () => {
    expect(typeof component.refresh).toBe('function');
    expect(typeof component.addSymbol).toBe('function');
    expect(typeof component.canAdd).toBe('function');
  });

  it('canAdd returns false for empty or duplicate symbols', () => {
    component.newSymbol.set('');
    expect(component.canAdd()).toBe(false);
    component.newSymbol.set('EURUSD');
    expect(component.canAdd()).toBe(false);
    component.newSymbol.set('AUDUSD');
    expect(component.canAdd()).toBe(true);
  });

  it('asInputValue extracts the raw value from an event target', () => {
    const event = { target: { value: 'foo' } } as any;
    expect(component.asInputValue(event)).toBe('foo');
  });
});
