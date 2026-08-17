import { TestBed } from '@angular/core/testing';
import { MfeMaeMiniChart } from '../mfe-mae-mini-chart';

// ============================================================================
//  MfeMaeMiniChart — slice 2c (Trader Journal Core / MFE/MAE Charts).
//
//  Two user-required specs:
//   1. Renders the MFE + MAE bars when both are non-null, with the right
//      colors (green for MFE positive, red for MAE negative).
//   2. Null state: when both MFE and MAE are null, the chart renders the
//      "—" placeholder instead of bars.
//
//  The component is intentionally dumb: it does NOT load any data. The
//  parent (trades-list.page.ts) injects the per-trade MFE/MAE via
//  component inputs. The chart renders bars proportional to the larger
//  of the two magnitudes (so they stay visually comparable across the
//  user's history).
// ============================================================================

describe('MfeMaeMiniChart', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [MfeMaeMiniChart],
    }).compileComponents();
  });

  it('renders MFE + MAE bars with green/red colors when both are populated', () => {
    const fixture = TestBed.createComponent(MfeMaeMiniChart);
    fixture.componentRef.setInput('mfeAmount', 10);
    fixture.componentRef.setInput('maeAmount', -5);
    fixture.componentRef.setInput('currency', 'USD');
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;

    // Both bars present.
    const bars = html.querySelectorAll('.mmc-bar');
    expect(bars.length).toBe(2);

    // MFE bar is green (positive). The component marks the MFE bar with
    // a class `mmc-bar--mfe`; CSS uses background-color: var(--green).
    const mfeBar = html.querySelector('.mmc-bar--mfe') as HTMLElement;
    expect(mfeBar).not.toBeNull();
    expect(mfeBar.classList.contains('mmc-bar--positive')).toBe(true);

    // MAE bar is red (negative). Marked with `mmc-bar--mae` and
    // `mmc-bar--negative` so CSS styles it with var(--red).
    const maeBar = html.querySelector('.mmc-bar--mae') as HTMLElement;
    expect(maeBar).not.toBeNull();
    expect(maeBar.classList.contains('mmc-bar--negative')).toBe(true);

    // Bar widths: the MFE bar (|10|) should be 2× the MAE bar (|5|) so
    // the chart visually encodes magnitude.
    const mfeWidth = parseFloat(mfeBar.style.width);
    const maeWidth = parseFloat(maeBar.style.width);
    expect(mfeWidth).toBeGreaterThan(maeWidth);
  });

  it('renders the "—" placeholder when both MFE and MAE are null (open trade)', () => {
    const fixture = TestBed.createComponent(MfeMaeMiniChart);
    fixture.componentRef.setInput('mfeAmount', null);
    fixture.componentRef.setInput('maeAmount', null);
    fixture.componentRef.setInput('currency', 'USD');
    fixture.detectChanges();

    const html = fixture.nativeElement as HTMLElement;

    // No bars in null state.
    const bars = html.querySelectorAll('.mmc-bar');
    expect(bars.length).toBe(0);

    // The "—" placeholder is rendered.
    expect(html.textContent).toContain('—');
  });
});