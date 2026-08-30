import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AttachmentUsageDto, ThumbnailUrlDto } from './attachments.types';

@Injectable({ providedIn: 'root' })
export class AttachmentsService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/attachments';

  async getUsage(): Promise<AttachmentUsageDto> {
    return firstValueFrom(this.http.get<AttachmentUsageDto>(`${this.base}/usage`));
  }

  async getThumbnail(attachmentId: string, width = 256, height = 256): Promise<ThumbnailUrlDto> {
    return firstValueFrom(
      this.http.get<ThumbnailUrlDto>(
        `${this.base}/${attachmentId}/thumbnail`,
        { params: { width: String(width), height: String(height) } }
      )
    );
  }
}