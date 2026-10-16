using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// The body of the two "parse test questions" endpoints — teacher portal
/// (<c>POST /api/teacher/assignments/questions/parse</c>) and admin
/// (<c>POST /api/admin/assignments/questions/parse</c>). Each controller keeps its own
/// gate; this only reads the request. docs/modules/test-import.md.
///
/// <para>
/// One endpoint, two bodies: multipart with <c>file</c> (.xlsx/.docx/.txt/.json, ≤ 10 MB) —
/// or a <c>text</c> form field — and JSON <c>{ "text": "…" }</c> for pasted text. Nothing is
/// written; 400 <c>{ message }</c> when there is nothing usable to read.
/// </para>
/// <para>
/// The body is read by hand (no bound parameter) because model binding would pin the
/// endpoint to one content type. A multipart form is still read by MVC's form value
/// provider before the action, and a broken one gets MVC's own validation 400 there; the
/// JSON body is read only here, so its read failures (too large, cut off) are caught below.
/// </para>
/// </summary>
internal static class AssignmentQuestionParsing
{
    public static async Task<ActionResult<AssignmentQuestionParseResultDto>> ParseAsync(
        HttpRequest request, CancellationToken ct)
    {
        AssignmentQuestionParseOutcome outcome;
        try
        {
            outcome = request.HasFormContentType
                ? await FromFormAsync(request, ct)
                : await FromJsonAsync(request, ct);
        }
        catch (BadHttpRequestException ex)
        {
            // Past the request ceiling (413), or a body that ended early.
            return Invalid(ex.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? AssignmentQuestionImport.TooBigMessage
                : AssignmentQuestionImport.BadUploadMessage);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // A broken or truncated multipart body, or one past the form limits.
            return Invalid(AssignmentQuestionImport.BadUploadMessage);
        }

        return outcome.FatalMessage is { } fatal ? Invalid(fatal) : outcome.Result!;
    }

    private static async Task<AssignmentQuestionParseOutcome> FromFormAsync(HttpRequest request, CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null) return AssignmentQuestionImport.FromText(form["text"].ToString());

        if (AssignmentQuestionImport.RejectFile(file.FileName, file.Length) is { } rejected)
            return AssignmentQuestionParseOutcome.Fatal(rejected);
        await using var stream = file.OpenReadStream();
        return AssignmentQuestionImport.FromFile(file.FileName, stream);
    }

    private static async Task<AssignmentQuestionParseOutcome> FromJsonAsync(HttpRequest request, CancellationToken ct)
    {
        ParseQuestionsRequest? body;
        try
        {
            body = await request.ReadFromJsonAsync<ParseQuestionsRequest>(ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Not JSON, or not a JSON content type — to the user, nothing was sent.
            body = null;
        }
        return AssignmentQuestionImport.FromText(body?.Text);
    }

    private static BadRequestObjectResult Invalid(string message) => new(new { message });
}
