import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'jcs-settings',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2>Configuración</h2>
    <p class="jcs-muted">Cuenta, preferencias, moneda base. Módulo en construcción.</p>
  `,
})
export class SettingsPage {}