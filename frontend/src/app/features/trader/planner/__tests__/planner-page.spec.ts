import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { PlannerPage } from '../planner-page';

jest.spyOn(console, 'warn').mockImplementation(() => {});

describe('PlannerPage', () => {
  let fixture: import('@angular/core/testing').ComponentFixture<PlannerPage>;
  let component: PlannerPage;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [PlannerPage],
      providers: [provideRouter([]), provideHttpClient()],
    }).compileComponents();

    fixture = TestBed.createComponent(PlannerPage);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('exposes navigation methods', () => {
    expect(typeof component.prevWeek).toBe('function');
    expect(typeof component.nextWeek).toBe('function');
    expect(typeof component.thisWeek).toBe('function');
  });

  it('formats PnL with sign', () => {
    expect(component.formatPnl(150)).toBe('+150.00');
    expect(component.formatPnl(-50)).toBe('-50.00');
  });

  it('statusLabel maps statuses correctly', () => {
    expect(component.statusLabel(1)).toBe('Planeada');
    expect(component.statusLabel(2)).toBe('Completada');
    expect(component.statusLabel(3)).toBe('Saltada');
    expect(component.statusLabel(4)).toBe('Cancelada');
  });
});
