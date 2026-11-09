using System.Security.Cryptography;
using JobPilot.Application.Abstractions;
using JobPilot.Domain.Entities;
using JobPilot.Domain.Enums;
using JobPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobPilot.Infrastructure.Services;

public class ResumeService : IResumeService
{
    private static readonly byte[] PdfMagic = [0x25, 0x50, 0x44, 0x46]; // %PDF
    private static readonly byte[] DocxMagic = [0x50, 0x4B, 0x03, 0x04]; // PK..
    private const long MaxSizeBytes = 5 * 1024 * 1024; // 5 MB

    private readonly JobPilotDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IFileStore _fileStore;

    public ResumeService(
        JobPilotDbContext db,
        ICurrentUser currentUser,
        IFileStore fileStore)
    {
        _db = db;
        _currentUser = currentUser;
        _fileStore = fileStore;
    }

    public async Task<IReadOnlyList<ResumeDto>> ListAsync(CancellationToken ct)
    {
        return await _db.Resumes
            .Where(r => r.UserId == _currentUser.UserId && r.DeletedAt == null)
            .OrderByDescending(r => r.IsDefault)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => new ResumeDto(
                r.ResumeId,
                r.FileName,
                r.ContentType,
                r.SizeBytes,
                r.Sha256,
                r.Version,
                r.ResumeType,
                r.Label,
                r.IsActive,
                r.IsDefault,
                r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<ResumeDto> UploadAsync(
        Stream content,
        string fileName,
        string contentType,
        ResumeMetadata meta,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("Filename cannot be empty.", nameof(fileName));

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, ct);

        if (memory.Length > MaxSizeBytes)
            throw new InvalidOperationException($"File exceeds maximum allowed size of {MaxSizeBytes / (1024 * 1024)} MB.");

        var buffer = memory.ToArray();

        // Magic byte verification
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".pdf")
        {
            if (buffer.Length < 4 || !buffer.Take(4).SequenceEqual(PdfMagic))
                throw new InvalidOperationException("Invalid PDF file content.");
        }
        else if (ext == ".docx")
        {
            if (buffer.Length < 4 || !buffer.Take(4).SequenceEqual(DocxMagic))
                throw new InvalidOperationException("Invalid DOCX file content.");
        }
        else
        {
            throw new InvalidOperationException("Only .pdf and .docx files are permitted.");
        }

        var hashBytes = SHA256.HashData(buffer);
        var sha256 = Convert.ToHexStringLower(hashBytes);

        var resumeId = Guid.CreateVersion7();
        memory.Position = 0;
        var storageRef = await _fileStore.SaveEncryptedAsync(_currentUser.UserId, "resumes", resumeId, memory, ct);

        // Check version count
        var version = await _db.Resumes
            .Where(r => r.UserId == _currentUser.UserId)
            .CountAsync(ct) + 1;

        // If meta.IsDefault is true, clear existing default
        if (meta.IsDefault)
        {
            var existingDefaults = await _db.Resumes
                .Where(r => r.UserId == _currentUser.UserId && r.IsDefault && r.DeletedAt == null)
                .ToListAsync(ct);

            foreach (var def in existingDefaults)
            {
                def.IsDefault = false;
            }
        }
        else
        {
            // If this is the user's first resume, make it default automatically
            var hasAnyDefault = await _db.Resumes
                .AnyAsync(r => r.UserId == _currentUser.UserId && r.IsDefault && r.DeletedAt == null, ct);

            if (!hasAnyDefault)
            {
                meta = meta with { IsDefault = true };
            }
        }

        var resume = new Resume
        {
            ResumeId = resumeId,
            UserId = _currentUser.UserId,
            FileName = Path.GetFileName(fileName),
            StorageReference = storageRef,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            Sha256 = sha256,
            Version = version,
            ResumeType = meta.ResumeType,
            Label = meta.Label,
            IsActive = meta.IsActive,
            IsDefault = meta.IsDefault
        };

        _db.Resumes.Add(resume);
        await _db.SaveChangesAsync(ct);

        return new ResumeDto(
            resume.ResumeId,
            resume.FileName,
            resume.ContentType,
            resume.SizeBytes,
            resume.Sha256,
            resume.Version,
            resume.ResumeType,
            resume.Label,
            resume.IsActive,
            resume.IsDefault,
            resume.CreatedAt);
    }

    public async Task<ResumeDto> UpdateAsync(Guid resumeId, ResumeMetadata meta, CancellationToken ct)
    {
        var resume = await _db.Resumes
            .FirstOrDefaultAsync(r => r.ResumeId == resumeId && r.UserId == _currentUser.UserId && r.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Resume with id '{resumeId}' not found.");

        if (meta.IsDefault && !resume.IsDefault)
        {
            var existingDefaults = await _db.Resumes
                .Where(r => r.UserId == _currentUser.UserId && r.IsDefault && r.DeletedAt == null)
                .ToListAsync(ct);

            foreach (var def in existingDefaults)
            {
                def.IsDefault = false;
            }
        }

        resume.ResumeType = meta.ResumeType;
        resume.Label = meta.Label;
        resume.IsActive = meta.IsActive;
        resume.IsDefault = meta.IsDefault;

        await _db.SaveChangesAsync(ct);

        return new ResumeDto(
            resume.ResumeId,
            resume.FileName,
            resume.ContentType,
            resume.SizeBytes,
            resume.Sha256,
            resume.Version,
            resume.ResumeType,
            resume.Label,
            resume.IsActive,
            resume.IsDefault,
            resume.CreatedAt);
    }

    public async Task DeleteAsync(Guid resumeId, CancellationToken ct)
    {
        var resume = await _db.Resumes
            .FirstOrDefaultAsync(r => r.ResumeId == resumeId && r.UserId == _currentUser.UserId && r.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Resume with id '{resumeId}' not found.");

        // Check if referenced by any application
        var isReferenced = await _db.JobApplications.AnyAsync(a => a.ResumeId == resumeId, ct);
        if (isReferenced)
        {
            // Soft delete
            resume.DeletedAt = DateTimeOffset.UtcNow;
            resume.IsDefault = false;
            resume.IsActive = false;
        }
        else
        {
            // Hard delete
            await _fileStore.DeleteAsync(resume.StorageReference, ct);
            _db.Resumes.Remove(resume);
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<FileHandle> OpenForUploadAsync(Guid resumeId, CancellationToken ct)
    {
        var resume = await _db.Resumes
            .FirstOrDefaultAsync(r => r.ResumeId == resumeId && r.UserId == _currentUser.UserId && r.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Resume with id '{resumeId}' not found.");

        var decryptedStream = await _fileStore.OpenReadEncryptedAsync(resume.StorageReference, ct);

        // Create temporary file with original extension in a secure temp folder
        var tempDir = Path.Combine(Path.GetTempPath(), "JobPilot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, resume.FileName);

        using (var fileStream = File.Create(tempFile))
        {
            await decryptedStream.CopyToAsync(fileStream, ct);
        }

        decryptedStream.Position = 0;

        void Cleanup()
        {
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        return new FileHandle(tempFile, decryptedStream, Cleanup);
    }

    public async Task<Stream> OpenForReadAsync(Guid resumeId, CancellationToken ct)
    {
        var resume = await _db.Resumes
            .FirstOrDefaultAsync(r => r.ResumeId == resumeId && r.UserId == _currentUser.UserId && r.DeletedAt == null, ct)
            ?? throw new KeyNotFoundException($"Resume with id '{resumeId}' not found.");

        return await _fileStore.OpenReadEncryptedAsync(resume.StorageReference, ct);
    }
}

