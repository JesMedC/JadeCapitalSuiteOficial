namespace JadeCapital.Trading.Domain.TradeAttachments;

/// <summary>
/// Status finito del attachment.
///
/// <c>Pending</c> — slot creado (POST /attachments) pero los bytes todavia
/// no se subieron a MinIO. El cliente debe hacer PUT directo a la presigned
/// URL y luego llamar a complete.
///
/// <c>Uploaded</c> — CompleteAsync confirmo que el objeto existe en MinIO
/// con el tamano esperado (y sha256 si fue provisto). El agregado emite
/// <c>TradeAttachmentUploadedDomainEvent</c>.
///
/// <c>Failed</c> — CompleteAsync rechazo el upload (tamano no matchea,
/// sha256 no matchea, o el object no existe). Estado terminal.
/// </summary>
public enum TradeAttachmentStatus : byte
{
    Pending  = 0,
    Uploaded = 1,
    Failed   = 2,
}
