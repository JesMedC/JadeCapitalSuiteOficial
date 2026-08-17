/** Wire shape mirrors JadeCapital.Trading.Contracts.Imports.ImportJobDto. */

export interface ImportJobDto {
  id: string;
  userId: string;
  accountId: string;
  format: number;        // 0=Csv, 1=Mt4, 2=Mt5, 255=Unknown
  fileName: string;
  fileSizeBytes: number;
  fileSha256: string;
  status: number;        // 0=Pending, 1=InProgress, 2=Completed, 3=Failed, 4=Cancelled
  rowsTotal: number;
  rowsImported: number;
  rowsSkipped: number;
  rowsErrored: number;
  errorMessage: string | null;
  startedAt: string;
  finishedAt: string | null;
}

export interface BeginImportResponse {
  importJobId: string;
}

export const IMPORT_STATUS_LABELS: Readonly<Record<number, string>> = {
  0: 'Pendiente',
  1: 'Importando',
  2: 'Completado',
  3: 'Error',
  4: 'Cancelado',
};

export const IMPORT_FORMAT_LABELS: Readonly<Record<number, string>> = {
  0: 'CSV',
  1: 'MT4',
  2: 'MT5',
  255: 'Desconocido',
};