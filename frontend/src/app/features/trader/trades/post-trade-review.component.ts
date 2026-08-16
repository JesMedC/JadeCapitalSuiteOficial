import {
  ChangeDetectionStrategy,
  Component,
  EventEmitter,
  Input,
  Output,
  inject,
  signal,
  computed,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  TradeReviewService,
  uploadBytesToMinio,
} from '@core/api/trade-review.service';
import {
  ALLOWED_ATTACHMENT_CONTENT_TYPES,
  MAX_ATTACHMENTS_PER_REVIEW,
  MAX_ATTACHMENT_SIZE_BYTES,
  TradeAttachmentDto,
  TradeReviewDto,
  TradeReviewError,
  UpsertTradeReviewRequest,
} from '@core/api/trade-review.types';

// ============================================================================
//  PostTradeReviewComponent — slice 1d.2 frontend.
//
//  Standalone Angular 19 component (Signals + OnPush + SCSS).
//
//  Inputs:
//   - tradeId (required): the trade to attach the review to.
//
//  Outputs:
//   - saved: emitted with the saved DTO after a successful upsert.
//   - cancelled: emitted when the user cancels.
//
//  State (signals):
//   - emotionality (1..5), rating (1..5 or null), setupUsed, lessons.
//   - existingAttachments (server-side list).
//   - isSaving, isUploading, errorMessage.
//
//  Attachment upload flow:
//   1. Validate content-type + size client-side (mirror of server whitelist).
//   2. POST /attachments -> { attachmentId, putUrl, objectKey }.
//   3. PUT file bytes DIRECTLY to MinIO (no backend proxy).
//   4. POST /attachments/{id}/complete -> backend verifies size + flips status.
// ============================================================================

@Component({
  selector: 'jcs-post-trade-review',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './post-trade-review.component.html',
  styleUrls: ['./post-trade-review.component.scss'],
})
export class PostTradeReviewComponent {
  private readonly api = inject(TradeReviewService);

  /** Required trade id (uuid). */
  @Input({ required: true }) tradeId!: string;

  /** Existing review loaded from the server (null until `load()` resolves). */
  @Input() existing: TradeReviewDto | null = null;

  @Output() readonly saved = new EventEmitter<TradeReviewDto>();
  @Output() readonly cancelled = new EventEmitter<void>();

  // ===== Form state =====
  readonly emotionality = signal<number>(3); // default Neutral
  readonly rating = signal<number | null>(null);
  readonly setupUsed = signal<string>('');
  readonly lessons = signal<string>('');

  // ===== Attachments state =====
  readonly attachments = signal<TradeAttachmentDto[]>([]);
  readonly isUploadingFile = signal<boolean>(false);
  readonly uploadErrorMessage = signal<string | null>(null);

  // ===== Submission state =====
  readonly isSaving = signal<boolean>(false);
  readonly errorMessage = signal<string | null>(null);
  readonly fieldErrors = signal<Record<string, string>>({});

  // ===== Computed =====
  readonly canSubmit = computed(() =>
    !this.isSaving() &&
    this.emotionality() >= 1 && this.emotionality() <= 5 &&
    (this.rating() === null || (this.rating()! >= 1 && this.rating()! <= 5)) &&
    (this.setupUsed().length === 0 || this.setupUsed().length <= 64) &&
    this.lessons().length <= 5000
  );

  readonly currentAttachmentsCount = computed(() => this.attachments().length);
  readonly canUploadMore = computed(
    () => this.attachments().length < MAX_ATTACHMENTS_PER_REVIEW
  );

  readonly ALLOWED_CONTENT_TYPES = ALLOWED_ATTACHMENT_CONTENT_TYPES;
  readonly MAX_SIZE_BYTES = MAX_ATTACHMENT_SIZE_BYTES;
  readonly MAX_ATTACHMENTS = MAX_ATTACHMENTS_PER_REVIEW;

  // ===== Lifecycle hooks =====

  ngOnInit(): void {
    // Si el caller inyecta un review existente (e.g. desde la tab detail),
    // hidrata el form. Si es null, el form empieza vacio (review nuevo).
    const ex = this.existing;
    if (ex) {
      this.emotionality.set(ex.emotionality);
      this.rating.set(ex.rating ?? null);
      this.setupUsed.set(ex.setupUsed ?? '');
      this.lessons.set(ex.lessons ?? '');
      this.attachments.set(ex.attachments);
    }
  }

  // ===== Form actions =====

  setEmotionality(value: number): void {
    this.emotionality.set(value);
  }

  setRating(value: number | null): void {
    this.rating.set(value);
  }

  onSetupChange(value: string): void {
    this.setupUsed.set(value);
  }

  onLessonsChange(value: string): void {
    this.lessons.set(value);
  }

