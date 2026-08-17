import {
  ChangeDetectionStrategy,
  Component,
  inject,
  signal,
  OnInit,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TradeReviewService } from '@core/api/trade-review.service';
import { TradeReviewDto } from '@core/api/trade-review.types';
import { PostTradeReviewComponent } from '../trades/post-trade-review.component';

// ============================================================================
//  TradeDetailPage — slice 1d.2 frontend.
//
//  Wrapper page (angular route `trades/:tradeId`) que:
//  1) Lee el review existente del server para pre-poblar el formulario
//     (si hay review para este trade; sino null = form vacio).
//  2) Hospeda el PostTradeReviewComponent.
//  3) Despues de guardar, redirige al usuario al listado de trades.
//
//  Si el backend devuelve 404 al GET /review (aun no se creo ninguno),
//  el componente se monta con `existing = null`, en modo "create".
// ============================================================================

@Component({
  selector: 'jcs-trade-detail',
  standalone: true,
  imports: [CommonModule, RouterLink, PostTradeReviewComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="td-shell">
      <nav class="td-back">
        <a routerLink="/trader/trades">← Volver al listado</a>
      </nav>

      @if (isLoading()) {
        <p class="td-muted">Cargando review...</p>
      } @else {
        <jcs-post-trade-review
          [tradeId]="tradeId()"
          [existing]="existing()"
          (saved)="onSaved($event)"
          (cancelled)="onCancelled()" />
      }
    </section>
  `,
  styles: [`
    .td-shell { padding: 1.5rem; max-width: 720px; margin: 0 auto; display: flex; flex-direction: column; gap: 1rem; }
    .td-back a { color: var(--jcs-primary, #6366f1); text-decoration: none; }
    .td-back a:hover { text-decoration: underline; }
    .td-muted { color: var(--jcs-muted, #9ca3af); font-size: 0.875rem; }
  `],
})
export class TradeDetailPage implements OnInit {
  private readonly api = inject(TradeReviewService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly tradeId = signal<string>('');
  readonly existing = signal<TradeReviewDto | null>(null);
  readonly isLoading = signal<boolean>(true);

  async ngOnInit(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('tradeId') ?? '';
    this.tradeId.set(id);

    try {
      const dto = await this.api.get(id);
      this.existing.set(dto);
    } catch {
      this.existing.set(null);
    } finally {
      this.isLoading.set(false);
    }
  }

  onSaved(dto: TradeReviewDto): void {
    this.router.navigate(['/trader/trades']);
  }

  onCancelled(): void {
    this.router.navigate(['/trader/trades']);
  }
}
