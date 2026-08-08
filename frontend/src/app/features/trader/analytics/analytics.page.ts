import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'jcs-analytics',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Análisis</h2>
    <p class="jcs-muted">Win rate, expectancy, drawdown. Módulo en construcción.</p>
  `,
})
export class AnalyticsPage {}