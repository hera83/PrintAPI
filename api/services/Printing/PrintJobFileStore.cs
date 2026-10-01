namespace api.Services.Printing;

/// <summary>On-disk storage for queued print documents, under <c>app_files/PrintJobs/</c>.</summary>
public class PrintJobFileStore
{
    public PrintJobFileStore(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }

    public string GetDocumentPath(Guid jobId) => Path.Combine(Directory, $"{jobId:N}.pdf");

    /// <summary>Temporary PWG Raster rendering of the document, only present while the job is being sent.</summary>
    public string GetRasterPath(Guid jobId) => Path.Combine(Directory, $"{jobId:N}.pwg");

    public async Task SaveDocumentAsync(Guid jobId, byte[] document, CancellationToken cancellationToken = default) =>
        await File.WriteAllBytesAsync(GetDocumentPath(jobId), document, cancellationToken);

    public void Delete(Guid jobId)
    {
        File.Delete(GetDocumentPath(jobId));
        File.Delete(GetRasterPath(jobId));
    }
}
