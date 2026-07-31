using System.Net;
using AutoSaveGame.Core.Models;
using Google;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Upload;

namespace AutoSaveGame.Infrastructure.GoogleDrive;

public sealed class GoogleDriveGateway(Func<DriveService> getService) : IDriveGateway
{
    private readonly Func<DriveService> getService = getService
        ?? throw new ArgumentNullException(nameof(getService));

    public async Task<IReadOnlyList<DriveItem>> ListAsync(
        DriveListSpec spec,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<DriveItem>();
            string? pageToken = null;
            do
            {
                var request = getService().Files.List();
                request.Spaces = spec.Spaces;
                request.Q = spec.Query;
                request.Fields = $"nextPageToken,{spec.Fields}";
                request.PageToken = pageToken;
                var response = await request.ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false);
                results.AddRange((response.Files ?? []).Select(Map));
                pageToken = response.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return results;
        }
        catch (Exception exception) when (IsCloudException(exception))
        {
            throw MapException(exception);
        }
    }

    public async Task<DriveItem> UploadAsync(
        DriveUploadSpec spec,
        Stream content,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = spec.Name,
                Parents = spec.Parents.ToList(),
            };
            var request = getService().Files.Create(
                metadata,
                content,
                spec.ContentType);
            request.Fields = spec.Fields;
            var progress = await request.UploadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (progress.Status != UploadStatus.Completed || request.ResponseBody is null)
            {
                throw progress.Exception
                    ?? new IOException("Google Drive upload did not complete.");
            }

            return Map(request.ResponseBody);
        }
        catch (Exception exception) when (IsCloudException(exception))
        {
            throw MapException(exception);
        }
    }

    public async Task DownloadAsync(
        string fileId,
        Stream destination,
        CancellationToken cancellationToken)
    {
        try
        {
            var progress = await getService().Files.Get(fileId)
                .DownloadAsync(destination, cancellationToken)
                .ConfigureAwait(false);
            if (progress.Status != DownloadStatus.Completed)
            {
                throw progress.Exception
                    ?? new IOException("Google Drive download did not complete.");
            }
        }
        catch (GoogleApiException exception) when (
            exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new CloudObjectNotFoundException(fileId);
        }
        catch (Exception exception) when (IsCloudException(exception))
        {
            throw MapException(exception);
        }
    }

    public async Task DeleteAsync(
        string fileId,
        CancellationToken cancellationToken)
    {
        try
        {
            await getService().Files.Delete(fileId)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (GoogleApiException exception) when (
            exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new CloudObjectNotFoundException(fileId);
        }
        catch (Exception exception) when (IsCloudException(exception))
        {
            throw MapException(exception);
        }
    }

    private static DriveItem Map(Google.Apis.Drive.v3.Data.File file) =>
        new(
            file.Id ?? throw new InvalidDataException("Drive response is missing file ID."),
            file.Name ?? throw new InvalidDataException("Drive response is missing file name."),
            file.Size ?? 0,
            file.CreatedTimeDateTimeOffset ?? DateTimeOffset.UnixEpoch,
            file.ModifiedTimeDateTimeOffset ?? DateTimeOffset.UnixEpoch);

    private static bool IsCloudException(Exception exception) =>
        exception is GoogleApiException or HttpRequestException or IOException;

    private static CloudStoreException MapException(Exception exception)
    {
        if (exception is GoogleApiException googleException)
        {
            var kind = googleException.HttpStatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    CloudStoreErrorKind.Authentication,
                HttpStatusCode.TooManyRequests or HttpStatusCode.InsufficientStorage =>
                    CloudStoreErrorKind.Quota,
                _ => CloudStoreErrorKind.Unknown,
            };
            return new CloudStoreException(
                kind,
                $"Google Drive request failed with HTTP {(int)googleException.HttpStatusCode}.",
                googleException);
        }

        return new CloudStoreException(
            CloudStoreErrorKind.Network,
            "Google Drive could not be reached.",
            exception);
    }
}