  async onSubmit(): Promise<void> {
    if (!this.canSubmit()) return;

    this.isSaving.set(true);
    this.errorMessage.set(null);
    this.fieldErrors.set({});

    const payload: UpsertTradeReviewRequest = {
      emotionality: this.emotionality(),
      rating: this.rating(),
      setupUsed: this.setupUsed().trim() || null,
      lessons: this.lessons().trim() || null,
    };

    try {
      const dto = await this.api.upsert(this.tradeId, payload);
      this.saved.emit(dto);
    } catch (e) {
      this.handleError(this.toError(e));
    } finally {
      this.isSaving.set(false);
    }
  }

  onCancel(): void {
    this.cancelled.emit();
  }

  // ===== Attachment upload =====

  async onFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    await this.handleFile(file);
    input.value = ''; // reset for re-upload same name
  }

  async onDrop(event: DragEvent): Promise<void> {
    event.preventDefault();
    const file = event.dataTransfer?.files?.[0];
    if (!file) return;
    await this.handleFile(file);
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
  }

  // Public for test convenience — the test suite drives this method with
  // synthetic File blobs to cover the client-side guards without needing a
  // full browser event shim.
  async handleFilePublicForTest(file: File): Promise<void> {
    return this.handleFile(file);
  }

  private async handleFile(file: File): Promise<void> {
    this.uploadErrorMessage.set(null);

    // 1) Client-side cap validation: server tamien enforce, pero feedback inmediato.
    if (!ALLOWED_ATTACHMENT_CONTENT_TYPES.has(file.type)) {
      this.uploadErrorMessage.set(
        `Tipo de archivo no permitido (${file.type || 'desconocido'}). Use PNG, JPEG, WebP o PDF.`
      );
      return;
    }
    if (file.size > MAX_ATTACHMENT_SIZE_BYTES) {
      this.uploadErrorMessage.set(
        `El archivo excede el limite de ${MAX_ATTACHMENT_SIZE_BYTES / (1024 * 1024)} MB.`
      );
      return;
    }
    if (!this.canUploadMore()) {
      this.uploadErrorMessage.set(
        `Ya alcanzaste el maximo de ${MAX_ATTACHMENTS_PER_REVIEW} attachments para este review.`
      );
      return;
    }

    this.isUploadingFile.set(true);
    try {
      // 2) Slot request: backend persiste la row pending + devuelve presigned URL.
      const slot = await this.api.requestUpload(this.tradeId, {
        contentType: file.type,
        sizeBytes: file.size,
        filename: file.name,
      });

      // 3) PUT directo a MinIO. El backend nunca toca los bytes.
      const ok = await uploadBytesToMinio(slot.putUrl, file, file.type);
      if (!ok) {
        this.uploadErrorMessage.set(
          'MinIO rechazo la subida. Reintenta o contacta a soporte.'
        );
        return;
      }

      // 4) Complete: backend verifica tamano contra MinIO + flip status.
      const confirmed = await this.api.confirmUpload(
        this.tradeId,
        slot.attachmentId,
        undefined
      );

      this.attachments.update((list) => [...list, confirmed]);
    } catch (e) {
      this.uploadErrorMessage.set(this.toError(e).message);
    } finally {
      this.isUploadingFile.set(false);
    }
  }

  async onDeleteAttachment(attachmentId: string): Promise<void> {
    // No pedimos confirmacion nativa window.confirm: el caller (parent tab)
    // maneja la confirmacion via dialog state.
    try {
      await this.api.deleteAttachment(this.tradeId, attachmentId);
      this.attachments.update((list) => list.filter((a) => a.id !== attachmentId));
    } catch (e) {
      this.uploadErrorMessage.set(this.toError(e).message);
    }
  }

  // ===== Helpers =====

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  isImage(contentType: string): boolean {
    return contentType.startsWith('image/');
  }

  private toError(e: unknown): TradeReviewError {
    // Reuse the service's normalizer indirectly (it lives in trade-review.service).
    if (e instanceof Error) return { status: 0, code: 'unknown', message: e.message };
    return { status: 0, code: 'unknown', message: 'Error inesperado.' };
  }

  private handleError(err: TradeReviewError): void {
    this.errorMessage.set(err.message);
    if (err.code.startsWith('validation.trade_review.rating_out_of_range')) {
      this.fieldErrors.update((m) => ({ ...m, rating: err.message }));
    } else if (err.code.startsWith('validation.trade_review.setup_used_too_long')) {
      this.fieldErrors.update((m) => ({ ...m, setupUsed: err.message }));
    } else if (err.code.startsWith('validation.trade_review.lessons_too_long')) {
      this.fieldErrors.update((m) => ({ ...m, lessons: err.message }));
    }
  }
}
