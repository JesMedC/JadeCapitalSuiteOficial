import { ChangeDetectionStrategy, Component } from '@angular/core';
@Component({
  selector: 'jcs-trades-list',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h2>Trades</h2><p class="jcs-muted">CRUD completo. Modulo en construccion.</p>`,
})
export class TradesListPage {}
