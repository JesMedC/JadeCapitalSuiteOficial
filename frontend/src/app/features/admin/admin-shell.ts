import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'jcs-admin-shell',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<h2>Administración</h2><p class="jcs-muted">Próximamente: gestión de usuarios y suscripciones.</p>`,
})
export class AdminShell {}