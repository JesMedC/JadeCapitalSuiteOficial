export interface AttachmentUsageDto {
  totalBytes: number;
  attachmentCount: number;
  quotaBytes: number;
  quotaCount: number;
  percentFull: number;
}

export interface ThumbnailUrlDto {
  attachmentId: string;
  contentType: string;
  width: number;
  height: number;
  presignedUrl: string;
  expiresAt: string;
}