import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { ImportsPage } from '../imports-page';
import { ImportsService } from '../api/imports.service';
import { ImportsState } from '../state/imports.state';

jest.spyOn(console, 'warn').mockImplementation(() => {});

describe('ImportsPage', () => {
  let fixture: ComponentFixture<ImportsPage>;
  let component: ImportsPage;
  let svcMock: jest.Mocked<ImportsService>;
  let state: ImportsState;

  beforeEach(async () => {
    svcMock = {
      uploadCsv: jest.fn(),
      getStatus: jest.fn(),
    } as unknown as jest.Mocked<ImportsService>;

    await TestBed.configureTestingModule({
      imports: [ImportsPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        { provide: ImportsService, useValue: svcMock },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(ImportsPage);
    component = fixture.componentInstance;
    state = TestBed.inject(ImportsState);
    fixture.detectChanges();
  });

  it('renders the imports title', () => {
    const h1 = fixture.nativeElement.querySelector('.imports-title');
    expect(h1?.textContent).toContain('Importar historial de trades');
  });

  it('exposes helper methods', () => {
    expect(typeof component.onFilePicked).toBe('function');
    expect(typeof component.onDrop).toBe('function');
    expect(typeof component.asInputValue).toBe('function');
    expect(typeof component.upload).toBe('function');
    expect(typeof component.reset).toBe('function');
  });

  it('canUpload returns false when no file is selected', () => {
    expect(component.canUpload()).toBe(false);
  });

  it('canUpload returns false when accountId is empty', () => {
    component.selectedFile.set(new File(['x'], 'trades.csv', { type: 'text/csv' }));
    component.accountId.set('');
    expect(component.canUpload()).toBe(false);
  });

  it('canUpload returns true when file + valid accountId are present', () => {
    component.selectedFile.set(new File(['x'], 'trades.csv', { type: 'text/csv' }));
    component.accountId.set('11111111-1111-1111-1111-111111111111');
    expect(component.canUpload()).toBe(true);
  });

  it('asInputValue extracts the raw value from an event target', () => {
    const event = { target: { value: 'foo' } } as any;
    expect(component.asInputValue(event)).toBe('foo');
  });
});