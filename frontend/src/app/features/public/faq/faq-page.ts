import { ChangeDetectionStrategy, Component } from '@angular/core';

interface Faq { q: string; a: string; }

const FAQ: Faq[] = [
  { q: '¿Puedo usar Jade Capital para opciones binarias?', a: 'Sí. Soportamos registro de operaciones binarias con payout, stake y resultado.' },
  { q: '¿Emiten señales o recomendaciones?', a: 'No. Jade Capital es exclusivamente una herramienta de registro y análisis. No provee señales, consejos ni gestión de capital.' },
  { q: '¿Mis datos están seguros?', a: 'Sí. Las contraseñas se almacenan con PBKDF2 + sal aleatoria. Los tokens opacos de refresco se guardan como hash SHA-256.' },
  { q: '¿Cuántos trades puedo registrar?', a: 'Depende de tu plan: Starter 200/mes, Pro 2.000/mes, Elite ilimitado.' },
];

@Component({
  selector: 'jcs-faq-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="jcs-container">
      <header><h1>Preguntas frecuentes</h1></header>
      @for (item of items; track item.q) {
        <details class="jcs-card faq">
          <summary>{{ item.q }}</summary>
          <p>{{ item.a }}</p>
        </details>
      }
    </div>
  `,
  styles: [`
    header { padding: var(--jcs-space-12) 0 var(--jcs-space-6); }
    .faq { margin-bottom: var(--jcs-space-3); }
    summary { cursor: pointer; font-weight: 600; }
    p { color: var(--jcs-color-text-muted); margin-top: var(--jcs-space-2); }
  `],
})
export class FaqPage {
  readonly items = FAQ;
}