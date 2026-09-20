using ChatApp.Api.Extensions;
using ChatApp.Application.Abstractions;
using ChatApp.Application.Common.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ChatApp.Api.Controllers;

[ApiController]
[Route("api/attachments")]
[Authorize]
public sealed class AttachmentsController(IAttachmentService attachments) : ControllerBase
{
    // Slightly above the 10 MB content cap to leave room for multipart framing.
    private const long MaxRequestBytes = 12_000_000;

    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<ActionResult<MessageAttachmentDto>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null)
            throw new ValidationAppException("A file is required.");
        await using var stream = file.OpenReadStream();
        var result = await attachments.UploadAsync(User.GetUserId(), file.FileName, stream, file.Length, ct);
        return CreatedAtAction(nameof(Download), new { id = result.Id }, result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id, CancellationToken ct)
    {
        var download = await attachments.GetDownloadAsync(User.GetUserId(), id, ct);
        return File(download.Content, download.ContentType, download.FileName);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await attachments.DeleteAsync(User.GetUserId(), id, ct);
        return NoContent();
    }
}
