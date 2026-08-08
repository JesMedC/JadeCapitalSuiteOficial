import { ChangeDetectionStrategy, Component } from '@angular/core';
@Component({
  selector: 'jcs-calendar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h2>Calendario P&L</h2><p class="jcs-muted">FullCalendar aqui. Modulo en construccion.</p>`,
})
export class CalendarPage {}
