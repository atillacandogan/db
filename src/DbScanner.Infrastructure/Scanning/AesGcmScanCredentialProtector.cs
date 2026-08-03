using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DbScanner.Application.Models;
using DbScanner.Application.Ports;
using Microsoft.Extensions.Options;

namespace DbScanner.Infrastructure.Scanning;

internal sealed class AesGcmScanCredentialProtector(
    IOptions<CredentialProtectionOptions> options) : IScanCredentialProtector
{
    private const byte FormatVersion = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("dbscanner-scan-credentials-v1");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Protect(DatabaseAuthentication authentication)
    {
        ArgumentNullException.ThrowIfNull(authentication);

        var key = LoadKey();
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(authentication, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);

            var envelope = new byte[1 + NonceSize + TagSize + ciphertext.Length];
            envelope[0] = FormatVersion;
            nonce.CopyTo(envelope.AsSpan(1, NonceSize));
            tag.CopyTo(envelope.AsSpan(1 + NonceSize, TagSize));
            ciphertext.CopyTo(envelope.AsSpan(1 + NonceSize + TagSize));

            return Convert.ToBase64String(envelope);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    public DatabaseAuthentication Unprotect(string protectedCredentials)
    {
        if (string.IsNullOrWhiteSpace(protectedCredentials))
        {
            throw new InvalidOperationException("Protected scan credentials are required.");
        }

        byte[] envelope;
        try
        {
            envelope = Convert.FromBase64String(protectedCredentials);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Protected scan credentials are invalid.");
        }

        if (envelope.Length <= 1 + NonceSize + TagSize || envelope[0] != FormatVersion)
        {
            throw new InvalidOperationException("Protected scan credentials are invalid.");
        }

        var key = LoadKey();
        var nonce = envelope.AsSpan(1, NonceSize);
        var tag = envelope.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = envelope.AsSpan(1 + NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData);

            return JsonSerializer.Deserialize<DatabaseAuthentication>(plaintext, JsonOptions)
                ?? throw new InvalidOperationException("Protected scan credentials are invalid.");
        }
        catch (CryptographicException)
        {
            throw new InvalidOperationException("Protected scan credentials could not be decrypted.");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Protected scan credentials are invalid.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private byte[] LoadKey()
    {
        var configuredKey = options.Value.Key;
        if (string.IsNullOrWhiteSpace(configuredKey)
            && !string.IsNullOrWhiteSpace(options.Value.KeyFile))
        {
            try
            {
                configuredKey = File.ReadAllText(options.Value.KeyFile).Trim();
            }
            catch (Exception exception) when (
                exception is IOException
                    or UnauthorizedAccessException
                    or ArgumentException)
            {
                throw new InvalidOperationException("Credential protection key file could not be read.");
            }
        }

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            throw new InvalidOperationException("Credential protection key is not configured.");
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configuredKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Credential protection key is invalid.");
        }

        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new InvalidOperationException("Credential protection key must contain exactly 32 bytes.");
        }

        return key;
    }
}
