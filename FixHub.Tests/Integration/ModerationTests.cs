using System.Net;
using System.Net.Http.Headers;
using FixHub.Api.DTOs;
using Xunit;

namespace FixHub.Tests.Integration;

[Collection("api")]
public class ModerationTests : IntegrationTestBase
{
    public ModerationTests(ApiFixture fixture) : base(fixture)
    {
    }

    private async Task<List<AuditLogDto>> AuditFor(string admin, int targetId, string targetType)
    {
        var response = await Client.CallAsync(HttpMethod.Get, "/api/admin/audit?pageSize=100", token: admin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.ReadAsync<PagedResult<AuditLogDto>>();
        return page.Items.Where(a => a.TargetId == targetId && a.TargetType == targetType).ToList();
    }

    private async Task<HttpResponseMessage> UploadAsync(string token, byte[] bytes, string fileName, string kind = "Passport")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", fileName);
        content.Add(new StringContent(kind), "kind");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/performer/verification/documents") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(request);
    }

    private static readonly byte[] PngBytes =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

    // ----- журнал

    [Fact]
    public async Task Admin_actions_are_written_to_audit_log()
    {
        var admin = await LoginAdminAsync();
        var (_, performerId) = await RegisterAsync("Performer");

        await Client.CallAsync(HttpMethod.Post, $"/api/admin/users/{performerId}/verify", token: admin);
        await Client.CallAsync(HttpMethod.Post, $"/api/admin/users/{performerId}/block", token: admin);

        var actions = (await AuditFor(admin, performerId, "User")).Select(a => a.Action).ToList();
        Assert.Contains("user.verify", actions);
        Assert.Contains("user.block", actions);
    }

    [Fact]
    public async Task Audit_log_is_for_admin_only()
    {
        var (client, _) = await RegisterAsync("Client");

        var response = await Client.CallAsync(HttpMethod.Get, "/api/admin/audit", token: client);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ----- жалобы

    [Fact]
    public async Task Client_complains_and_admin_resolves_with_block()
    {
        var admin = await LoginAdminAsync();
        var (client, _) = await RegisterAsync("Client");
        var (_, performerId) = await RegisterAsync("Performer");

        var created = await Client.CallAsync(HttpMethod.Post, "/api/complaints",
            new { kind = "Performer", targetId = performerId, reason = "fraud", comment = "Взял предоплату и пропал" }, client);
        Assert.Equal(HttpStatusCode.NoContent, created.StatusCode);

        // повторная жалоба, пока первая не рассмотрена
        var duplicate = await Client.CallAsync(HttpMethod.Post, "/api/complaints",
            new { kind = "Performer", targetId = performerId, reason = "fraud" }, client);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var list = await Client.CallAsync(HttpMethod.Get, "/api/admin/complaints?status=New&pageSize=100", token: admin);
        var complaint = (await list.ReadAsync<PagedResult<ComplaintDto>>()).Items.Single(c => c.PerformerId == performerId);
        Assert.Equal("fraud", complaint.Reason);

        var resolved = await Client.CallAsync(HttpMethod.Post, $"/api/admin/complaints/{complaint.Id}/resolve",
            new { note = "Проверили, блокируем", blockPerformer = true }, admin);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal("Resolved", (await resolved.ReadAsync<ComplaintDto>()).Status);

        // мастер заблокирован, а его публичная страница недоступна
        var page = await Client.CallAsync(HttpMethod.Get, $"/api/performers/{performerId}");
        Assert.Equal(HttpStatusCode.NotFound, page.StatusCode);

        // повторное решение запрещено
        var again = await Client.CallAsync(HttpMethod.Post, $"/api/admin/complaints/{complaint.Id}/reject", new { }, admin);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        var actions = (await AuditFor(admin, complaint.Id, "Complaint")).Select(a => a.Action);
        Assert.Contains("complaint.resolve", actions);
    }

    [Theory]
    [InlineData("Performer", "nonsense")]
    [InlineData("Performer", "other")]
    [InlineData("Something", "spam")]
    public async Task Invalid_complaint_is_rejected(string kind, string reason)
    {
        var (client, _) = await RegisterAsync("Client");
        var (_, performerId) = await RegisterAsync("Performer");

        var response = await Client.CallAsync(HttpMethod.Post, "/api/complaints",
            new { kind, targetId = performerId, reason }, client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Complaint_about_missing_review_is_not_found()
    {
        var (client, _) = await RegisterAsync("Client");

        var response = await Client.CallAsync(HttpMethod.Post, "/api/complaints",
            new { kind = "Review", targetId = 999999, reason = "spam" }, client);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ----- документы

    [Fact]
    public async Task Performer_uploads_document_and_admin_can_open_it()
    {
        var admin = await LoginAdminAsync();
        var (performer, performerId) = await RegisterAsync("Performer");

        var upload = await UploadAsync(performer, PngBytes, "passport.png");
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var document = await upload.ReadAsync<VerificationDocumentDto>();
        Assert.Equal("image/png", document.ContentType);

        var forAdmin = await Client.CallAsync(HttpMethod.Get, $"/api/admin/users/{performerId}/documents", token: admin);
        Assert.Single(await forAdmin.ReadAsync<List<VerificationDocumentDto>>());

        var file = await Client.CallAsync(HttpMethod.Get, $"/api/verification/documents/{document.Id}/file", token: admin);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/png", file.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Document_must_be_jpg_png_or_pdf()
    {
        var (performer, _) = await RegisterAsync("Performer");

        var response = await UploadAsync(performer, System.Text.Encoding.UTF8.GetBytes("просто текст"), "fake.png");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Document_is_hidden_from_other_users()
    {
        var (owner, _) = await RegisterAsync("Performer");
        var (stranger, _) = await RegisterAsync("Performer");
        var (client, _) = await RegisterAsync("Client");

        var document = await (await UploadAsync(owner, PngBytes, "passport.png")).ReadAsync<VerificationDocumentDto>();

        var byStranger = await Client.CallAsync(HttpMethod.Get, $"/api/verification/documents/{document.Id}/file", token: stranger);
        Assert.Equal(HttpStatusCode.NotFound, byStranger.StatusCode);

        var byClient = await Client.CallAsync(HttpMethod.Get, $"/api/verification/documents/{document.Id}/file", token: client);
        Assert.Equal(HttpStatusCode.Forbidden, byClient.StatusCode);

        var anonymous = await Client.CallAsync(HttpMethod.Get, $"/api/verification/documents/{document.Id}/file");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var byOwner = await Client.CallAsync(HttpMethod.Get, $"/api/verification/documents/{document.Id}/file", token: owner);
        Assert.Equal(HttpStatusCode.OK, byOwner.StatusCode);
    }
}
