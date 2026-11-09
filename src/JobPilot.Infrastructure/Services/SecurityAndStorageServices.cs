using System.Security.Cryptography;
using System.Text;
using JobPilot.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace JobPilot.Infrastructure.Services;

public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public class CurrentUser : ICurrentUser
{
    public static readonly Guid DefaultUserId = new("0193a0c2-7d1e-7c4a-9a51-2f0b1c9e0001");

    public Guid UserId { get; private set; } = DefaultUserId;
    public string Email { get; private set; } = "pranjal@jobpilot.local";
    public bool IsAuthenticated => true;

    public void SetUser(Guid userId, string email)
    {
        UserId = userId;
        Email = email;
    }
}

public class DpapiSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DpapiSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("JobPilot.Secrets.v1");
    }

    public byte[] Protect(byte[] plaintext) => _protector.Protect(plaintext);

    public byte[] Unprotect(byte[] protectedData) => _protector.Unprotect(protectedData);

    public string ProtectString(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var protectedBytes = Protect(bytes);
        return Convert.ToBase64String(protectedBytes);
    }

    public string UnprotectString(string protectedData)
    {
        if (string.IsNullOrEmpty(protectedData)) return string.Empty;
        var bytes = Convert.FromBase64String(protectedData);
        var unprotected = Unprotect(bytes);
        return Encoding.UTF8.GetString(unprotected);
    }
}

public class LocalEncryptedFileStore : IFileStore
{
    private readonly string _basePath;
    private readonly ISecretProtector _protector;

    public LocalEncryptedFileStore(ISecretProtector protector)
    {
        _protector = protector;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _basePath = Path.Combine(localAppData, "JobPilot", "files");
        Directory.CreateDirectory(_basePath);
    }

    public async Task<string> SaveEncryptedAsync(Guid userId, string category, Guid fileId, Stream content, CancellationToken ct)
    {
        var userDir = Path.Combine(_basePath, userId.ToString(), category);
        Directory.CreateDirectory(userDir);

        var filePath = Path.Combine(userDir, $"{fileId}.bin");

        using var memory = new MemoryStream();
        await content.CopyToAsync(memory, ct);
        var plaintextBytes = memory.ToArray();

        var encryptedBytes = _protector.Protect(plaintextBytes);
        await File.WriteAllBytesAsync(filePath, encryptedBytes, ct);

        return filePath;
    }

    public async Task<Stream> OpenReadEncryptedAsync(string storageReference, CancellationToken ct)
    {
        if (!File.Exists(storageReference))
            throw new FileNotFoundException("File not found at reference.", storageReference);

        var encryptedBytes = await File.ReadAllBytesAsync(storageReference, ct);
        var decryptedBytes = _protector.Unprotect(encryptedBytes);

        return new MemoryStream(decryptedBytes);
    }

    public Task DeleteAsync(string storageReference, CancellationToken ct)
    {
        if (File.Exists(storageReference))
        {
            File.Delete(storageReference);
        }
        return Task.CompletedTask;
    }
}

