namespace JadeCapital.Shared.Kernel.Storage;

/// <summary>
/// Abstraccion para storage de attachments (S3-compatible). Vive en
/// <c>Shared.Kernel</c> para que las capas <c>Application</c> puedan
/// depender de la interfaz sin importar la implementacion (que vive
/// en <c>Shared.Infrastructure</c> con el SDK de MinIO). El patron es
/// el mismo que <c>IEmailSender</c> en Wave 0: la abstraccion en Kernel
/// (cross-module-safe), la implementacion en Infrastructure (puente al
/// SDK externo).
///
/// El contrato es el minimo necesario para el flujo de presigned URL
/// (slice 1d.1): generar URL para subir, eliminar object (delete), e
/// inicializar el bucket al startup. NO se exponen APIs de listar/leer:
/// el backend nunca lee el contenido del attachment — eso lo hace el
/// browser del trader apuntando directamente a MinIO cuando quiere
/// previsualizar (via presigned GET URL, fuera del scope de v1).
/// </summary>
public interface IAttachmentStorage
{
    /// <summary>
    /// Genera una URL PUT prefirmada para subir bytes al object <paramref name="objectKey"/>.
    /// El backend NO hace de proxy: el cliente sube DIRECTO al endpoint
    /// S3-compatible usando esta URL.
    /// </summary>
    Task<string> GetPresignedPutUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// Elimina un object del bucket. Best-effort: si el object no existe
    /// (porque el cliente nunca subio los bytes tras crear el slot), la
    /// operacion es un no-op desde el punto de vista del producto.
    /// </summary>
    Task DeleteAsync(string objectKey, CancellationToken ct = default);

    /// <summary>
    /// Verifica que el object existe y (opcionalmente) computa su SHA-256.
    /// Usado por <c>CompleteAttachmentUploadHandler</c> despues de que el
    /// cliente confirma la subida: el backend vuelve a hacer STAT contra
    /// MinIO para confirmar que el tamano (y sha si fue provisto) coincide
    /// con lo que el cliente declaro al pedir el slot.
    /// </summary>
    /// <returns>
    /// <c>true</c> si el object existe y el tamano coincide;
    /// <c>false</c> si el object no existe o el tamano no coincide.
    /// </returns>
    Task<bool> VerifyObjectExistsAsync(string objectKey, long expectedSizeBytes, CancellationToken ct = default);

    /// <summary>
    /// Provisiona el bucket si no existe. Idempotente: puede llamarse en
    /// cada startup. Usado por <c>MinioInitializerHostedService</c>.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);
}
